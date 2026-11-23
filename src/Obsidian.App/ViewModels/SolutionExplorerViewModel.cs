using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Obsidian.Core.Formatting;
using Obsidian.Core.Model;

namespace Obsidian.App.ViewModels;

public sealed partial class SolutionExplorerViewModel : ObservableObject
{
    private BinaryImage? _image;
    private List<ExplorerNode> _allRoots = [];
    private ExplorerNode? _functionsFolder;
    private ExplorerNode? _stringsFolder;
    private ExplorerNode? _crossReferencesFolder;
    private ExplorerNode? _bookmarksFolder;
    private AnalysisResult? _lastAnalysis;
    private string? _analysisUnavailableMessage;
    private IReadOnlyDictionary<uint, AddressAnnotation> _annotations =
        new Dictionary<uint, AddressAnnotation>();
    private IReadOnlyList<ProjectBookmark> _bookmarks = [];

    public ObservableCollection<ExplorerNode> Roots { get; } = [];

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private ExplorerNode? _selectedNode;

    public event EventHandler<ExplorerNode?>? SelectionChanged;

    public event Action<ExplorerNode>? NodeActivated;

    public event Action<ExplorerNode, ExplorerAnnotationAction>? AnnotationRequested;

    public bool HasImage => _image is not null;

    public bool IsFiltered => !string.IsNullOrWhiteSpace(FilterText);

    public void LoadImage(BinaryImage image)
    {
        _image = image;
        SelectedNode = null;
        _functionsFolder = null;
        _stringsFolder = null;
        _crossReferencesFolder = null;
        _bookmarksFolder = null;
        _lastAnalysis = null;
        _analysisUnavailableMessage = null;
        _annotations = new Dictionary<uint, AddressAnnotation>();
        _bookmarks = [];
        _allRoots = [BuildRoot(image)];
        FilterText = string.Empty;
        ApplyView();
        OnPropertyChanged(nameof(HasImage));
    }

    public void Clear()
    {
        _image = null;
        _functionsFolder = null;
        _stringsFolder = null;
        _crossReferencesFolder = null;
        _bookmarksFolder = null;
        _lastAnalysis = null;
        _analysisUnavailableMessage = null;
        _annotations = new Dictionary<uint, AddressAnnotation>();
        _bookmarks = [];
        _allRoots = [];
        FilterText = string.Empty;
        SelectedNode = null;
        Roots.Clear();
        OnPropertyChanged(nameof(HasImage));
    }

    public void Refresh()
    {
        if (_image is null)
        {
            return;
        }

        _functionsFolder = null;
        _stringsFolder = null;
        _crossReferencesFolder = null;
        _allRoots = [BuildRoot(_image)];

        if (_lastAnalysis is not null)
        {
            LoadAnalysis(_lastAnalysis);
            return;
        }

        if (_analysisUnavailableMessage is not null)
        {
            SetAnalysisUnavailable(_analysisUnavailableMessage);
            return;
        }

        ApplyView();
    }

    public void ActivateNode(ExplorerNode node) => NodeActivated?.Invoke(node);

    public void RequestAnnotation(ExplorerAnnotationAction action)
    {
        if (SelectedNode is { } node)
        {
            AnnotationRequested?.Invoke(node, action);
        }
    }

    public void ApplyAnnotations(
        IReadOnlyDictionary<uint, AddressAnnotation> annotations,
        IReadOnlyList<ProjectBookmark> bookmarks)
    {
        _annotations = annotations;
        _bookmarks = bookmarks;
        ApplyAnnotationsToNodes();
        UpdateBookmarks();
        ApplyView();
    }

