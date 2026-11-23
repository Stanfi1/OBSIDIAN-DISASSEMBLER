using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Obsidian.Analysis;
using Obsidian.App.Services;
using Obsidian.Core.Formatting;
using Obsidian.Core.Model;
using Obsidian.Core.Services;
using Obsidian.Projects;
using Serilog;

namespace Obsidian.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IBinaryImageLoader _loader;
    private readonly IDisassembler _disassembler;
    private readonly IBinaryAnalysisEngine _analysisEngine;
    private readonly IProjectStore _projectStore;
    private readonly ILogger _logger;
    private readonly ThemeService _theme;
    private readonly SettingsService _settings;
    private readonly RecentFilesService _recent;
    private readonly IDialogService _dialogs;

    private readonly Stack<ExplorerNode> _backStack = new();
    private readonly Stack<ExplorerNode> _forwardStack = new();
    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private ExplorerNode? _currentNode;

    public MainViewModel(
        IBinaryImageLoader loader,
        IDisassembler disassembler,
        IBinaryAnalysisEngine analysisEngine,
        IProjectStore projectStore,
        ILogger logger,
        ThemeService theme,
        SettingsService settings,
        RecentFilesService recent,
        UiLogSink logSink,
        IDialogService dialogs)
    {
        _loader = loader;
        _disassembler = disassembler;
        _analysisEngine = analysisEngine;
        _projectStore = projectStore;
        _logger = logger.ForContext<MainViewModel>();
        _theme = theme;
        _settings = settings;
        _recent = recent;
        _dialogs = dialogs;

        Explorer = new SolutionExplorerViewModel();
        Properties = new PropertiesViewModel();
        Output = new OutputViewModel(logSink, _logger);
        Status = new StatusBarViewModel
        {
            ThemeName = theme.Current == AppTheme.Dark ? "Dark" : "Light",
            Version = AppVersion,
        };

        Explorer.SelectionChanged += OnExplorerSelectionChanged;
        Explorer.NodeActivated += OnExplorerNodeActivated;
        Explorer.AnnotationRequested += OnAnnotationRequested;
        _recent.Changed += (_, _) => RefreshRecentMenu();
        RecentFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentFiles));

        RefreshRecentMenu();
        BuildPaletteCommands();
        RefreshPalette();

        _logger.Information(
            "Obsidian Disassembler {Version:l} started - {Runtime:l} - {Os:l}",
            AppVersion,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription);
    }

    public SolutionExplorerViewModel Explorer { get; }

    public PropertiesViewModel Properties { get; }

    public OutputViewModel Output { get; }

    public StatusBarViewModel Status { get; }

    [ObservableProperty]
    private HexEditorViewModel? _hexEditor;

    [ObservableProperty]
    private DisassemblyViewModel? _disassembly;

    [ObservableProperty]
    private AnalysisViewModel? _binaryAnalysis;

    [ObservableProperty]
    private FunctionGraphViewModel? _graph;

    public AnalysisResult? AnalysisResult { get; private set; }

    [ObservableProperty]
    private AnalysisBrowserViewModel? _analysisBrowser;

    [ObservableProperty]
    private CallGraphViewModel? _callGraph;

    private readonly Dictionary<uint, AddressAnnotation> _annotations = [];
    private readonly List<ProjectBookmark> _bookmarks = [];
    private readonly List<BinaryPatch> _patchOperations = [];
    private readonly Stack<BinaryPatch> _patchRedo = new();
    private byte[]? _baseBytes;
    private string? _baseSha256;
    private DateTimeOffset _projectCreatedUtc = DateTimeOffset.UtcNow;

    [ObservableProperty]
    private string? _projectPath;

    [ObservableProperty]
    private string? _projectName;

    [ObservableProperty]
    private bool _isProjectDirty;

    [ObservableProperty]
    private bool _hasPatches;

    public bool HasProject => !string.IsNullOrWhiteSpace(ProjectPath);

    public string ProjectDisplayName => ProjectName ?? "No project";

    public ObservableCollection<RecentFileEntry> RecentFiles => _recent.Items;

    public bool HasRecentFiles => RecentFiles.Count > 0;

    public ObservableCollection<RecentMenuEntry> RecentMenuItems { get; } = [];

    public ObservableCollection<PaletteCommand> PaletteCommands { get; } = [];

    public ObservableCollection<PaletteCommand> FilteredPaletteCommands { get; } = [];

    public IReadOnlyList<ShortcutInfo> Shortcuts { get; } =
    [
        new("Open binary", "Ctrl+O"),
        new("New project", "Ctrl+Shift+N"),
        new("Open project", "Ctrl+Shift+O"),
        new("Save project", "Ctrl+S"),
        new("Save project as", "Ctrl+Shift+S"),
        new("Analysis browser", "Ctrl+Shift+A"),
        new("Patch instruction", "Ctrl+E"),
        new("Undo patch", "Ctrl+Shift+Z"),
        new("Redo patch", "Ctrl+Shift+Y"),
        new("Save patched copy", "Ctrl+Shift+E"),
        new("Reload and re-analyze", "Ctrl+R"),
        new("Close binary", "Ctrl+W"),
        new("Go to address", "Ctrl+G"),
        new("Disassembly", "Ctrl+D"),
        new("Analysis browser", "Ctrl+Shift+A"),
        new("Call graph", "Ctrl+Shift+C"),
        new("Control-flow graph", "Ctrl+Shift+G"),
        new("Hex editor", "Ctrl+H"),
        new("Command palette", "Ctrl+Q"),
        new("Settings", "Ctrl+,"),
        new("Navigate back", "Alt+Left"),
        new("Navigate forward", "Alt+Right"),
    ];

    public string AppVersion =>
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public string RuntimeDescription => RuntimeInformation.FrameworkDescription;

    public string OsDescription => RuntimeInformation.OSDescription;

    public string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();

    public string ThemeLabel => _theme.Current == AppTheme.Dark ? "Dark" : "Light";

    public bool HasImage => Image is not null;

    public string WindowTitle => Image is null
        ? "Obsidian Disassembler"
        : $"{Image.FileName}{(HasPatches ? " [patched]" : string.Empty)}{(IsProjectDirty ? " *" : string.Empty)} — {ProjectName ?? "Obsidian Disassembler"}";

    [ObservableProperty]
    private BinaryImage? _image;

    [ObservableProperty]
    private OverviewViewModel? _overview;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPaletteOpen;

    [ObservableProperty]
    private string _paletteQuery = string.Empty;

    [ObservableProperty]
    private PaletteCommand? _selectedPaletteCommand;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    public event Action? BinaryLoaded;

    public event Action? BinaryClosed;

    public event Action? OpenHexRequested;

    public event Action? OpenDisassemblyRequested;

    public event Action? OpenAnalysisBrowserRequested;

    public event Action? OpenCallGraphRequested;

    public event Action<FunctionAnalysis>? OpenGraphRequested;

    public event Action<string>? PanelToggleRequested;

    public event Action? CloseActiveDocumentRequested;

    public async Task OpenFileAsync(string path, bool preserveProject = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!preserveProject)
        {
            ClearProjectState();
        }

        var fileName = Path.GetFileName(path);
        var version = ++_loadVersion;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        IsBusy = true;
        Status.IsBusy = true;
        Status.Progress = 0;
        Status.Message = $"Opening {fileName}…";

        _logger.Information("Opening binary {Path:l}", path);

        var progress = new Progress<LoadProgress>(update =>
        {
            if (_loadVersion != version)
            {
                return;
            }

            Status.Message = update.Message;
            if (update.Percent is { } percent)
            {
                Status.Progress = percent;
            }
        });

        try
        {
            var image = await _loader.LoadAsync(path, progress, cts.Token);

            if (_loadVersion != version)
            {
                return;
            }

            ApplyImage(image);
            _baseBytes = image.Bytes.ToArray();
            _baseSha256 = image.Sha256;
            _patchOperations.Clear();
            _patchRedo.Clear();
            HasPatches = false;
            _recent.Add(image.FilePath);

            Status.Message = $"Loaded {image.FileName}";
            Status.Progress = 100;

            _logger.Information(
                "Loaded {File:l}: {Format:l}, {Architecture:l}, {Sections} sections, {Imports} imported functions, {Findings} observation(s)",
                image.FileName,
                image.FormatText,
                image.ArchitectureText,
                image.Sections.Count,
                image.ImportedFunctionCount,
                image.Findings.Count);
        }
        catch (OperationCanceledException)
        {
            if (_loadVersion == version)
            {
                Status.Message = "Load cancelled.";
                Status.Progress = 0;
                _logger.Warning("Loading {File} was cancelled.", fileName);
            }
        }
        catch (BinaryLoadException ex)
        {
            if (_loadVersion == version)
            {
                Status.Message = ex.Message;
                Status.Progress = 0;
                _logger.Error(ex, "Could not load {Path}", path);
                _dialogs.ShowError("Could not open binary", ex.Message, ex);
            }
        }
        catch (Exception ex)
        {
            if (_loadVersion == version)
            {
                Status.Message = "The binary could not be analyzed.";
                Status.Progress = 0;
                _logger.Error(ex, "Unexpected failure while loading {Path}", path);
                _dialogs.ShowError(
                    "Unexpected error",
                    "The binary could not be analyzed. Details were written to the output log.",
                    ex);
            }
        }
        finally
        {
            if (_loadVersion == version)
            {
                IsBusy = false;
                Status.IsBusy = false;
                if (ReferenceEquals(_loadCts, cts))
                {
                    _loadCts = null;
                    cts.Dispose();
                }
            }
        }
    }

    [RelayCommand]
    private async Task OpenBinaryAsync()
    {
        if (!await ConfirmProjectChangeAsync())
        {
            return;
        }

        var path = _dialogs.PickBinaryFile();
        if (path is not null)
        {
            await OpenFileAsync(path);
        }
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!File.Exists(path))
        {
            Status.Message = "The recent binary no longer exists at that path.";
            _dialogs.ShowError(
                "Binary not found",
                $"The file no longer exists:\n{path}");
            return;
        }

        if (!await ConfirmProjectChangeAsync())
        {
            return;
        }

        await OpenFileAsync(path);
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (Image is null)
        {
            Status.Message = "Open a binary before creating a project.";
            return;
        }

        if (!await ConfirmProjectChangeAsync())
        {
            return;
        }

        var suggested = Path.GetFileNameWithoutExtension(Image.FileName) + ".obsproj";
        var path = _dialogs.PickProjectSaveFile(suggested);
        if (!string.IsNullOrWhiteSpace(path))
        {
            await CreateProjectAtAsync(path);
        }
    }

    public async Task<bool> CreateProjectAtAsync(string path)
    {
        if (Image is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var metadata = new ProjectMetadata(
                Path.GetFileNameWithoutExtension(path),
                Image.FilePath,
                _baseSha256 ?? Image.Sha256,
                now,
                now);

            await _projectStore.CreateAsync(path, metadata);
            _annotations.Clear();
            _bookmarks.Clear();
            _projectCreatedUtc = now;
            ProjectPath = path;
            ProjectName = metadata.Name;
            IsProjectDirty = false;
            Explorer.ApplyAnnotations(_annotations, _bookmarks);
            Graph?.UpdateLabels(_annotations);
            CallGraph?.UpdateLabels(_annotations);
            if (_patchOperations.Count > 0)
            {
                await _projectStore.SaveAsync(path, BuildProjectDocument(metadata));
            }

            Status.Message = $"Project created: {metadata.Name}";
            return true;
        }
        catch (ProjectStoreException ex)
        {
            _logger.Error(ex, "Project creation failed");
            _dialogs.ShowError("Could not create project", ex.Message, ex);
            return false;
        }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (!await ConfirmProjectChangeAsync())
        {
            return;
        }

        var path = _dialogs.PickProjectFile();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await OpenProjectFromPathAsync(path);
        }
    }

    public async Task<ProjectDocument?> OpenProjectFromPathAsync(string path)
    {
        try
        {
            var document = await _projectStore.OpenAsync(path);
            if (!File.Exists(document.Metadata.BinaryPath))
            {
                _dialogs.ShowError(
                    "Binary not found",
                    $"The project references a binary that no longer exists:\n{document.Metadata.BinaryPath}");
                return null;
            }

            await OpenFileAsync(document.Metadata.BinaryPath, preserveProject: true);
            if (Image is null)
            {
                return null;
            }

            if (!string.Equals(Image.Sha256, document.Metadata.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _dialogs.ShowInformation(
                    "Binary changed",
                    "The project hash does not match the current binary. Existing annotations are loaded but may refer to different code.");
            }

            await ApplyProjectAsync(path, document);
            Status.Message = $"Project opened: {document.Metadata.Name}";
            return document;
        }
        catch (ProjectStoreException ex)
        {
            _logger.Error(ex, "Project open failed");
            _dialogs.ShowError("Could not open project", ex.Message, ex);
            return null;
        }
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (Image is null)
        {
            Status.Message = "Open a binary before saving a project.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            await SaveProjectAsAsync();
            return;
        }

        await SaveProjectAtAsync(ProjectPath);
    }

    [RelayCommand]
    private async Task SaveProjectAsAsync()
    {
        if (Image is null)
        {
            Status.Message = "Open a binary before saving a project.";
            return;
        }

        var suggested = (ProjectName ?? Path.GetFileNameWithoutExtension(Image.FileName)) + ".obsproj";
        var path = _dialogs.PickProjectSaveFile(suggested);
        if (!string.IsNullOrWhiteSpace(path))
        {
            await SaveProjectAtAsync(path);
        }
    }

    public async Task<bool> SaveProjectAtAsync(string path)
    {
        if (Image is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var name = ProjectName ?? Path.GetFileNameWithoutExtension(path);
            var metadata = new ProjectMetadata(
                name,
                Image.FilePath,
                _baseSha256 ?? Image.Sha256,
                _projectCreatedUtc,
                now);

            await _projectStore.SaveAsync(path, BuildProjectDocument(metadata));
            ProjectPath = path;
            ProjectName = name;
            IsProjectDirty = false;
            Status.Message = $"Project saved: {name}";
            _logger.Information("Project saved to {Path:l}", path);
            return true;
        }
        catch (ProjectStoreException ex)
        {
            _logger.Error(ex, "Project save failed");
            _dialogs.ShowError("Could not save project", ex.Message, ex);
            return false;
        }
    }

    private ProjectDocument BuildProjectDocument(ProjectMetadata metadata) =>
        new()
        {
            Metadata = metadata,
            Annotations = _annotations.Values.OrderBy(annotation => annotation.Rva).ToArray(),
            Bookmarks = _bookmarks.OrderBy(bookmark => bookmark.Rva).ToArray(),
            Patches = _patchOperations.ToArray(),
        };

    private async Task ApplyProjectAsync(string path, ProjectDocument document)
    {
        _projectCreatedUtc = document.Metadata.CreatedUtc;
        ProjectPath = path;
        ProjectName = document.Metadata.Name;
        _annotations.Clear();
        foreach (var annotation in document.Annotations)
        {
            _annotations[annotation.Rva] = annotation;
        }

        _bookmarks.Clear();
        _bookmarks.AddRange(document.Bookmarks);
        _patchOperations.Clear();
        _patchOperations.AddRange(document.Patches);
        _patchRedo.Clear();
        if (_patchOperations.Count > 0)
        {
            var applied = await RebuildPatchedImageAsync("Project patches loaded");
            if (!applied)
            {
                _patchOperations.Clear();
                _dialogs.ShowError(
                    "Could not apply project patches",
                    "A persisted patch lies outside the current binary.");
            }
        }

        Explorer.ApplyAnnotations(_annotations, _bookmarks);
        Graph?.UpdateLabels(_annotations);
        CallGraph?.UpdateLabels(_annotations);
        HasPatches = _patchOperations.Count > 0;
        IsProjectDirty = false;
        RefreshSelectedProperties();
    }

    private void ClearProjectState()
    {
        _projectCreatedUtc = DateTimeOffset.UtcNow;
        ProjectPath = null;
        ProjectName = null;
        _annotations.Clear();
        _bookmarks.Clear();
        Explorer.ApplyAnnotations(_annotations, _bookmarks);
        Graph?.UpdateLabels(_annotations);
        CallGraph?.UpdateLabels(_annotations);
        IsProjectDirty = false;
    }

    private async Task<bool> ConfirmProjectChangeAsync()
    {
        if (!IsProjectDirty)
        {
            return true;
        }

        var decision = _dialogs.ShowSaveChanges(
            "Unsaved project changes",
            $"Project '{ProjectDisplayName}' has unsaved annotations. Save before continuing?");

        if (decision == SaveChangesDecision.Cancel)
        {
            return false;
        }

        if (decision == SaveChangesDecision.Save)
        {
            await SaveProjectCommand.ExecuteAsync(null);
            return !IsProjectDirty;
        }

        return true;
    }

    private void OnAnnotationRequested(ExplorerNode node, ExplorerAnnotationAction action)
    {
        var target = GetAnnotationTarget(node);
        if (target is not { } value)
        {
            Status.Message = "Select a function, string, reference or bookmark first.";
            return;
        }

        var existing = _annotations.GetValueOrDefault(value.Rva);
        switch (action)
        {
            case ExplorerAnnotationAction.RenameFunction when node.Payload is FunctionAnalysis function:
            {
                var name = _dialogs.PromptText(
                    "Rename function",
                    $"{function.Function.Name} at {Hex.Rva(value.Rva)}",
                    existing?.UserName ?? function.Function.Name);

                if (name is null)
                {
                    return;
                }

                UpdateAnnotation(value.Rva, name, existing?.Comment);
                break;
            }

            case ExplorerAnnotationAction.EditComment:
            {
                var comment = _dialogs.PromptText(
                    "Edit comment",
                    $"Comment for {Hex.Rva(value.Rva)}",
                    existing?.Comment ?? string.Empty,
                    multiline: true);

                if (comment is null)
                {
                    return;
                }

                UpdateAnnotation(value.Rva, existing?.UserName, comment);
                break;
            }

            case ExplorerAnnotationAction.AddBookmark:
            {
                var title = _dialogs.PromptText(
                    "Add bookmark",
                    $"Bookmark at {Hex.Rva(value.Rva)}",
                    $"{Hex.Rva(value.Rva)} · {value.Label}");

                if (string.IsNullOrWhiteSpace(title))
                {
                    return;
                }

                _bookmarks.Add(new ProjectBookmark(
                    0,
                    value.Rva,
                    title.Trim(),
                    null,
                    DateTimeOffset.UtcNow));
                MarkProjectDirty();
                Explorer.ApplyAnnotations(_annotations, _bookmarks);
                break;
            }

            case ExplorerAnnotationAction.RemoveBookmark when node.Payload is ProjectBookmark bookmark:
            {
                _bookmarks.Remove(bookmark);
                MarkProjectDirty();
                Explorer.ApplyAnnotations(_annotations, _bookmarks);
                break;
            }

            case ExplorerAnnotationAction.OpenCallGraph when node.Payload is FunctionAnalysis function:
                OpenCallGraphFor(function);
                break;

            default:
                Status.Message = "Select a function for rename or open call graph.";
                break;
        }
    }

    private void UpdateAnnotation(uint rva, string? userName, string? comment)
    {
        var normalizedName = string.IsNullOrWhiteSpace(userName) ? null : userName.Trim();
        var normalizedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        if (normalizedName is null && normalizedComment is null)
        {
            _annotations.Remove(rva);
        }
        else
        {
            _annotations[rva] = new AddressAnnotation(
                rva,
                normalizedName,
                normalizedComment,
                DateTimeOffset.UtcNow);
        }

        MarkProjectDirty();
        Explorer.ApplyAnnotations(_annotations, _bookmarks);
        Graph?.UpdateLabels(_annotations);
        CallGraph?.UpdateLabels(_annotations);
        RefreshSelectedProperties();
    }

    private (uint Rva, string Label)? GetAnnotationTarget(ExplorerNode node) => node.Payload switch
    {
        FunctionAnalysis function => (function.Function.Rva, function.Function.Name),
        StringLiteral value => (value.Rva, value.Preview),
        CrossReference reference => (reference.FromRva, reference.KindText),
        ProjectBookmark bookmark => (bookmark.Rva, bookmark.Title),
        _ => null,
    };

    private void MarkProjectDirty()
    {
        IsProjectDirty = true;
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void RefreshSelectedProperties()
    {
        if (Explorer.SelectedNode is { } node)
        {
            ShowProperties(node);
        }
    }

    [RelayCommand]
    private void OpenAnalysisBrowser()
    {
        if (Image is null)
        {
            Status.Message = "Open a binary before opening the analysis browser.";
            return;
        }

        if (AnalysisBrowser is null)
        {
            Status.Message = "Analysis is still running.";
            return;
        }

        OpenAnalysisBrowserRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenCallGraph()
    {
        if (Image is null || AnalysisResult is null)
        {
            Status.Message = "Open a binary and wait for analysis before opening the call graph.";
            return;
        }

        var function = FindCallGraphTarget();
        if (function is null)
        {
            Status.Message = "Select a function or an address inside a function first.";
            return;
        }

        if (CallGraph is null)
        {
            CallGraph = new CallGraphViewModel(Image, AnalysisResult, _annotations);
        }

        CallGraph.SelectFunction(function);
        OpenCallGraphRequested?.Invoke();
    }

    private FunctionAnalysis? FindCallGraphTarget()
    {
        if (Explorer.SelectedNode?.Payload is FunctionAnalysis selectedFunction)
        {
            return selectedFunction;
        }

        if (Graph?.SelectedFunction is { } graphFunction)
        {
            return graphFunction;
        }

        if (AnalysisBrowser?.SelectedFunction is { } browserFunction)
        {
            return browserFunction;
        }

        if (AnalysisResult is null)
        {
            return null;
        }

        var selectedRva = Explorer.SelectedNode?.Payload switch
        {
            StringLiteral value => value.Rva,
            CrossReference reference => reference.FromRva,
            ProjectBookmark bookmark => bookmark.Rva,
            _ => (uint?)null,
        };
        if (selectedRva is { } rva)
        {
            return AnalysisResult.FindFunctionContaining(rva);
        }

        return AnalysisResult.FindFunction(Image?.EntryPointRva ?? 0)
            ?? AnalysisResult.Functions.FirstOrDefault();
    }

    private void OpenCallGraphFor(FunctionAnalysis function)
    {
        if (Image is null || AnalysisResult is null)
        {
            return;
        }

        if (CallGraph is null)
        {
            CallGraph = new CallGraphViewModel(Image, AnalysisResult, _annotations);
        }

        CallGraph.SelectFunction(function);
        OpenCallGraphRequested?.Invoke();
    }

    [RelayCommand]
    private async Task PatchInstructionAsync()
    {
        if (Image is null || Disassembly?.SelectedInstruction is not { } row)
        {
            Status.Message = "Select an instruction in the disassembly first.";
            return;
        }

        var bytes = _dialogs.ShowPatchInstruction(row.Instruction, Image);
        if (bytes is not null)
        {
            await ApplyInstructionPatchAsync(row.Instruction, bytes);
        }
    }

    public async Task<bool> ApplyInstructionPatchAsync(DisassembledInstruction instruction, byte[] replacement)
    {
        if (Image is null || instruction.FileOffset is not { } offset || offset < 0)
        {
            Status.Message = "The selected instruction has no file-backed bytes.";
            return false;
        }

        if (replacement.Length != instruction.Bytes.Length)
        {
            Status.Message = "The replacement must have the same length as the original instruction.";
            return false;
        }

        if (Image.Bytes.Span.Slice(checked((int)offset), replacement.Length).SequenceEqual(replacement))
        {
            Status.Message = "The replacement bytes are identical to the original instruction.";
            return false;
        }

        var original = Image.Bytes.Slice(checked((int)offset), replacement.Length).ToArray();
        _patchOperations.Add(new BinaryPatch(
            0,
            instruction.Rva,
            offset,
            original,
            replacement.ToArray(),
            DateTimeOffset.UtcNow));
        _patchRedo.Clear();
        var applied = await RebuildPatchedImageAsync("Instruction patched");
        if (applied)
        {
            if (HasProject)
            {
                MarkProjectDirty();
            }

            _logger.Information(
                "Patched {Length} instruction byte(s) at RVA {Rva}",
                replacement.Length,
                Hex.Rva(instruction.Rva));
        }

        return applied;
    }

    private async Task<bool> RebuildPatchedImageAsync(string message)
    {
        if (Image is null || _baseBytes is null)
        {
            return false;
        }

        var bytes = _baseBytes.ToArray();
        foreach (var patch in _patchOperations)
        {
            if (patch.FileOffset < 0 || patch.FileOffset + patch.PatchedBytes.Length > bytes.Length)
            {
                Status.Message = "A persisted patch lies outside the base image.";
                _logger.Warning("Patch at offset {Offset} is outside the base image", patch.FileOffset);
                return false;
            }

            patch.PatchedBytes.CopyTo(bytes, checked((int)patch.FileOffset));
        }

        ApplyImage(Image.WithBytes(bytes, HashBytes(bytes)));
        HasPatches = _patchOperations.Count > 0;
        Status.Message = message;
        await Task.CompletedTask;
        return true;
    }

    [RelayCommand]
    private async Task UndoPatchAsync()
    {
        if (Image is null || _patchOperations.Count == 0)
        {
            Status.Message = "There is no patch to undo.";
            return;
        }

        var patch = _patchOperations[^1];
        _patchOperations.RemoveAt(_patchOperations.Count - 1);
        _patchRedo.Push(patch);
        await RebuildPatchedImageAsync("Patch undone.");
        if (HasProject)
        {
            MarkProjectDirty();
        }
    }

    [RelayCommand]
    private async Task RedoPatchAsync()
    {
        if (Image is null || _patchRedo.Count == 0)
        {
            Status.Message = "There is no patch to redo.";
            return;
        }

        var patch = _patchRedo.Pop();
        _patchOperations.Add(patch);
        await RebuildPatchedImageAsync("Patch reapplied.");
        if (HasProject)
        {
            MarkProjectDirty();
        }
    }

    [RelayCommand]
    private async Task SavePatchedCopyAsync()
    {
        if (Image is null)
        {
            Status.Message = "Open a binary before saving a patched copy.";
            return;
        }

        if (!HasPatches)
        {
            Status.Message = "There are no patches to save.";
            return;
        }

        var extension = Path.GetExtension(Image.FileName);
        var suggested = Path.GetFileNameWithoutExtension(Image.FileName) + ".patched" + extension;
        var path = _dialogs.PickPatchedBinaryFile(suggested);
        if (!string.IsNullOrWhiteSpace(path))
        {
            await SavePatchedCopyAtAsync(path);
        }
    }

    public async Task<bool> SavePatchedCopyAtAsync(string path)
    {
        if (Image is null || !HasPatches || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            await File.WriteAllBytesAsync(path, Image.Bytes.ToArray());
            Status.Message = $"Patched copy saved: {Path.GetFileName(path)}";
            _logger.Information("Patched copy saved to {Path:l}", path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error(exception, "Could not save patched copy to {Path:l}", path);
            _dialogs.ShowError("Could not save patched copy", exception.Message, exception);
            return false;
        }
    }

    private static string HashBytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (Image is null)
        {
            Status.Message = "Open a binary before running analysis.";
            return;
        }

        await OpenFileAsync(Image.FilePath, preserveProject: true);
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_loadCts is null)
        {
            return;
        }

        Status.Message = "Cancelling…";
        _loadCts.Cancel();
        _logger.Information("Cancellation requested by the user.");
    }

    [RelayCommand]
    private void CloseBinary()
    {
        _loadCts?.Cancel();

        if (Image is null && !IsBusy)
        {
            return;
        }

        Image = null;
        Overview = null;
        if (Disassembly is not null)
        {
            Disassembly.Cancel();
            Disassembly.InstructionSelected -= OnDisassemblyInstructionSelected;
        }

        Disassembly = null;
        if (BinaryAnalysis is not null)
        {
            BinaryAnalysis.Cancel();
            BinaryAnalysis.Completed -= OnAnalysisCompleted;
            BinaryAnalysis.Failed -= OnAnalysisFailed;
        }

        BinaryAnalysis = null;
        AnalysisResult = null;
        if (AnalysisBrowser is not null)
        {
            AnalysisBrowser.ActivateRequested -= OnAnalysisBrowserActivated;
        }

        AnalysisBrowser = null;
        CallGraph = null;
        if (Graph is not null)
        {
            Graph.NavigateToRvaRequested -= OnGraphNavigate;
        }

        Graph = null;
        ClearProjectState();
        HexEditor = null;
        _baseBytes = null;
        _baseSha256 = null;
        _patchOperations.Clear();
        _patchRedo.Clear();
        HasPatches = false;
        Explorer.Clear();
        Properties.Clear();
        Status.ImageInfo = "No binary loaded";
        Status.FindingsInfo = string.Empty;
        Status.Message = "Ready";
        Status.Progress = 0;
        Status.IsBusy = false;
        IsBusy = false;

        _backStack.Clear();
        _forwardStack.Clear();
        _currentNode = null;
        UpdateNavigation();

        BinaryClosed?.Invoke();
        _logger.Information("Binary closed.");
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var result = _dialogs.ShowSettings(_theme.Current, _settings.Current.RememberWindowSize);
        if (result is null)
        {
            return;
        }

        var previousTheme = _theme.Current;
        _settings.Current.RememberWindowSize = result.RememberWindowSize;
        _settings.SetTheme(result.Theme);
        _theme.Apply(result.Theme);

        Status.ThemeName = result.Theme == AppTheme.Dark ? "Dark" : "Light";
        OnPropertyChanged(nameof(ThemeLabel));

        _logger.Information(
            "Settings updated: theme {Previous} -> {Current}, remember window size {Remember}",
            previousTheme,
            result.Theme,
            result.RememberWindowSize);

        Status.Message = "Settings saved.";
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var theme = _theme.Toggle();
        _settings.SetTheme(theme);
        Status.ThemeName = theme == AppTheme.Dark ? "Dark" : "Light";
        OnPropertyChanged(nameof(ThemeLabel));
        _logger.Information("Theme toggled to {Theme}", theme);
    }

    [RelayCommand]
    private void TogglePanel(string? panelId)
    {
        if (!string.IsNullOrWhiteSpace(panelId))
        {
            PanelToggleRequested?.Invoke(panelId);
        }
    }

    [RelayCommand]
    private void GoToAddress()
    {
        if (Image is null)
        {
            Status.Message = "Open a binary before navigating to an address.";
            return;
        }

        var resolution = _dialogs.ShowGoToAddress(Image.AddressMap);
        if (resolution is not { } resolved)
        {
            return;
        }

        Status.Message =
            $"RVA {Hex.Rva(resolved.Rva)} · VA {Hex.Va(resolved.VirtualAddress)} · offset {Hex.Offset(resolved.FileOffset)}";

        if (resolved.Section is { } section)
        {
            var node = Explorer.FindSectionNode(section.Name);
            if (node is not null)
            {
                NavigateTo(node);
            }
        }

        if (Disassembly is { IsSupported: true } disassembly)
        {
            disassembly.NavigateToRva(resolved.Rva);
            OpenDisassemblyRequested?.Invoke();
        }
        else if (HexEditor is not null)
        {
            HexEditor.NavigateToOffset(resolved.FileOffset);
            OpenHexRequested?.Invoke();
        }

        _logger.Information(
            "Navigated to address 0x{Value:X}: RVA {Rva}, offset {Offset}, section {Section}",
            resolved.VirtualAddress,
            Hex.Rva(resolved.Rva),
            Hex.Offset(resolved.FileOffset),
            resolved.Section?.Name ?? "none");
    }

    [RelayCommand]
    private void NavigateToEntryPoint()
    {
        if (Image is null)
        {
            Status.Message = "Open a binary first.";
            return;
        }

        var section = Image.EntryPointSection;
        if (section is null)
        {
            Status.Message = "The entry point is not inside a mapped section.";
            return;
        }

        var node = Explorer.FindSectionNode(section.Name);
        if (node is not null)
        {
            NavigateTo(node);
            Status.Message = $"Entry point {Hex.Rva(Image.EntryPointRva)} is in {section.Name}.";

            if (Disassembly is { IsSupported: true } disassembly)
            {
                disassembly.NavigateToEntryPoint();
                OpenDisassemblyRequested?.Invoke();
            }
            else if (HexEditor is not null && Image.EntryPointFileOffset is { } entryPointOffset)
            {
                HexEditor.NavigateToOffset(entryPointOffset);
                OpenHexRequested?.Invoke();
            }
        }
    }

    [RelayCommand]
    private void NavigateBack()
    {
        if (_backStack.Count == 0)
        {
            return;
        }

        if (_currentNode is not null)
        {
            _forwardStack.Push(_currentNode);
        }

        var node = _backStack.Pop();
        Explorer.SelectNode(node);
        _currentNode = node;
        UpdateNavigation();
    }

    [RelayCommand]
    private void NavigateForward()
    {
        if (_forwardStack.Count == 0)
        {
            return;
        }

        if (_currentNode is not null)
        {
            _backStack.Push(_currentNode);
        }

        var node = _forwardStack.Pop();
        Explorer.SelectNode(node);
        _currentNode = node;
        UpdateNavigation();
    }

    [RelayCommand]
    private void OpenCommandPalette()
    {
        PaletteQuery = string.Empty;
        RefreshPalette();
        IsPaletteOpen = true;
    }

    [RelayCommand]
    private void OpenHexEditor()
    {
        if (Image is null)
        {
            Status.Message = "Open a binary before opening the hex editor.";
            return;
        }

        OpenHexRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenDisassembly()
    {
        if (Image is null)
        {
            Status.Message = "Open a binary before opening the disassembly.";
            return;
        }

        OpenDisassemblyRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenControlFlowGraph()
    {
        if (Image is null || AnalysisResult is null)
        {
            Status.Message = "Function analysis is not ready yet.";
            return;
        }

        var function = Explorer.SelectedNode?.Payload as FunctionAnalysis
                       ?? AnalysisResult.FindFunction(Image.EntryPointRva)
                       ?? AnalysisResult.Functions.FirstOrDefault();

        if (function is null)
        {
            Status.Message = "No function is available for a control-flow graph.";
            return;
        }

        OpenGraph(function);
    }

    [RelayCommand]
    private void CloseCommandPalette() => IsPaletteOpen = false;

    [RelayCommand]
    private void ExecutePaletteCommand(PaletteCommand? command)
    {
        if (command is null)
        {
            return;
        }

        IsPaletteOpen = false;
        if (command.Command.CanExecute(command.Parameter))
        {
            command.Command.Execute(command.Parameter);
        }
    }

    [RelayCommand]
    private void RefreshExplorer() => Explorer.Refresh();

    [RelayCommand]
    private void OpenAbout() => _dialogs.ShowAbout();

    [RelayCommand]
    private void ShowShortcuts()
    {
        var text = string.Join(
            Environment.NewLine,
            Shortcuts.Select(shortcut => $"{shortcut.Gesture,-14} {shortcut.Description}"));
        _dialogs.ShowInformation("Keyboard shortcuts", text);
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            ShellHelper.OpenFolder(ShellPaths.LogDirectory);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not open the log folder.");
            _dialogs.ShowError("Could not open folder", ex.Message, ex);
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            ShellHelper.OpenFolder(ShellPaths.DataDirectory);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not open the data folder.");
            _dialogs.ShowError("Could not open folder", ex.Message, ex);
        }
    }

    [RelayCommand]
    private void OpenContainingFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            ShellHelper.ShowInExplorer(path);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not open the containing folder for {Path}", path);
            _dialogs.ShowError("Could not open folder", ex.Message, ex);
        }
    }

    [RelayCommand]
    private void RemoveRecent(RecentFileEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        _recent.Remove(entry.Path);
        Status.Message = "Removed from recent binaries.";
    }

    [RelayCommand]
    private void CopyFilePath()
    {
        if (Image is not null)
        {
            CopyToClipboard(Image.FilePath, "File path");
        }
    }

    [RelayCommand]
    private void CopySha256()
    {
        if (Image is not null)
        {
            CopyToClipboard(Image.Sha256, "SHA-256");
        }
    }

    [RelayCommand]
    private void CopyProperties() => CopyToClipboard(Properties.GetText(), "Properties");

    [RelayCommand]
    private void CloseActiveDocument() => CloseActiveDocumentRequested?.Invoke();

    [RelayCommand]
    private void Exit()
    {
        _logger.Information("Application exit requested.");
        Application.Current.Shutdown();
    }

    public void SelectFinding(PeFinding? finding)
    {
        if (finding is null)
        {
            return;
        }

        Properties.Show(PropertySheetFactory.ForFinding(finding), finding.Code);
    }

    partial void OnImageChanged(BinaryImage? value)
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnProjectPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnProjectNameChanged(string? value)
    {
        OnPropertyChanged(nameof(ProjectDisplayName));
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnIsProjectDirtyChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));

    partial void OnHasPatchesChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));

    partial void OnPaletteQueryChanged(string value) => RefreshPalette();

    private void ApplyImage(BinaryImage image)
    {
        Image = image;
        Overview = new OverviewViewModel(image);

        if (Disassembly is not null)
        {
            Disassembly.Cancel();
            Disassembly.InstructionSelected -= OnDisassemblyInstructionSelected;
        }

        Disassembly = new DisassemblyViewModel(
            image,
            _disassembler,
            _logger.ForContext<DisassemblyViewModel>());
        Disassembly.InstructionSelected += OnDisassemblyInstructionSelected;
        _ = Disassembly.LoadAsync();

        if (BinaryAnalysis is not null)
        {
            BinaryAnalysis.Cancel();
            BinaryAnalysis.Completed -= OnAnalysisCompleted;
            BinaryAnalysis.Failed -= OnAnalysisFailed;
        }

        AnalysisResult = null;
        BinaryAnalysis = new AnalysisViewModel(
            image,
            Disassembly,
            _analysisEngine,
            _logger.ForContext<AnalysisViewModel>());
        BinaryAnalysis.Completed += OnAnalysisCompleted;
        BinaryAnalysis.Failed += OnAnalysisFailed;
        HexEditor = new HexEditorViewModel(image);
        Explorer.LoadImage(image);
        Explorer.ApplyAnnotations(_annotations, _bookmarks);

        if (BinaryAnalysis is not null)
        {
            _ = BinaryAnalysis.LoadAsync();
        }

        Properties.Show(PropertySheetFactory.ForImage(image), image.FileName);

        Status.ImageInfo =
            $"{image.FormatText} · {image.ArchitectureText} · {Hex.Va(image.ImageBase)} · {Hex.Size(image.FileSize)}";
        Status.FindingsInfo = image.Findings.Count == 0
            ? "No structural observations"
            : $"{image.Findings.Count} observation{(image.Findings.Count == 1 ? string.Empty : "s")} · " +
              $"{image.WarningCount} warning{(image.WarningCount == 1 ? string.Empty : "s")}";

        _backStack.Clear();
        _forwardStack.Clear();
        _currentNode = null;
        UpdateNavigation();

        BinaryLoaded?.Invoke();
    }

    private void OnExplorerSelectionChanged(object? sender, ExplorerNode? node)
    {
        _currentNode = node;

        if (node is null)
        {
            if (Image is not null)
            {
                Properties.Show(PropertySheetFactory.ForImage(Image), Image.FileName);
            }
            else
            {
                Properties.Clear();
            }

            return;
        }

        ShowProperties(node);
    }

    private void OnDisassemblyInstructionSelected(DisassembledInstruction? instruction)
    {
        if (Image is null || instruction is null)
        {
            return;
        }

        Properties.Show(
            PropertySheetFactory.ForInstruction(instruction, Image),
            $"RVA {Hex.Rva(instruction.Rva)}");
    }

    private void OnAnalysisCompleted(AnalysisResult result)
    {
        AnalysisResult = result;
        Explorer.LoadAnalysis(result);

        if (AnalysisBrowser is not null)
        {
            AnalysisBrowser.ActivateRequested -= OnAnalysisBrowserActivated;
        }

        AnalysisBrowser = new AnalysisBrowserViewModel(result);
        AnalysisBrowser.ActivateRequested += OnAnalysisBrowserActivated;
        CallGraph = Image is null ? null : new CallGraphViewModel(Image, result, _annotations);

        if (Graph is not null)
        {
            Graph.NavigateToRvaRequested -= OnGraphNavigate;
        }

        Graph = Image is null ? null : new FunctionGraphViewModel(Image, result, _annotations);
        if (Graph is not null)
        {
            Graph.NavigateToRvaRequested += OnGraphNavigate;
        }

        Status.Message = result.Message ?? $"Analysis: {result.FunctionCount:N0} functions, {result.CrossReferenceCount:N0} references, {result.StringCount:N0} strings.";
    }

    private void OnAnalysisFailed(string message)
    {
        Explorer.SetAnalysisUnavailable(message);
        Status.Message = message;
    }

    private void OnGraphNavigate(uint rva)
    {
        Disassembly?.NavigateToRva(rva);
        OpenDisassemblyRequested?.Invoke();
    }

    private void OnExplorerNodeActivated(ExplorerNode node) => ActivateAnalysisPayload(node.Payload);

    private void OnAnalysisBrowserActivated(object payload) => ActivateAnalysisPayload(payload);

    private void ActivateAnalysisPayload(object? payload)
    {
        switch (payload)
        {
            case FunctionAnalysis function:
                OpenGraph(function);
                break;

            case StringLiteral value when HexEditor is not null:
                HexEditor.NavigateToOffset(value.FileOffset);
                OpenHexRequested?.Invoke();
                break;

            case CrossReference reference when Disassembly is not null:
                Disassembly.NavigateToRva(reference.FromRva);
                OpenDisassemblyRequested?.Invoke();
                break;

            case ProjectBookmark bookmark when Disassembly is not null:
                Disassembly.NavigateToRva(bookmark.Rva);
                OpenDisassemblyRequested?.Invoke();
                break;
        }
    }

    private void OpenGraph(FunctionAnalysis function)
    {
        if (Image is null || AnalysisResult is null)
        {
            Status.Message = "Function analysis is not ready yet.";
            return;
        }

        Graph?.SelectFunction(function);
        OpenGraphRequested?.Invoke(function);
    }

    private void ShowProperties(ExplorerNode node)
    {
        IReadOnlyList<PropertyGroupViewModel> groups = node.Payload switch
        {
            BinaryImage image => PropertySheetFactory.ForImage(image),
            PeSection section when Image is not null => PropertySheetFactory.ForSection(section, Image),
            ImportedModule module => PropertySheetFactory.ForImportModule(module),
            ImportedSymbol symbol => PropertySheetFactory.ForImportedSymbol(symbol),
            ExportedSymbol export => PropertySheetFactory.ForExport(export),
            PeResourceEntry resource when Image is not null => PropertySheetFactory.ForResource(resource, Image),
            PeRelocationInfo relocations => PropertySheetFactory.ForRelocations(relocations),
            PeTlsInfo tls when Image is not null => PropertySheetFactory.ForTls(tls, Image),
            PeTlsCallback callback when Image is not null => PropertySheetFactory.ForTlsCallback(callback, Image),
            PeExceptionInfo exceptions => PropertySheetFactory.ForExceptions(exceptions),
            PeDebugEntry debugEntry => PropertySheetFactory.ForDebugEntry(debugEntry),
            PeLoadConfigurationInfo loadConfiguration => PropertySheetFactory.ForLoadConfiguration(loadConfiguration),
            DisassembledInstruction instruction when Image is not null => PropertySheetFactory.ForInstruction(instruction, Image),
            FunctionAnalysis function when AnalysisResult is not null && Image is not null => PropertySheetFactory.ForFunction(function, AnalysisResult, Image, _annotations.GetValueOrDefault(function.Function.Rva)),
            StringLiteral value when Image is not null => PropertySheetFactory.ForString(value, Image, _annotations.GetValueOrDefault(value.Rva)),
            CrossReference reference when Image is not null => PropertySheetFactory.ForCrossReference(reference, Image, _annotations.GetValueOrDefault(reference.FromRva)),
            ProjectBookmark bookmark => PropertySheetFactory.ForBookmark(bookmark),
            AnalysisResult analysis => PropertySheetFactory.ForAnalysisSummary(analysis),
            PeFinding finding => PropertySheetFactory.ForFinding(finding),
            DataDirectoryInfo directory => PropertySheetFactory.ForDataDirectory(directory),
            PropertyBag bag => PropertySheetFactory.ForPropertyBag(bag),
            PropertyRowViewModel row => PropertySheetFactory.ForPropertyRow(row),
            NotAvailable unavailable => PropertySheetFactory.ForNotAvailable(unavailable),
            _ when Image is not null => PropertySheetFactory.ForImage(Image),
            _ => [],
        };

        Properties.Show(groups, node.Title);
    }

    private void NavigateTo(ExplorerNode node)
    {
        if (_currentNode is not null && !ReferenceEquals(_currentNode, node))
        {
            _backStack.Push(_currentNode);
            _forwardStack.Clear();
        }

        Explorer.SelectNode(node);
        _currentNode = node;
        UpdateNavigation();
    }

    private void UpdateNavigation()
    {
        CanGoBack = _backStack.Count > 0;
        CanGoForward = _forwardStack.Count > 0;
    }

    private void RefreshRecentMenu()
    {
        RecentMenuItems.Clear();

        if (_recent.Items.Count == 0)
        {
            RecentMenuItems.Add(new RecentMenuEntry
            {
                Header = "(no recent binaries)",
            });
            return;
        }

        foreach (var entry in _recent.Items)
        {
            RecentMenuItems.Add(new RecentMenuEntry
            {
                Header = entry.FileName,
                Path = entry.Path,
                Command = OpenRecentCommand,
            });
        }
    }

    private void BuildPaletteCommands()
    {
        PaletteCommands.Clear();

        PaletteCommands.Add(new PaletteCommand("Open binary…", "File", "Ctrl+O", OpenBinaryCommand));
        PaletteCommands.Add(new PaletteCommand("New project…", "File", "Ctrl+Shift+N", NewProjectCommand));
        PaletteCommands.Add(new PaletteCommand("Open project…", "File", "Ctrl+Shift+O", OpenProjectCommand));
        PaletteCommands.Add(new PaletteCommand("Save project", "File", "Ctrl+S", SaveProjectCommand));
        PaletteCommands.Add(new PaletteCommand("Save project as…", "File", "Ctrl+Shift+S", SaveProjectAsCommand));
        PaletteCommands.Add(new PaletteCommand("Save patched copy…", "File", "Ctrl+Shift+E", SavePatchedCopyCommand));
        PaletteCommands.Add(new PaletteCommand("Reload and re-analyze", "File", "Ctrl+R", ReloadCommand));
        PaletteCommands.Add(new PaletteCommand("Close binary", "File", "Ctrl+W", CloseBinaryCommand));
        PaletteCommands.Add(new PaletteCommand("Settings", "Tools", "Ctrl+,", OpenSettingsCommand));
        PaletteCommands.Add(new PaletteCommand("Go to address", "Navigation", "Ctrl+G", GoToAddressCommand));
        PaletteCommands.Add(new PaletteCommand("Navigate to entry point", "Navigation", "—", NavigateToEntryPointCommand));
        PaletteCommands.Add(new PaletteCommand("Navigate back", "Navigation", "Alt+Left", NavigateBackCommand));
        PaletteCommands.Add(new PaletteCommand("Navigate forward", "Navigation", "Alt+Right", NavigateForwardCommand));
        PaletteCommands.Add(new PaletteCommand("Toggle theme", "View", "—", ToggleThemeCommand));
        PaletteCommands.Add(new PaletteCommand("Open disassembly", "View", "Ctrl+D", OpenDisassemblyCommand));
        PaletteCommands.Add(new PaletteCommand("Open analysis browser", "View", "Ctrl+Shift+A", OpenAnalysisBrowserCommand));
        PaletteCommands.Add(new PaletteCommand("Open call graph", "View", "Ctrl+Shift+C", OpenCallGraphCommand));
        PaletteCommands.Add(new PaletteCommand("Open control-flow graph", "View", "Ctrl+Shift+G", OpenControlFlowGraphCommand));
        PaletteCommands.Add(new PaletteCommand("Open hex editor", "View", "Ctrl+H", OpenHexEditorCommand));
        PaletteCommands.Add(new PaletteCommand("Patch selected instruction…", "Edit", "Ctrl+E", PatchInstructionCommand));
        PaletteCommands.Add(new PaletteCommand("Undo patch", "Edit", "Ctrl+Shift+Z", UndoPatchCommand));
        PaletteCommands.Add(new PaletteCommand("Redo patch", "Edit", "Ctrl+Shift+Y", RedoPatchCommand));
        PaletteCommands.Add(new PaletteCommand("Toggle Solution Explorer", "View", "—", TogglePanelCommand, "solutionExplorer"));
        PaletteCommands.Add(new PaletteCommand("Toggle Properties", "View", "—", TogglePanelCommand, "properties"));
        PaletteCommands.Add(new PaletteCommand("Toggle Output", "View", "—", TogglePanelCommand, "output"));
        PaletteCommands.Add(new PaletteCommand("Toggle Analysis", "View", "—", TogglePanelCommand, "analysis"));
        PaletteCommands.Add(new PaletteCommand("Open log folder", "Tools", "—", OpenLogFolderCommand));
        PaletteCommands.Add(new PaletteCommand("Open data folder", "Tools", "—", OpenDataFolderCommand));
        PaletteCommands.Add(new PaletteCommand("About Obsidian Disassembler", "Help", "—", OpenAboutCommand));
        PaletteCommands.Add(new PaletteCommand("Keyboard shortcuts", "Help", "—", ShowShortcutsCommand));
        PaletteCommands.Add(new PaletteCommand("Exit", "File", "—", ExitCommand));
    }

    private void RefreshPalette()
    {
        var query = PaletteQuery.Trim();
        var matches = PaletteCommands
            .Where(command => query.Length == 0
                              || command.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        FilteredPaletteCommands.Clear();
        foreach (var match in matches)
        {
            FilteredPaletteCommands.Add(match);
        }

        SelectedPaletteCommand = FilteredPaletteCommands.FirstOrDefault();
    }

    private void CopyToClipboard(string text, string description)
    {
        try
        {
            Clipboard.SetText(text);
            Status.Message = $"{description} copied to the clipboard.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not copy {Description} to the clipboard.", description);
            _dialogs.ShowError("Clipboard unavailable", "Windows did not allow access to the clipboard.", ex);
        }
    }
}

