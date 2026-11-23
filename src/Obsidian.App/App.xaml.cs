using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.Layout;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Obsidian.App.Services;
using Obsidian.App.ViewModels;
using Obsidian.App.Views;
using Obsidian.Analysis;
using Obsidian.Core.Model;
using Obsidian.Core.Services;
using Obsidian.Disassembly;
using Obsidian.Pe;
using Obsidian.Projects;
using Serilog;

namespace Obsidian.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private bool _isCaptureMode;

    /// <summary>
    /// True while the screenshot/dialog diagnostic modes run. Modal dialogs are
    /// suppressed so an automated capture can never block on user input.
    /// </summary>
    public static bool IsDiagnosticsMode { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShellPaths.EnsureCreated();

        var sink = new UiLogSink();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.Sink(sink)
            .WriteTo.File(
                Path.Combine(ShellPaths.LogDirectory, "obsidian-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled AppDomain exception");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(Log.Logger);
            services.AddSingleton(sink);
            services.AddSingleton<SettingsService>();
            services.AddSingleton<RecentFilesService>();
            services.AddSingleton<ThemeService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IBinaryImageLoader, PeImageLoader>();
            services.AddSingleton<IDisassembler, IcedDisassembler>();
            services.AddSingleton<IBinaryAnalysisEngine, BinaryAnalysisEngine>();
            services.AddSingleton<IProjectStore, SqliteProjectStore>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            _services = services.BuildServiceProvider();

            var settings = _services.GetRequiredService<SettingsService>();
            var theme = _services.GetRequiredService<ThemeService>();
            theme.Apply(settings.CurrentTheme, force: true);

            var args = e.Args.ToList();
            var dialogName = GetOption(args, "--dialog");

            if (args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase)))
            {
                IsDiagnosticsMode = true;
                await RunSmokeAsync();
                return;
            }

            if (!string.IsNullOrWhiteSpace(dialogName))
            {
                _isCaptureMode = true;
                IsDiagnosticsMode = true;
                var outputPath = GetOption(args, "--capture") ?? "artifacts/dialog.png";
                await RunDialogCaptureAsync(dialogName, outputPath, GetOption(args, "--open"));
                return;
            }

            var captureIndex = args.FindIndex(a => string.Equals(a, "--capture", StringComparison.OrdinalIgnoreCase));

            if (captureIndex >= 0)
            {
                _isCaptureMode = true;
                IsDiagnosticsMode = true;
                var outputPath = captureIndex + 1 < args.Count ? args[captureIndex + 1] : null;
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    Log.Error("--capture requires an output PNG path.");
                    Shutdown(2);
                    return;
                }

                var openPath = GetOption(args, "--open");
                var themeName = GetOption(args, "--theme");
                var captureTheme = string.Equals(themeName, "light", StringComparison.OrdinalIgnoreCase)
                    ? AppTheme.Light
                    : AppTheme.Dark;
                var width = ParseInt(GetOption(args, "--width"), 1600);
                var height = ParseInt(GetOption(args, "--height"), 1000);
                var showPalette = args.Any(a => string.Equals(a, "--palette", StringComparison.OrdinalIgnoreCase));
                var panelId = GetOption(args, "--panel");
                var documentId = GetOption(args, "--document");
                var treeFilter = GetOption(args, "--tree-filter");
                var selectTree = GetOption(args, "--select-tree");
                var projectPath = GetOption(args, "--project");

                await RunCaptureAsync(
                    outputPath,
                    openPath,
                    captureTheme,
                    width,
                    height,
                    showPalette,
                    panelId,
                    documentId,
                    treeFilter,
                    selectTree,
                    projectPath);
                return;
            }

            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();

            var initialFile = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && File.Exists(a));
            if (initialFile is not null)
            {
                _ = OpenInitialFileAsync(window.DataContext as MainViewModel, initialFile);
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");

            if (_isCaptureMode)
            {
                Shutdown(1);
                return;
            }

            MessageBox.Show(
                "Obsidian Disassembler could not start.\n\n" + ex.Message,
                "Obsidian Disassembler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application exiting with code {Code}", e.ApplicationExitCode);
        Log.CloseAndFlush();
        _services?.Dispose();
        base.OnExit(e);
    }

    private async Task OpenInitialFileAsync(MainViewModel? viewModel, string path)
    {
        if (viewModel is null)
        {
            return;
        }

        await viewModel.OpenFileAsync(path);
    }

    private async Task RunSmokeAsync()
    {
        var services = _services ?? throw new InvalidOperationException("Services are not initialized.");
        var window = services.GetRequiredService<Obsidian.App.Views.MainWindow>();
        var viewModel = services.GetRequiredService<MainViewModel>();
        var themeService = services.GetRequiredService<ThemeService>();

        window.IsCaptureMode = true;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

        var failures = 0;

        void Check(string name, bool condition)
        {
            if (condition)
            {
                Log.Information("SMOKE OK: {Check:l}", name);
            }
            else
            {
                failures++;
                Log.Error("SMOKE FAILED: {Check:l}", name);
            }
        }

        // Menu template/popup
        var fileMenu = window.FindName("FileMenu") as MenuItem;
        if (fileMenu is null)
        {
            Check("file menu exists", false);
        }
        else
        {
            fileMenu.ApplyTemplate();
            fileMenu.IsSubmenuOpen = true;
            await Task.Delay(150);
            var popup = fileMenu.Template.FindName("PART_Popup", fileMenu) as Popup;
            Check("file menu opens", fileMenu.IsSubmenuOpen && popup?.IsOpen == true);
            fileMenu.IsSubmenuOpen = false;
        }

        // Runtime theme switch
        var originalTheme = themeService.Current;
        viewModel.ToggleThemeCommand.Execute(null);
        await Task.Delay(80);
        Check("theme toggles", themeService.Current != originalTheme);
        viewModel.ToggleThemeCommand.Execute(null);
        await Task.Delay(80);
        Check("theme restores", themeService.Current == originalTheme);

        // Tool window hide/show via the same command the UI uses
        var dockManager = window.FindName("DockManager") as DockingManager;
        var outputPanel = dockManager?.Layout
            .Descendents()
            .OfType<LayoutAnchorable>()
            .FirstOrDefault(panel => panel.ContentId == "output");

        if (outputPanel is null)
        {
            Check("output tool window exists", false);
        }
        else
        {
            Check("output starts visible", outputPanel.IsVisible);
            viewModel.TogglePanelCommand.Execute("output");
            await Task.Delay(120);
            Check("output hides", !outputPanel.IsVisible);
            viewModel.TogglePanelCommand.Execute("output");
            await Task.Delay(120);
            Check("output shows", outputPanel.IsVisible);

            outputPanel.Close();
            await Task.Delay(120);
            Check("output close hides", !outputPanel.IsVisible);
            outputPanel.Show();
            await Task.Delay(120);
            Check("output reopens after close", outputPanel.IsVisible);
        }

        // Command palette
        viewModel.OpenCommandPaletteCommand.Execute(null);
        await Task.Delay(60);
        Check("command palette opens", viewModel.IsPaletteOpen && viewModel.FilteredPaletteCommands.Count > 0);
        viewModel.PaletteQuery = "theme";
        await Task.Delay(60);
        Check("command palette filters", viewModel.FilteredPaletteCommands.Count > 0
                                            && viewModel.FilteredPaletteCommands.All(c =>
                                                c.SearchText.Contains("theme", StringComparison.OrdinalIgnoreCase)));
        viewModel.CloseCommandPaletteCommand.Execute(null);
        Check("command palette closes", !viewModel.IsPaletteOpen);

        // Document lifecycle
        var binaryPath = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        if (File.Exists(binaryPath))
        {
            await viewModel.OpenFileAsync(binaryPath);
            Check("binary loads", viewModel.Image is not null);
            await Task.Delay(80);
            var overviewDocument = dockManager?.Layout
                .Descendents()
                .OfType<LayoutDocument>()
                .FirstOrDefault(document => document.ContentId == "overview");
            Check("overview document opens", overviewDocument is not null);
            Check("resources parsed", viewModel.Image?.Resources.Count > 0);
            Check("relocations parsed", viewModel.Image?.Relocations.Count > 0);
            Check("disassembly document opens", dockManager?.Layout
                .Descendents()
                .OfType<LayoutDocument>()
                .Any(document => document.ContentId == "disassembly") == true);

            if (viewModel.Disassembly is { } disassembly)
            {
                await disassembly.WaitForCompletionAsync();
                Check("disassembly decodes instructions", disassembly.Rows.Count > 0);
                disassembly.NavigateToEntryPoint();
                await Task.Delay(80);
                Check(
                    "disassembly entry point selected",
                    disassembly.SelectedInstruction?.Instruction.Rva == viewModel.Image?.EntryPointRva);

                viewModel.OpenDisassemblyCommand.Execute(null);
                await Task.Delay(80);
                Check("disassembly command activates document", dockManager?.Layout
                    .Descendents()
                    .OfType<LayoutDocument>()
                    .Any(document => document.ContentId == "disassembly" && document.IsActive) == true);

                var mappedBranch = disassembly.Result?.Instructions.FirstOrDefault(instruction =>
                    instruction.TargetRva is { } target
                    && disassembly.Result.FindInstructionIndex(target) >= 0);

                if (mappedBranch?.TargetRva is { } branchTarget)
                {
                    disassembly.NavigateToRva(mappedBranch.Rva);
                    disassembly.FollowTargetCommand.Execute(null);
                    await Task.Delay(80);
                    Check(
                        "disassembly follows branch",
                        disassembly.SelectedInstruction?.Instruction.Rva == branchTarget);
                }
            }

            if (viewModel.BinaryAnalysis is { } binaryAnalysis)
            {
                await binaryAnalysis.WaitForCompletionAsync();
                Check("analysis discovers functions", binaryAnalysis.Result?.FunctionCount > 0);
                Check("analysis builds CFGs", binaryAnalysis.Result?.Functions.Any(function => function.Blocks.Count > 0) == true);
                var analysisTreeNodes = viewModel.Explorer.Roots
                    .SelectMany(root => root.Descendants().Prepend(root))
                    .ToArray();
                Check("functions tree populated", analysisTreeNodes.Any(node => node.Title == "Functions" && node.Children.Count > 0));
                Check("strings tree populated", analysisTreeNodes.Any(node => node.Title == "Strings" && node.Children.Count > 0));
                Check("cross references tree populated", analysisTreeNodes.Any(node => node.Title == "Cross References" && node.Children.Count > 0));

                viewModel.OpenAnalysisBrowserCommand.Execute(null);
                await Task.Delay(150);
                Check("analysis browser opens", dockManager?.Layout
                    .Descendents()
                    .OfType<LayoutDocument>()
                    .Any(document => document.ContentId == "analysis-browser" && document.IsActive) == true);
                Check("analysis browser has functions", viewModel.AnalysisBrowser?.Functions.Cast<object>().Any() == true);
                if (viewModel.AnalysisBrowser is { } analysisBrowser)
                {
                    analysisBrowser.SearchText = "sub_140001094";
                    await Task.Delay(100);
                    Check(
                        "analysis browser filters",
                        analysisBrowser.Functions.Cast<object>().Count() == 1);
                    analysisBrowser.ClearSearchCommand.Execute(null);
                }

                viewModel.Explorer.SelectFirstMatching("sub_140001094");
                await Task.Delay(100);
                viewModel.OpenCallGraphCommand.Execute(null);
                await Task.Delay(200);
                Check("call graph opens", dockManager?.Layout
                    .Descendents()
                    .OfType<LayoutDocument>()
                    .Any(document => document.ContentId == "call-graph" && document.IsActive) == true);
                Check(
                    "call graph has neighborhood",
                    viewModel.CallGraph?.Nodes.Count > 1 && viewModel.CallGraph.HasNeighborhood);

                var stringNode = analysisTreeNodes.FirstOrDefault(node => node.Payload is StringLiteral);
                if (stringNode?.Payload is StringLiteral stringLiteral)
                {
                    viewModel.Explorer.ActivateNode(stringNode);
                    await Task.Delay(120);
                    Check("string double-click opens hex", viewModel.HexEditor?.SelectedOffset == stringLiteral.FileOffset);
                }

                var xrefNode = analysisTreeNodes.FirstOrDefault(node => node.Payload is CrossReference);
                if (xrefNode?.Payload is CrossReference crossReference)
                {
                    viewModel.Explorer.ActivateNode(xrefNode);
                    await Task.Delay(120);
                    Check(
                        "xref double-click opens disassembly",
                        viewModel.Disassembly?.SelectedInstruction?.Instruction.Rva == crossReference.FromRva);
                }

                var functionNode = analysisTreeNodes.FirstOrDefault(node => node.Payload is FunctionAnalysis);
                if (functionNode?.Payload is FunctionAnalysis selectedFunction)
                {
                    viewModel.Explorer.ActivateNode(functionNode);
                    await Task.Delay(150);
                    Check(
                        "function double-click opens CFG",
                        dockManager?.Layout
                            .Descendents()
                            .OfType<LayoutDocument>()
                            .Any(document => document.ContentId == "cfg" && document.IsActive) == true
                        && ReferenceEquals(viewModel.Graph?.SelectedFunction, selectedFunction));
                }

                viewModel.OpenControlFlowGraphCommand.Execute(null);
                await Task.Delay(250);
                Check("CFG document opens", dockManager?.Layout
                    .Descendents()
                    .OfType<LayoutDocument>()
                    .Any(document => document.ContentId == "cfg" && document.IsActive) == true);
                Check("CFG has blocks", viewModel.Graph?.Nodes.Count > 0);

                var graphNode = viewModel.Graph?.Nodes.FirstOrDefault();
                if (graphNode is not null)
                {
                    viewModel.Graph!.NavigateToBlock(graphNode);
                    await Task.Delay(120);
                    Check(
                        "CFG block opens disassembly",
                        viewModel.Disassembly?.SelectedInstruction?.Instruction.Rva == graphNode.Block.StartRva);
                }
            }

            var projectPath = Path.Combine(
                Path.GetTempPath(),
                "obsidian-project-smoke-" + Guid.NewGuid().ToString("N") + ".obsproj");

            try
            {
                Check("project creates", await viewModel.CreateProjectAtAsync(projectPath));
                Check("project has path and name",
                    viewModel.HasProject && !string.IsNullOrWhiteSpace(viewModel.ProjectName) && File.Exists(projectPath));
                Check("project saves", await viewModel.SaveProjectAtAsync(projectPath));
            }
            finally
            {
                if (File.Exists(projectPath))
                {
                    try
                    {
                        File.Delete(projectPath);
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            Check("hex document opens", dockManager?.Layout
                .Descendents()
                .OfType<LayoutDocument>()
                .Any(document => document.ContentId == "hex") == true);
            Check("hex rows available", viewModel.HexEditor?.Rows.Count > 0);

            viewModel.OpenHexEditorCommand.Execute(null);
            await Task.Delay(80);
            Check("hex editor command activates document", dockManager?.Layout
                .Descendents()
                .OfType<LayoutDocument>()
                .Any(document => document.ContentId == "hex" && document.IsActive) == true);

            if (viewModel.HexEditor is { } hexEditor)
            {
                hexEditor.NavigateToOffset(0);
                hexEditor.SearchText = "4D 5A";
                hexEditor.SearchMode = HexSearchMode.Hex;
                await hexEditor.FindNextCommand.ExecuteAsync(null);
                Check(
                    "hex search finds MZ",
                    hexEditor.StatusText.Contains("Match", StringComparison.OrdinalIgnoreCase));
            }

            if (viewModel.Disassembly?.SelectedInstruction?.Instruction is { } patchTarget)
            {
                var replacement = Enumerable.Repeat((byte)0x90, patchTarget.Bytes.Length).ToArray();
                var patchApplied = await viewModel.ApplyInstructionPatchAsync(patchTarget, replacement);
                if (viewModel.Disassembly is not null)
                {
                    await viewModel.Disassembly.WaitForCompletionAsync();
                }

                if (viewModel.BinaryAnalysis is not null)
                {
                    await viewModel.BinaryAnalysis.WaitForCompletionAsync();
                }

                Check("instruction patch applies", patchApplied && viewModel.HasPatches);

                var patchedCopyPath = Path.Combine(
                    Path.GetTempPath(),
                    "obsidian-patched-smoke-" + Guid.NewGuid().ToString("N") + ".exe");
                var patchedCopySaved = await viewModel.SavePatchedCopyAtAsync(patchedCopyPath);
                var patchedCopyBytes = File.Exists(patchedCopyPath) ? new FileInfo(patchedCopyPath).Length : 0;
                Check(
                    "patched copy saves",
                    patchedCopySaved && patchedCopyBytes == viewModel.Image?.FileSize);
                try
                {
                    File.Delete(patchedCopyPath);
                }
                catch (IOException)
                {
                }

                Check(
                    "project stores patches",
                    await viewModel.SaveProjectAtAsync(projectPath) && viewModel.HasPatches);

                await viewModel.UndoPatchCommand.ExecuteAsync(null);
                if (viewModel.Disassembly is not null)
                {
                    await viewModel.Disassembly.WaitForCompletionAsync();
                }

                if (viewModel.BinaryAnalysis is not null)
                {
                    await viewModel.BinaryAnalysis.WaitForCompletionAsync();
                }

                Check("patch undo restores the base image", !viewModel.HasPatches);

                var reopened = await viewModel.OpenProjectFromPathAsync(projectPath);
                Check(
                    "project reload restores patches",
                    reopened is not null && viewModel.HasPatches);
            }

            viewModel.CloseBinaryCommand.Execute(null);
            await Task.Delay(80);
            Check("binary closes", viewModel.Image is null);
            Check("overview document closes", dockManager?.Layout
                .Descendents()
                .OfType<LayoutDocument>()
                .All(document => document.ContentId != "overview") == true);
            Check("disassembly document closes", dockManager?.Layout
                .Descendents()
                .OfType<LayoutDocument>()
                .All(document => document.ContentId != "disassembly") == true);
            Check("CFG document closes", dockManager?.Layout
                .Descendents()
                .OfType<LayoutDocument>()
                .All(document => document.ContentId != "cfg") == true);
            Check("call graph closes", dockManager?.Layout
                .Descendents()
                .OfType<LayoutDocument>()
                .All(document => document.ContentId != "call-graph") == true);
            Check("analysis browser closes", dockManager?.Layout
                .Descendents()
                .OfType<LayoutDocument>()
                .All(document => document.ContentId != "analysis-browser") == true);
            Check("hex document closes", dockManager?.Layout
                .Descendents()
                .OfType<LayoutDocument>()
                .All(document => document.ContentId != "hex") == true);
        }

        window.Close();
        Log.Information("SMOKE RESULT: {Failures} failure(s)", failures);
        Shutdown(failures == 0 ? 0 : 1);
    }

    private async Task RunDialogCaptureAsync(string dialogName, string outputPath, string? openPath)
    {
        var services = _services ?? throw new InvalidOperationException("Services are not initialized.");
        Window window = dialogName.ToLowerInvariant() switch
        {
            "settings" => new SettingsWindow
            {
                DataContext = new SettingsViewModel(
                    services.GetRequiredService<ThemeService>().Current,
                    services.GetRequiredService<SettingsService>().Current.RememberWindowSize,
                    typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                    ShellPaths.DataDirectory),
            },
            "goto" => await CreateGoToDialogAsync(services, openPath),
            "patch" => await CreatePatchDialogAsync(services, openPath),
            "prompt" => new TextPromptWindow(
                "Rename function",
                "Set a project label for sub_140001094",
                "NotepadEntryHelper",
                multiline: false),
            "savechanges" => new SaveChangesWindow(
                "Unsaved project changes",
                "Project 'Notepad reverse notes' has unsaved annotations. Save before closing?"),
            "about" => new AboutWindow(),
            _ => new MessageWindow(
                "Could not open binary",
                "The file could not be parsed as a Portable Executable image.",
                "System.BadImageFormatException: The specified file is not a valid PE image."),
        };

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Show();
        await Task.Delay(300);
        window.UpdateLayout();

        await CaptureVisualAsync((FrameworkElement)window.Content, outputPath);
        window.Close();
        Shutdown(0);
    }

    private static async Task<GoToAddressWindow> CreateGoToDialogAsync(IServiceProvider services, string? openPath)
    {
        var loader = services.GetRequiredService<IBinaryImageLoader>();
        var path = !string.IsNullOrWhiteSpace(openPath) && File.Exists(openPath)
            ? openPath
            : Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var image = await loader.LoadAsync(path);
        return new GoToAddressWindow(image.AddressMap);
    }

    private static async Task<PatchInstructionWindow> CreatePatchDialogAsync(IServiceProvider services, string? openPath)
    {
        var loader = services.GetRequiredService<IBinaryImageLoader>();
        var disassembler = services.GetRequiredService<IDisassembler>();
        var path = !string.IsNullOrWhiteSpace(openPath) && File.Exists(openPath)
            ? openPath
            : Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var image = await loader.LoadAsync(path);
        var result = await disassembler.DisassembleAsync(image);
        var instruction = result.Instructions.FirstOrDefault(instruction => instruction.Rva >= image.EntryPointRva)
            ?? result.Instructions[0];
        return new PatchInstructionWindow(instruction, image);
    }

    private static async Task CaptureVisualAsync(FrameworkElement content, string outputPath)
    {
        content.UpdateLayout();
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(content.ActualWidth));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(content.ActualHeight));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        var fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        await using (var stream = File.Create(fullOutputPath))
        {
            encoder.Save(stream);
        }

        Log.Information("Captured window to {Path:l} ({Width}x{Height})", fullOutputPath, pixelWidth, pixelHeight);
    }

    private async Task RunCaptureAsync(
        string outputPath,
        string? openPath,
        AppTheme theme,
        int width,
        int height,
        bool showPalette = false,
        string? panelId = null,
        string? documentId = null,
        string? treeFilter = null,
        string? selectTree = null,
        string? projectPath = null)
    {
        var services = _services ?? throw new InvalidOperationException("Services are not initialized.");
        var themeService = services.GetRequiredService<ThemeService>();
        var viewModel = services.GetRequiredService<MainViewModel>();
        var window = services.GetRequiredService<MainWindow>();

        MainWindow = window;
        window.IsCaptureMode = true;
        window.Width = Math.Max(width, 900);
        window.Height = Math.Max(height, 600);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        themeService.Apply(theme, force: true);

        window.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

        if (!string.IsNullOrWhiteSpace(openPath))
        {
            await viewModel.OpenFileAsync(openPath);

            if (viewModel.Disassembly is not null)
            {
                await viewModel.Disassembly.WaitForCompletionAsync();
            }

            if (viewModel.BinaryAnalysis is not null)
            {
                await viewModel.BinaryAnalysis.WaitForCompletionAsync();
            }
        }

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            await viewModel.OpenProjectFromPathAsync(projectPath);
            if (viewModel.Disassembly is not null)
            {
                await viewModel.Disassembly.WaitForCompletionAsync();
            }

            if (viewModel.BinaryAnalysis is not null)
            {
                await viewModel.BinaryAnalysis.WaitForCompletionAsync();
            }
        }

        if (!string.IsNullOrWhiteSpace(treeFilter))
        {
            viewModel.Explorer.FilterText = treeFilter;
            await Task.Delay(200);
        }

        if (!string.IsNullOrWhiteSpace(selectTree))
        {
            viewModel.Explorer.SelectFirstMatching(selectTree);
            await Task.Delay(200);
        }

        if (!string.IsNullOrWhiteSpace(panelId))
        {
            window.ActivatePanel(panelId);
            await Task.Delay(200);
        }

        if (!string.IsNullOrWhiteSpace(documentId))
        {
            if (string.Equals(documentId, "cfg", StringComparison.OrdinalIgnoreCase))
            {
                viewModel.OpenControlFlowGraphCommand.Execute(null);
                await Task.Delay(300);
                window.ActivateDocument("cfg");
                await Task.Delay(200);
            }
            else if (string.Equals(documentId, "analysis-browser", StringComparison.OrdinalIgnoreCase))
            {
                viewModel.OpenAnalysisBrowserCommand.Execute(null);
                await Task.Delay(300);
                window.ActivateDocument("analysis-browser");
                await Task.Delay(200);
            }
            else if (string.Equals(documentId, "call-graph", StringComparison.OrdinalIgnoreCase))
            {
                viewModel.OpenCallGraphCommand.Execute(null);
                await Task.Delay(300);
                window.ActivateDocument("call-graph");
                await Task.Delay(200);
            }
            else
            {
                window.ActivateDocument(documentId);
                await Task.Delay(200);

                if (string.Equals(documentId, "disassembly", StringComparison.OrdinalIgnoreCase))
                {
                    viewModel.Disassembly?.NavigateToEntryPoint();
                    await Task.Delay(150);
                }
            }
        }

        if (showPalette)
        {
            viewModel.OpenCommandPaletteCommand.Execute(null);
            await Task.Delay(200);
        }

        await Task.Delay(450);
        window.UpdateLayout();

        if (window.Content is not FrameworkElement content)
        {
            throw new InvalidOperationException("The main window has no content to capture.");
        }

        content.UpdateLayout();
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(content.ActualWidth));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(content.ActualHeight));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        var fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        await using (var stream = File.Create(fullOutputPath))
        {
            encoder.Save(stream);
        }

        Log.Information("Captured UI to {Path:l} ({Width}x{Height})", fullOutputPath, pixelWidth, pixelHeight);
        window.Close();
        Shutdown(0);
    }

    private static string? GetOption(IReadOnlyList<string> args, string option)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static int ParseInt(string? text, int fallback) =>
        int.TryParse(text, out var value) ? value : fallback;

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled UI exception");
        e.Handled = true;

        if (_isCaptureMode)
        {
            Shutdown(1);
            return;
        }

        try
        {
            var window = new MessageWindow(
                "Unexpected error",
                "An unhandled error occurred. Details were written to the log file.",
                e.Exception.ToString())
            {
                Owner = MainWindow,
            };
            window.ShowDialog();
        }
        catch (Exception dialogFailure)
        {
            Log.Error(dialogFailure, "Could not display the unhandled-exception dialog.");
        }
    }
}