    public void LoadAnalysis(AnalysisResult analysis)
    {
        _lastAnalysis = analysis;
        _analysisUnavailableMessage = null;
        if (_functionsFolder is null || _stringsFolder is null || _crossReferencesFolder is null)
        {
            return;
        }

        _functionsFolder.Detail = $"{analysis.FunctionCount:N0} functions";
        _functionsFolder.Payload = analysis;
        _functionsFolder.Children.Clear();

        foreach (var function in analysis.Functions.OrderBy(function => function.Function.Rva))
        {
            var discovered = function.Function;
            _functionsFolder.AddChild(new ExplorerNode
            {
                Title = discovered.Name,
                Detail = $"{Hex.Rva(discovered.Rva)} · {discovered.BasicBlockCount} blocks",
                Icon = ExplorerIcon.Function,
                Kind = ExplorerNodeKind.Function,
                Payload = function,
                ToolTip =
                    $"{discovered.Name}\n{Hex.Rva(discovered.Rva)} · {Hex.Va(discovered.VirtualAddress)}\n" +
                    $"{discovered.InstructionCount:N0} instructions · {discovered.BasicBlockCount} blocks\n" +
                    $"Sources: {discovered.SourceText}",
            });
        }

        _stringsFolder.Detail = $"{analysis.StringCount:N0} strings";
        _stringsFolder.Payload = analysis;
        _stringsFolder.Children.Clear();

        foreach (var group in analysis.Strings
                     .GroupBy(value => value.EncodingText)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var groupNode = new ExplorerNode
            {
                Title = group.Key,
                Detail = $"{group.Count():N0} strings",
                Icon = ExplorerIcon.String,
                Kind = ExplorerNodeKind.Folder,
                Payload = new PropertyBag(
                    $"{group.Key} strings",
                    [
                        new PropertyRowViewModel("Encoding", group.Key),
                        new PropertyRowViewModel("Count", group.Count().ToString("N0")),
                    ]),
                ToolTip = $"{group.Key} strings found in file-backed data sections",
            };

            foreach (var value in group.OrderBy(value => value.Rva))
            {
                groupNode.AddChild(new ExplorerNode
                {
                    Title = $"\"{value.Preview}\"",
                    Detail = $"{Hex.Rva(value.Rva)} · {value.ReferenceCount} refs",
                    Icon = ExplorerIcon.String,
                    Kind = ExplorerNodeKind.String,
                    Payload = value,
                    ToolTip =
                        $"{value.EncodingText} · {value.SectionName}\n" +
                        $"{Hex.Rva(value.Rva)} · {value.ReferenceCount} references",
                });
            }

            _stringsFolder.AddChild(groupNode);
        }

        _crossReferencesFolder.Detail = $"{analysis.CrossReferenceCount:N0} references";
        _crossReferencesFolder.Payload = analysis;
        _crossReferencesFolder.Children.Clear();

        foreach (var group in analysis.CrossReferences
                     .GroupBy(reference => reference.KindText)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var groupNode = new ExplorerNode
            {
                Title = group.Key,
                Detail = $"{group.Count():N0}",
                Icon = ExplorerIcon.CrossReference,
                Kind = ExplorerNodeKind.Folder,
                Payload = new PropertyBag(
                    $"Cross references: {group.Key}",
                    [
                        new PropertyRowViewModel("Kind", group.Key),
                        new PropertyRowViewModel("Count", group.Count().ToString("N0")),
                    ]),
                ToolTip = $"{group.Key} references",
            };

            foreach (var reference in group.OrderBy(reference => reference.FromRva))
            {
                groupNode.AddChild(new ExplorerNode
                {
                    Title = $"{Hex.Rva(reference.FromRva)} → {Hex.Rva(reference.ToRva)}",
                    Detail = reference.SourceText,
                    Icon = ExplorerIcon.CrossReference,
                    Kind = ExplorerNodeKind.CrossReference,
                    Payload = reference,
                    ToolTip =
                        $"{reference.KindText}: {reference.SourceText}\n" +
                        $"{Hex.Rva(reference.FromRva)} ({reference.FromSection}) → " +
                        $"{Hex.Rva(reference.ToRva)} ({reference.ToSection ?? "external"})",
                });
            }

            _crossReferencesFolder.AddChild(groupNode);
        }

        ApplyAnnotationsToNodes();
        UpdateBookmarks();
        ApplyView();
    }

    public void SetAnalysisUnavailable(string message)
    {
        _analysisUnavailableMessage = message;
        _lastAnalysis = null;
        if (_functionsFolder is null || _stringsFolder is null || _crossReferencesFolder is null)
        {
            return;
        }

        foreach (var folder in new[] { _functionsFolder, _stringsFolder, _crossReferencesFolder })
        {
            folder.Detail = "unavailable";
            folder.Children.Clear();
            folder.Payload = new NotAvailable(
                folder.Title,
                message,
                "Analysis");
        }

        UpdateBookmarks();
        ApplyView();
    }

    /// <summary>Expands ancestors and selects a node that exists in the full tree.</summary>
    public void SelectNode(ExplorerNode node)
    {
        if (IsFiltered)
        {
            FilterText = string.Empty;
        }

        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            parent.IsExpanded = true;
        }

        SelectedNode = node;
    }

    public void SelectSection(PeSection section)
    {
        var node = _allRoots
            .SelectMany(r => r.Descendants().Prepend(r))
            .FirstOrDefault(n => ReferenceEquals(n.Payload, section));

        if (node is not null)
        {
            SelectNode(node);
        }
    }

    public bool SelectFirstMatching(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var node = _allRoots
            .SelectMany(root => root.Descendants().Prepend(root))
            .FirstOrDefault(node => node.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                                    || (node.Detail?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));

        if (node is null)
        {
            return false;
        }

        SelectNode(node);
        return true;
    }

    public ExplorerNode? FindSectionNode(string sectionName) =>
        _allRoots
            .SelectMany(r => r.Descendants().Prepend(r))
            .FirstOrDefault(n => n.Payload is PeSection section
                                 && string.Equals(section.Name, sectionName, StringComparison.Ordinal));

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var node in _allRoots.SelectMany(r => r.Descendants().Prepend(r)))
        {
            node.IsExpanded = false;
        }

        if (_allRoots.Count > 0)
        {
            _allRoots[0].IsExpanded = true;
        }
    }

    [RelayCommand]
    private void ClearFilter() => FilterText = string.Empty;

    partial void OnFilterTextChanged(string value) => ApplyView();

    partial void OnSelectedNodeChanged(ExplorerNode? value) =>
        SelectionChanged?.Invoke(this, value);

    private void ApplyView()
    {
        Roots.Clear();

        var filter = FilterText.Trim();
        if (filter.Length == 0)
        {
            foreach (var root in _allRoots)
            {
                Roots.Add(root);
            }

            OnPropertyChanged(nameof(IsFiltered));
            return;
        }

        foreach (var root in _allRoots)
        {
            var filtered = FilterNode(root, filter);
            if (filtered is not null)
            {
                Roots.Add(filtered);
            }
        }

        OnPropertyChanged(nameof(IsFiltered));
    }

    private static ExplorerNode? FilterNode(ExplorerNode node, string filter)
    {
        if (node.Matches(filter))
        {
            return Clone(node, includeChildren: true, forceExpand: true);
        }

        var children = node.Children
            .Select(child => FilterNode(child, filter))
            .Where(child => child is not null)
            .Cast<ExplorerNode>()
            .ToArray();

        if (children.Length == 0)
        {
            return null;
        }

        var clone = Clone(node, includeChildren: false, forceExpand: true);
        foreach (var child in children)
        {
            clone.AddChild(child);
        }

        return clone;
    }

    private static ExplorerNode Clone(ExplorerNode node, bool includeChildren, bool forceExpand)
    {
        var clone = new ExplorerNode
        {
            Title = node.Title,
            Detail = node.Detail,
            ToolTip = node.ToolTip,
            Icon = node.Icon,
            Kind = node.Kind,
            Payload = node.Payload,
            IsExpanded = forceExpand || node.IsExpanded,
        };

        if (includeChildren)
        {
            foreach (var child in node.Children)
            {
                clone.AddChild(Clone(child, includeChildren: true, forceExpand: false));
            }
        }

        return clone;
    }

    private ExplorerNode BuildRoot(BinaryImage image)
    {
        var root = new ExplorerNode
        {
            Title = image.FileName,
            Detail = $"{image.FormatText} · {image.ArchitectureText}",
            ToolTip = image.FilePath,
            Icon = ExplorerIcon.Binary,
            Kind = ExplorerNodeKind.Binary,
            Payload = image,
            IsExpanded = true,
        };

        root.AddChild(BuildExecutableFolder(image));
        root.AddChild(BuildHeadersFolder(image));
        root.AddChild(BuildSectionsFolder(image));
        root.AddChild(BuildFunctionsFolder());
        root.AddChild(BuildImportsFolder(image));
        root.AddChild(BuildExportsFolder(image));
        root.AddChild(BuildStringsFolder());
        root.AddChild(BuildResourcesFolder(image));
        root.AddChild(BuildCrossReferencesFolder());
        root.AddChild(BuildBookmarksFolder());
        root.AddChild(BuildFindingsFolder(image));

        return root;
    }

    private static ExplorerNode BuildExecutableFolder(BinaryImage image)
    {
        var folder = new ExplorerNode
        {
            Title = "Executable",
            Icon = ExplorerIcon.Folder,
            Kind = ExplorerNodeKind.Folder,
            Payload = image,
            ToolTip = "Image identity and loading properties",
        };

        AddProperty(folder, "Format", image.FormatText);
        AddProperty(folder, "Architecture", image.Architecture.ToDisplayName());
        AddProperty(folder, "Subsystem", image.Subsystem.ToDisplayName());
        AddProperty(folder, "Entry point", Hex.Rva(image.EntryPointRva), mono: true);
        AddProperty(folder, "Image base", Hex.Va(image.ImageBase), mono: true);
        AddProperty(folder, "File size", Hex.Size(image.FileSize));
        AddProperty(folder, "SHA-256", image.Sha256, mono: true);
        AddProperty(folder, "Managed image", image.IsManaged ? "Yes" : "No");
        AddProperty(folder, "Timestamp", Hex.Timestamp(image.TimeDateStampUtc));

        return folder;
    }

    private static ExplorerNode BuildHeadersFolder(BinaryImage image)
    {
        var folder = new ExplorerNode
        {
            Title = "PE Headers",
            Icon = ExplorerIcon.Folder,
            Kind = ExplorerNodeKind.Folder,
            Payload = image,
            ToolTip = "DOS, COFF, optional header and data directories",
        };

        var dos = new ExplorerNode
        {
            Title = "DOS header",
            Icon = ExplorerIcon.Header,
            Kind = ExplorerNodeKind.Info,
            Payload = new PropertyBag(
                "DOS header",
                [
                    new PropertyRowViewModel("Signature", "MZ (0x5A4D)", true),
                    new PropertyRowViewModel("e_lfanew", Hex.UInt32(image.PeHeaderOffset), true),
                ]),
        };

        var coff = new ExplorerNode
        {
            Title = "COFF file header",
            Icon = ExplorerIcon.Header,
            Kind = ExplorerNodeKind.Info,
            Payload = new PropertyBag(
                "COFF file header",
                [
                    new PropertyRowViewModel("Machine", $"0x{image.Machine:X4}", true),
                    new PropertyRowViewModel("Sections", image.Sections.Count.ToString()),
                    new PropertyRowViewModel("Characteristics", $"0x{image.Characteristics:X4}", true),
                    new PropertyRowViewModel("DLL", image.IsDll ? "Yes" : "No"),
                    new PropertyRowViewModel("Executable", image.IsExe ? "Yes" : "No"),
                ]),
        };

        var optional = new ExplorerNode
        {
            Title = "Optional header",
            Icon = ExplorerIcon.Header,
            Kind = ExplorerNodeKind.Info,
            Payload = new PropertyBag(
                "Optional header",
                [
                    new PropertyRowViewModel("Image base", Hex.Va(image.ImageBase), true),
                    new PropertyRowViewModel("Entry point RVA", Hex.Rva(image.EntryPointRva), true),
                    new PropertyRowViewModel("Size of image", Hex.UInt32(image.SizeOfImage), true),
                    new PropertyRowViewModel("Size of headers", Hex.UInt32(image.SizeOfHeaders), true),
                    new PropertyRowViewModel("Section alignment", Hex.UInt32(image.SectionAlignment), true),
                    new PropertyRowViewModel("File alignment", Hex.UInt32(image.FileAlignment), true),
                    new PropertyRowViewModel("Subsystem", image.Subsystem.ToDisplayName()),
                ]),
        };

        var directories = new ExplorerNode
        {
            Title = "Data directories",
            Detail = $"{image.DataDirectories.Count(d => d.IsPresent)} present",
            Icon = ExplorerIcon.Header,
            Kind = ExplorerNodeKind.Folder,
            Payload = new PropertyBag(
                "Data directories",
                [
                    new PropertyRowViewModel("Declared entries", image.DataDirectories.Count.ToString()),
                    new PropertyRowViewModel("Present entries", image.DataDirectories.Count(d => d.IsPresent).ToString()),
                    new PropertyRowViewModel("Absent entries", image.DataDirectories.Count(d => !d.IsPresent).ToString()),
                ]),
        };

        foreach (var directory in image.DataDirectories.Where(d => d.IsPresent))
        {
            directories.AddChild(new ExplorerNode
            {
                Title = directory.Name,
                Detail = Hex.Rva(directory.VirtualAddress),
                Icon = ExplorerIcon.Data,
                Kind = ExplorerNodeKind.DataDirectory,
                Payload = directory,
                ToolTip = $"{directory.Name}: {directory.Size:N0} bytes",
            });
        }

        folder.AddChild(dos);
        folder.AddChild(coff);
        folder.AddChild(optional);
        folder.AddChild(directories);
        folder.AddChild(BuildRelocationsNode(image));
        folder.AddChild(BuildTlsNode(image));
        folder.AddChild(BuildExceptionsNode(image));
        folder.AddChild(BuildDebugNode(image));
        folder.AddChild(BuildLoadConfigurationNode(image));
        return folder;
    }

    private static ExplorerNode BuildRelocationsNode(BinaryImage image)
    {
        var info = image.Relocations;
        return new ExplorerNode
        {
            Title = "Base relocations",
            Detail = info.Count == 0 ? "none" : $"{info.Count:N0} entries · {info.PageCount} pages",
            Icon = ExplorerIcon.Data,
            Kind = ExplorerNodeKind.Folder,
            Payload = info,
            ToolTip = "Base relocation table; select to see type counts",
        };
    }

    private static ExplorerNode BuildTlsNode(BinaryImage image)
    {
        if (image.Tls is not { } tls)
        {
            return new ExplorerNode
            {
                Title = "Thread-local storage",
                Detail = image.HasTls ? "unreadable" : "absent",
                Icon = ExplorerIcon.Data,
                Kind = ExplorerNodeKind.Info,
                Payload = new NotAvailable(
                    "TLS directory",
                    image.HasTls
                        ? "The TLS data directory is present but its contents could not be parsed."
                        : "This image has no TLS data directory.",
                    image.HasTls ? "Not parsed" : "—"),
            };
        }

        var folder = new ExplorerNode
        {
            Title = "Thread-local storage",
            Detail = $"{tls.CallbackCount} callbacks",
            Icon = ExplorerIcon.Data,
            Kind = ExplorerNodeKind.Folder,
            Payload = tls,
            ToolTip = "Thread-local storage index, template and callbacks",
        };

        for (var i = 0; i < tls.Callbacks.Count; i++)
        {
            var callback = tls.Callbacks[i];
            folder.AddChild(new ExplorerNode
            {
                Title = $"Callback {i}",
                Detail = callback.VirtualAddress == 0 ? "unresolved" : Hex.Va(callback.VirtualAddress),
                Icon = ExplorerIcon.Function,
                Kind = ExplorerNodeKind.Info,
                Payload = callback,
                ToolTip = "Runs before the entry point",
            });
        }

        return folder;
    }

    private static ExplorerNode BuildExceptionsNode(BinaryImage image)
    {
        if (image.Exceptions is not { } exceptions)
        {
            return new ExplorerNode
            {
                Title = "Exception runtime functions",
                Detail = image.HasExceptions ? "unreadable" : "absent",
                Icon = ExplorerIcon.Data,
                Kind = ExplorerNodeKind.Info,
                Payload = new NotAvailable(
                    "Exception directory",
                    image.HasExceptions
                        ? "The exception directory is present but its runtime functions could not be parsed."
                        : "This image has no exception directory.",
                    image.HasExceptions ? "Not parsed" : "—"),
            };
        }

        return new ExplorerNode
        {
            Title = "Exception runtime functions",
            Detail = $"{exceptions.Count:N0} runtime functions",
            Icon = ExplorerIcon.Data,
            Kind = ExplorerNodeKind.Folder,
            Payload = exceptions,
            ToolTip = exceptions.IsX64
                ? "x64 RUNTIME_FUNCTION table (unwind metadata)"
                : "Runtime function table",
        };
    }

    private static ExplorerNode BuildDebugNode(BinaryImage image)
    {
        var entries = image.DebugEntries;
        if (entries.Count == 0 && !image.HasDebugData)
        {
            return new ExplorerNode
            {
                Title = "Debug entries",
                Detail = "absent",
                Icon = ExplorerIcon.Data,
                Kind = ExplorerNodeKind.Info,
                Payload = new NotAvailable(
                    "Debug directory",
                    "This image has no debug directory.",
                    "—"),
            };
        }

        var folder = new ExplorerNode
        {
            Title = "Debug entries",
            Detail = $"{entries.Count} entries",
            Icon = ExplorerIcon.Data,
            Kind = ExplorerNodeKind.Folder,
            Payload = new PropertyBag(
                "Debug directory",
                [
                    new PropertyRowViewModel("Entries", entries.Count.ToString("N0")),
                    new PropertyRowViewModel("CodeView entries", entries.Count(e => e.TypeName == "CodeView").ToString()),
                    new PropertyRowViewModel("PDB paths", entries.Count(e => !string.IsNullOrEmpty(e.PdbPath)).ToString()),
                ]),
            ToolTip = "Debug directory: CodeView/PDB, POGO, embedded PDB and checksum entries",
        };

        foreach (var entry in entries)
        {
            var detail = entry.PdbPath is { Length: > 0 } pdb
                ? Path.GetFileName(pdb)
                : entry.TimestampUtc is { } timestamp
                    ? timestamp.ToString("yyyy-MM-dd")
                    : Hex.Offset(entry.FileOffset ?? 0);
            folder.AddChild(new ExplorerNode
            {
                Title = entry.TypeName,
                Detail = detail,
                Icon = ExplorerIcon.Header,
                Kind = ExplorerNodeKind.Info,
                Payload = entry,
                ToolTip = entry.PdbPath ?? entry.TypeName,
            });
        }

        return folder;
    }

    private static ExplorerNode BuildLoadConfigurationNode(BinaryImage image)
    {
        if (image.LoadConfiguration is not { } configuration)
        {
            return new ExplorerNode
            {
                Title = "Guard configuration",
                Detail = image.HasLoadConfiguration ? "unreadable" : "absent",
                Icon = ExplorerIcon.Data,
                Kind = ExplorerNodeKind.Info,
                Payload = new NotAvailable(
                    "Load configuration",
                    image.HasLoadConfiguration
                        ? "The load configuration directory is present but could not be parsed."
                        : "This image has no load configuration directory.",
                    image.HasLoadConfiguration ? "Not parsed" : "—"),
            };
        }

        return new ExplorerNode
        {
            Title = "Guard configuration",
            Detail = configuration.GuardFlags,
            Icon = ExplorerIcon.Data,
            Kind = ExplorerNodeKind.Folder,
            Payload = configuration,
            ToolTip = "Guard flags and load-time security settings",
        };
    }

    private static ExplorerNode BuildSectionsFolder(BinaryImage image)
    {
        var folder = new ExplorerNode
        {
            Title = "Sections",
            Detail = image.Sections.Count.ToString(),
            Icon = ExplorerIcon.Folder,
            Kind = ExplorerNodeKind.Folder,
            Payload = image,
            IsExpanded = true,
            ToolTip = "PE section table",
        };

        foreach (var section in image.Sections)
        {
            folder.AddChild(new ExplorerNode
            {
                Title = section.Name,
                Detail = $"{section.Permissions} · {Hex.Size(section.RawSize)}",
                Icon = section.IsExecutable ? ExplorerIcon.Code : ExplorerIcon.Data,
                Kind = ExplorerNodeKind.Section,
                Payload = section,
                ToolTip =
                    $"{section.Name}\nRVA {Hex.Rva(section.VirtualAddress)} · {section.Kind}\n" +
                    $"Raw {Hex.Offset(section.RawOffset)} ({section.RawSize:N0} bytes)\n" +
                    $"Virtual {section.VirtualSize:N0} bytes · {section.Permissions}",
            });
        }

        return folder;
    }

    private ExplorerNode BuildFunctionsFolder()
    {
        var folder = new ExplorerNode
        {
            Title = "Functions",
            Detail = "Analyzing…",
            Icon = ExplorerIcon.Function,
            Kind = ExplorerNodeKind.Folder,
            Payload = new NotAvailable(
                "Function discovery",
                "Functions are discovered from entry point, exports, call targets, exception tables and TLS callbacks after disassembly completes.",
                "Analysis"),
            ToolTip = "Seed-based function discovery and basic-block CFGs",
        };

        _functionsFolder = folder;
        return folder;
    }

    private static ExplorerNode BuildImportsFolder(BinaryImage image)
    {
        var folder = new ExplorerNode
        {
            Title = "Imports",
            Detail = $"{image.Imports.Count} modules · {image.ImportedFunctionCount} functions",
            Icon = ExplorerIcon.Folder,
            Kind = ExplorerNodeKind.Folder,
            Payload = new PropertyBag(
                "Import table",
                [
                    new PropertyRowViewModel("Modules", image.Imports.Count.ToString("N0")),
                    new PropertyRowViewModel("Imported functions", image.ImportedFunctionCount.ToString("N0")),
                    new PropertyRowViewModel("Directory present", image.DataDirectories[1].IsPresent ? "Yes" : "No"),
                ]),
            ToolTip = "Imported modules and symbols",
        };

        foreach (var module in image.Imports)
        {
            var moduleNode = new ExplorerNode
            {
                Title = module.Name,
                Detail = module.FunctionCount.ToString(),
                Icon = ExplorerIcon.Module,
                Kind = ExplorerNodeKind.ImportModule,
                Payload = module,
                ToolTip = $"{module.Name}: {module.FunctionCount} imported symbols",
            };

            foreach (var symbol in module.Symbols)
            {
                moduleNode.AddChild(new ExplorerNode
                {
                    Title = string.IsNullOrEmpty(symbol.Name) ? $"#{symbol.Ordinal}" : symbol.Name,
                    Icon = ExplorerIcon.Function,
                    Kind = ExplorerNodeKind.ImportFunction,
                    Payload = symbol,
                    ToolTip = symbol.IsByOrdinal
                        ? $"{module.Name} ordinal #{symbol.Ordinal}"
                        : $"{module.Name}!{symbol.Name} (hint {symbol.Hint})",
                });
            }

            folder.AddChild(moduleNode);
        }

        return folder;
    }

    private static ExplorerNode BuildExportsFolder(BinaryImage image)
    {
        var folder = new ExplorerNode
        {
            Title = "Exports",
            Detail = image.Exports.Count.ToString(),
            Icon = ExplorerIcon.Folder,
            Kind = ExplorerNodeKind.Folder,
            Payload = new PropertyBag(
                "Export table",
                [
                    new PropertyRowViewModel("Exported symbols", image.Exports.Count.ToString("N0")),
                    new PropertyRowViewModel("Forwarded", image.Exports.Count(e => e.IsForwarder).ToString("N0")),
                    new PropertyRowViewModel("Directory present", image.DataDirectories[0].IsPresent ? "Yes" : "No"),
                ]),
            ToolTip = "Exported symbols",
        };

        foreach (var export in image.Exports)
        {
            folder.AddChild(new ExplorerNode
            {
                Title = export.DisplayName,
                Detail = export.IsForwarder
                    ? "forwarded"
                    : export.Rva is { } rva ? Hex.Rva(rva) : string.Empty,
                Icon = ExplorerIcon.Export,
                Kind = ExplorerNodeKind.Export,
                Payload = export,
                ToolTip = export.IsForwarder
                    ? $"{export.DisplayName} → {export.ForwarderName}"
                    : $"{export.DisplayName} (ordinal {export.Ordinal})",
            });
        }

        return folder;
    }

    private ExplorerNode BuildStringsFolder()
    {
        var folder = new ExplorerNode
        {
            Title = "Strings",
            Detail = "Analyzing…",
            Icon = ExplorerIcon.String,
            Kind = ExplorerNodeKind.Folder,
            Payload = new NotAvailable(
                "String extraction",
                "ASCII and UTF-16LE strings are extracted from non-executable file-backed sections after analysis completes.",
                "Analysis"),
            ToolTip = "ASCII and UTF-16LE candidate strings",
        };

        _stringsFolder = folder;
        return folder;
    }

    private static ExplorerNode BuildResourcesFolder(BinaryImage image)
    {
        if (image.Resources.Count == 0)
        {
            return new ExplorerNode
            {
                Title = "Resources",
                Detail = image.HasResources ? "unreadable" : "none",
                Icon = ExplorerIcon.Resource,
                Kind = ExplorerNodeKind.Info,
                Payload = new NotAvailable(
                    "Resource directory",
                    image.HasResources
                        ? "The resource directory is present but no entries could be parsed."
                        : "This image has no resource data directory.",
                    image.HasResources ? "Not parsed" : "—"),
            };
        }

        var folder = new ExplorerNode
        {
            Title = "Resources",
            Detail = $"{image.Resources.Count:N0} entries",
            Icon = ExplorerIcon.Resource,
            Kind = ExplorerNodeKind.Folder,
            Payload = new PropertyBag(
                "Resources",
                [
                    new PropertyRowViewModel("Entries", image.Resources.Count.ToString("N0")),
                    new PropertyRowViewModel("Types", image.Resources.Select(r => r.TypeName).Distinct().Count().ToString()),
                    new PropertyRowViewModel("Languages", image.Resources.Select(r => r.LanguageName).Distinct().Count().ToString()),
                    new PropertyRowViewModel("Total size", Hex.Size(image.Resources.Sum(r => (long)r.Size))),
                ]),
            ToolTip = "Win32 resource directory parsed into type / name / language",
        };

        foreach (var typeGroup in image.Resources
                     .GroupBy(resource => resource.TypeName, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var typeFolder = new ExplorerNode
            {
                Title = typeGroup.Key,
                Detail = CountText(typeGroup.Count(), "entry", "entries"),
                Icon = ExplorerIcon.Resource,
                Kind = ExplorerNodeKind.Folder,
                Payload = new PropertyBag(
                    $"Resource type: {typeGroup.Key}",
                    [
                        new PropertyRowViewModel("Type", typeGroup.Key),
                        new PropertyRowViewModel("Entries", typeGroup.Count().ToString("N0")),
                        new PropertyRowViewModel("Total size", Hex.Size(typeGroup.Sum(r => (long)r.Size))),
                    ]),
                ToolTip = $"Resource type {typeGroup.Key}",
            };

            foreach (var nameGroup in typeGroup
                         .GroupBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var nameNode = new ExplorerNode
                {
                    Title = nameGroup.Key,
                    Detail = nameGroup.Count() > 1 ? $"{nameGroup.Count()} languages" : string.Empty,
                    Icon = ExplorerIcon.Resource,
                    Kind = ExplorerNodeKind.Folder,
                    Payload = new PropertyBag(
                        $"Resource: {typeGroup.Key} / {nameGroup.Key}",
                        [
                            new PropertyRowViewModel("Type", typeGroup.Key),
                            new PropertyRowViewModel("Name", nameGroup.Key),
                            new PropertyRowViewModel("Languages", nameGroup.Count().ToString()),
                            new PropertyRowViewModel("Total size", Hex.Size(nameGroup.Sum(r => (long)r.Size))),
                        ]),
                    ToolTip = $"{typeGroup.Key} / {nameGroup.Key}",
                };

                foreach (var resource in nameGroup.OrderBy(r => r.LanguageName, StringComparer.Ordinal))
                {
                    nameNode.AddChild(new ExplorerNode
                    {
                        Title = resource.LanguageName,
                        Detail = Hex.Size(resource.Size),
                        Icon = ExplorerIcon.Data,
                        Kind = ExplorerNodeKind.Info,
                        Payload = resource,
                        ToolTip =
                            $"{resource.TypeName} / {resource.Name} / {resource.LanguageName}\n" +
                            $"{(resource.Rva is { } rva ? Hex.Rva(rva) : "no RVA")} · " +
                            $"{(resource.FileOffset is { } offset ? Hex.Offset(offset) : "no file data")} · " +
                            $"{resource.Size:N0} bytes",
                    });
                }

                typeFolder.AddChild(nameNode);
            }

            folder.AddChild(typeFolder);
        }

        return folder;
    }

    private static string CountText(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private ExplorerNode BuildCrossReferencesFolder()
    {
        var folder = new ExplorerNode
        {
            Title = "Cross References",
            Detail = "Analyzing…",
            Icon = ExplorerIcon.CrossReference,
            Kind = ExplorerNodeKind.Folder,
            Payload = new NotAvailable(
                "Cross references",
                "Call, jump and RIP-relative data references are indexed after disassembly completes.",
                "Analysis"),
            ToolTip = "Call, jump and data references",
        };

        _crossReferencesFolder = folder;
        return folder;
    }

    private ExplorerNode BuildBookmarksFolder()
    {
        var folder = new ExplorerNode
        {
            Title = "Bookmarks",
            Detail = "0",
            Icon = ExplorerIcon.Symbol,
            Kind = ExplorerNodeKind.Folder,
            Payload = new PropertyBag(
                "Bookmarks",
                [new PropertyRowViewModel("Items", "0")]),
            ToolTip = "Project bookmarks; double-click navigates to the address",
        };

        _bookmarksFolder = folder;
        return folder;
    }

    private void ApplyAnnotationsToNodes()
    {
        foreach (var node in _allRoots.SelectMany(root => root.Descendants().Prepend(root)))
        {
            if (node.Payload is not FunctionAnalysis function)
            {
                continue;
            }

            var annotation = _annotations.GetValueOrDefault(function.Function.Rva);
            node.Title = string.IsNullOrWhiteSpace(annotation?.UserName)
                ? function.Function.Name
                : annotation.UserName!;
        }
    }

    private void UpdateBookmarks()
    {
        if (_bookmarksFolder is null)
        {
            return;
        }

        _bookmarksFolder.Detail = _bookmarks.Count.ToString();
        _bookmarksFolder.Payload = new PropertyBag(
            "Bookmarks",
            [new PropertyRowViewModel("Items", _bookmarks.Count.ToString())]);
        _bookmarksFolder.Children.Clear();

        foreach (var bookmark in _bookmarks.OrderBy(bookmark => bookmark.Rva))
        {
            _bookmarksFolder.AddChild(new ExplorerNode
            {
                Title = bookmark.Title,
                Detail = $"{Hex.Rva(bookmark.Rva)} · {bookmark.CreatedUtc:yyyy-MM-dd}",
                Icon = ExplorerIcon.Symbol,
                Kind = ExplorerNodeKind.Info,
                Payload = bookmark,
                ToolTip = bookmark.Comment ?? bookmark.Title,
            });
        }
    }

    private static ExplorerNode BuildFindingsFolder(BinaryImage image)
    {
        var folder = new ExplorerNode
        {
            Title = "Analysis Results",
            Detail = image.Findings.Count.ToString(),
            Icon = ExplorerIcon.Analysis,
            Kind = ExplorerNodeKind.Folder,
            Payload = new PropertyBag(
                "Structural observations",
                [
                    new PropertyRowViewModel("Observations", image.Findings.Count.ToString("N0")),
                    new PropertyRowViewModel("Warnings", image.WarningCount.ToString("N0")),
                    new PropertyRowViewModel("Informational", (image.Findings.Count - image.WarningCount).ToString("N0")),
                    new PropertyRowViewModel("Note", "Observations are not a malware verdict."),
                ]),
            ToolTip = "Structural observations, not a malware verdict",
        };

        foreach (var finding in image.Findings)
        {
            folder.AddChild(new ExplorerNode
            {
                Title = finding.Title,
                Detail = finding.Code,
                Icon = finding.Severity == PeFindingSeverity.Warning
                    ? ExplorerIcon.Warning
                    : ExplorerIcon.Info,
                Kind = ExplorerNodeKind.Finding,
                Payload = finding,
                ToolTip = finding.Detail,
            });
        }

        return folder;
    }

    private static void AddProperty(
        ExplorerNode parent,
        string label,
        string value,
        bool mono = false) =>
        parent.AddChild(new ExplorerNode
        {
            Title = label,
            Detail = value,
            Icon = ExplorerIcon.Property,
            Kind = ExplorerNodeKind.Property,
            Payload = new PropertyRowViewModel(label, value, mono),
            ToolTip = value,
        });
}
