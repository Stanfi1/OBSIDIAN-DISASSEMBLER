using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AvalonDock.Layout;
using AvalonDock.Themes.VS;
using Obsidian.App.Services;
using Obsidian.App.ViewModels;
using Obsidian.Core.Model;
using Serilog;

namespace Obsidian.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ThemeService _themeService;
    private readonly SettingsService _settingsService;
    private readonly IDialogService _dialogs;
    private LayoutDocument? _startDocument;
    private LayoutDocument? _overviewDocument;
    private LayoutDocument? _disassemblyDocument;
    private LayoutDocument? _analysisBrowserDocument;
    private LayoutDocument? _callGraphDocument;
    private LayoutDocument? _graphDocument;
    private LayoutDocument? _hexDocument;
    private bool _closingDocuments;
    private bool _eventsDetached;
    private bool _allowClose;

    public MainWindow(
        MainViewModel viewModel,
        ThemeService themeService,
        SettingsService settingsService,
        IDialogService dialogs)
    {
        _viewModel = viewModel;
        _themeService = themeService;
        _settingsService = settingsService;
        _dialogs = dialogs;

        InitializeComponent();
        DataContext = viewModel;

        RestoreWindowBounds();
        CreateStartDocument();

        viewModel.BinaryLoaded += OnBinaryLoaded;
        viewModel.BinaryClosed += OnBinaryClosed;
        viewModel.OpenDisassemblyRequested += ShowDisassemblyDocument;
        viewModel.OpenAnalysisBrowserRequested += ShowAnalysisBrowserDocument;
        viewModel.OpenCallGraphRequested += ShowCallGraphDocument;
        viewModel.OpenGraphRequested += ShowGraphDocument;
        viewModel.OpenHexRequested += ShowHexDocument;
        viewModel.PanelToggleRequested += TogglePanel;
        viewModel.CloseActiveDocumentRequested += CloseActiveDocument;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _themeService.ThemeChanged += OnThemeChanged;

        Loaded += OnLoaded;
    }

    public bool IsCaptureMode { get; set; }

    /// <summary>Diagnostic helper: activates one tool window for visual capture.</summary>
    public void ActivatePanel(string contentId)
    {
        var anchorable = DockManager.Layout
            .Descendents()
            .OfType<LayoutAnchorable>()
            .FirstOrDefault(item => string.Equals(item.ContentId, contentId, StringComparison.Ordinal));

        if (anchorable is null)
        {
            return;
        }

        if (!anchorable.IsVisible)
        {
            anchorable.Show();
        }

        anchorable.IsActive = true;
        anchorable.IsSelected = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyDockTheme();
        UpdateMaximizeRestoreIcon();
        AttachPanelCloseBehaviour();
    }

    private void DockManager_OnActiveContentChanged(object? sender, EventArgs e)
    {
        if (DockManager.ActiveContent is not DisassemblyView)
        {
            return;
        }

        if (_viewModel.Disassembly is
            {
                SelectedInstruction: null,
                SelectedIndex: < 0,
                IsBusy: false,
                HasResult: true,
            } disassembly)
        {
            disassembly.NavigateToEntryPoint();
        }
    }

    private void AttachPanelCloseBehaviour()
    {
        foreach (var anchorable in DockManager.Layout
                     .Descendents()
                     .OfType<LayoutAnchorable>())
        {
            anchorable.Closing += (_, args) =>
            {
                // Closing a tool window hides it instead of destroying it, so
                // the View menu can bring the panel back in this session.
                args.Cancel = true;
                anchorable.Hide();
            };
        }
    }

    private void RestoreWindowBounds()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1600, workArea.Width);
        Height = Math.Min(1000, workArea.Height);

        if (!_settingsService.Current.RememberWindowSize)
        {
            return;
        }

        if (_settingsService.Current.WindowWidth >= MinWidth
            && _settingsService.Current.WindowHeight >= MinHeight)
        {
            Width = Math.Min(_settingsService.Current.WindowWidth, workArea.Width);
            Height = Math.Min(_settingsService.Current.WindowHeight, workArea.Height);
        }

        if (_settingsService.Current.WindowLeft is { } left
            && _settingsService.Current.WindowTop is { } top)
        {
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

            if (left + 120 >= virtualLeft && left + Width - 120 <= virtualRight
                && top + 40 >= virtualTop && top + Height - 120 <= virtualBottom)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }
    }

    private void CreateStartDocument()
    {
        if (_startDocument is not null && DocumentPane.Children.Contains(_startDocument))
        {
            return;
        }

        _startDocument = new LayoutDocument
        {
            Title = "Start Page",
            ContentId = "startPage",
            CanClose = false,
            Content = new StartPageView { DataContext = _viewModel },
            ToolTip = "Start a new analysis",
        };

        DocumentPane.Children.Add(_startDocument);
        if (_overviewDocument is null)
        {
            _startDocument.IsActive = true;
        }
    }

    private void OnBinaryLoaded()
    {
        if (_viewModel.Overview is null)
        {
            return;
        }

        RemoveOverviewDocument();
        RemoveGraphDocument();
        RemoveAnalysisBrowserDocument();
        RemoveCallGraphDocument();

        var overview = _viewModel.Overview;
        var document = new LayoutDocument
        {
            Title = $"{overview.FileName} — Overview",
            ContentId = "overview",
            CanClose = true,
            Content = new OverviewView { DataContext = overview },
            ToolTip = overview.FilePath,
        };
        document.Closed += (_, _) => OnOverviewDocumentClosed(document);

        _overviewDocument = document;
        DocumentPane.Children.Add(document);
        document.IsActive = true;

        CreateDisassemblyDocument();
        CreateHexDocument();
    }

    private void CreateDisassemblyDocument()
    {
        RemoveDisassemblyDocument();

        if (_viewModel.Disassembly is null)
        {
            return;
        }

        var disassemblyDocument = new LayoutDocument
        {
            Title = $"{_viewModel.Image?.FileName ?? "Binary"} — Disassembly",
            ContentId = "disassembly",
            CanClose = true,
            Content = new DisassemblyView { DataContext = _viewModel.Disassembly },
            ToolTip = "Linear x86/x64 disassembly produced by Iced",
        };
        disassemblyDocument.Closed += (_, _) =>
        {
            if (ReferenceEquals(_disassemblyDocument, disassemblyDocument))
            {
                _disassemblyDocument = null;
            }
        };

        _disassemblyDocument = disassemblyDocument;
        DocumentPane.Children.Add(disassemblyDocument);
    }

    private void ShowDisassemblyDocument()
    {
        if (_viewModel.Disassembly is null)
        {
            return;
        }

        if (_disassemblyDocument is null)
        {
            CreateDisassemblyDocument();
        }

        if (_disassemblyDocument is not null)
        {
            _disassemblyDocument.IsActive = true;
            ActivateDocument("disassembly");

            if (_viewModel.Disassembly is
                {
                    SelectedInstruction: null,
                    SelectedIndex: < 0,
                    IsBusy: false,
                    HasResult: true,
                } disassembly)
            {
                disassembly.NavigateToEntryPoint();
            }
        }
    }

    private void RemoveDisassemblyDocument()
    {
        if (_disassemblyDocument is null)
        {
            return;
        }

        var document = _disassemblyDocument;
        _disassemblyDocument = null;
        if (DocumentPane.Children.Contains(document))
        {
            DocumentPane.Children.Remove(document);
        }
    }

    private void ShowGraphDocument(FunctionAnalysis function)
    {
        if (_viewModel.Graph is null)
        {
            return;
        }

        _viewModel.Graph.SelectFunction(function);

        if (_graphDocument is null)
        {
            var graphDocument = new LayoutDocument
            {
                Title = $"CFG — {function.Function.Name}",
                ContentId = "cfg",
                CanClose = true,
                Content = new FunctionGraphView { DataContext = _viewModel.Graph },
                ToolTip = "Interactive control-flow graph",
            };
            graphDocument.Closed += (_, _) =>
            {
                if (ReferenceEquals(_graphDocument, graphDocument))
                {
                    _graphDocument = null;
                }
            };

            _graphDocument = graphDocument;
            DocumentPane.Children.Add(graphDocument);
        }

        _graphDocument.Title = $"CFG — {function.Function.Name}";
        _graphDocument.IsActive = true;
        ActivateDocument("cfg");
    }

    private void RemoveGraphDocument()
    {
        if (_graphDocument is null)
        {
            return;
        }

        var document = _graphDocument;
        _graphDocument = null;
        if (DocumentPane.Children.Contains(document))
        {
            DocumentPane.Children.Remove(document);
        }
    }

    private void ShowAnalysisBrowserDocument()
    {
        if (_viewModel.AnalysisBrowser is null)
        {
            return;
        }

        if (_analysisBrowserDocument is null)
        {
            var document = new LayoutDocument
            {
                Title = "Analysis Browser",
                ContentId = "analysis-browser",
                CanClose = true,
                Content = new AnalysisBrowserView { DataContext = _viewModel.AnalysisBrowser },
                ToolTip = "Searchable, virtualized functions, strings and cross references",
            };
            document.Closed += (_, _) =>
            {
                if (ReferenceEquals(_analysisBrowserDocument, document))
                {
                    _analysisBrowserDocument = null;
                }
            };

            _analysisBrowserDocument = document;
            DocumentPane.Children.Add(document);
        }

        _analysisBrowserDocument.IsActive = true;
        ActivateDocument("analysis-browser");
    }

    private void RemoveAnalysisBrowserDocument()
    {
        if (_analysisBrowserDocument is null)
        {
            return;
        }

        var document = _analysisBrowserDocument;
        _analysisBrowserDocument = null;
        if (DocumentPane.Children.Contains(document))
        {
            DocumentPane.Children.Remove(document);
        }
    }

    private void ShowCallGraphDocument()
    {
        if (_viewModel.CallGraph is null)
        {
            return;
        }

        if (_callGraphDocument is null)
        {
            var document = new LayoutDocument
            {
                Title = "Call Graph",
                ContentId = "call-graph",
                CanClose = true,
                Content = new CallGraphView { DataContext = _viewModel.CallGraph },
                ToolTip = "Direct callers and callees of the selected function",
            };
            document.Closed += (_, _) =>
            {
                if (ReferenceEquals(_callGraphDocument, document))
                {
                    _callGraphDocument = null;
                }
            };

            _callGraphDocument = document;
            DocumentPane.Children.Add(document);
        }

        _callGraphDocument.IsActive = true;
        ActivateDocument("call-graph");
    }

    private void RemoveCallGraphDocument()
    {
        if (_callGraphDocument is null)
        {
            return;
        }

        var document = _callGraphDocument;
        _callGraphDocument = null;
        if (DocumentPane.Children.Contains(document))
        {
            DocumentPane.Children.Remove(document);
        }
    }

    private void CreateHexDocument()
    {
        RemoveHexDocument();

        if (_viewModel.HexEditor is null)
        {
            return;
        }

        var hexDocument = new LayoutDocument
        {
            Title = $"{_viewModel.Image?.FileName ?? "Binary"} — Hex",
            ContentId = "hex",
            CanClose = true,
            Content = new HexEditorView { DataContext = _viewModel.HexEditor },
            ToolTip = "Hexadecimal view of the raw file bytes",
        };
        hexDocument.Closed += (_, _) =>
        {
            if (ReferenceEquals(_hexDocument, hexDocument))
            {
                _hexDocument = null;
            }
        };

        _hexDocument = hexDocument;
        DocumentPane.Children.Add(hexDocument);
    }

    private void ShowHexDocument()
    {
        if (_viewModel.HexEditor is null)
        {
            return;
        }

        if (_hexDocument is null)
        {
            CreateHexDocument();
        }

        if (_hexDocument is not null)
        {
            _hexDocument.IsActive = true;
            ActivateDocument("hex");
        }
    }

    private void RemoveHexDocument()
    {
        if (_hexDocument is null)
        {
            return;
        }

        var document = _hexDocument;
        _hexDocument = null;
        if (DocumentPane.Children.Contains(document))
        {
            DocumentPane.Children.Remove(document);
        }
    }

    public void ActivateDocument(string contentId)
    {
        var document = DocumentPane.Children
            .OfType<LayoutDocument>()
            .FirstOrDefault(item => string.Equals(item.ContentId, contentId, StringComparison.Ordinal));

        if (document is not null)
        {
            document.IsActive = true;
        }
    }

    private void OnBinaryClosed()
    {
        RemoveOverviewDocument();
        RemoveDisassemblyDocument();
        RemoveGraphDocument();
        RemoveAnalysisBrowserDocument();
        RemoveCallGraphDocument();
        RemoveHexDocument();
        CreateStartDocument();
        _startDocument?.IsActive = true;
    }

    private void OnOverviewDocumentClosed(LayoutDocument document)
    {
        if (_closingDocuments || !ReferenceEquals(_overviewDocument, document))
        {
            return;
        }

        _overviewDocument = null;
        _viewModel.CloseBinaryCommand.Execute(null);
    }

    private void RemoveOverviewDocument()
    {
        if (_overviewDocument is null)
        {
            return;
        }

        var document = _overviewDocument;
        _overviewDocument = null;
        _closingDocuments = true;
        try
        {
            if (DocumentPane.Children.Contains(document))
            {
                DocumentPane.Children.Remove(document);
            }
        }
        finally
        {
            _closingDocuments = false;
        }
    }

    private void CloseActiveDocument()
    {
        if (DocumentPane.SelectedContent is not LayoutDocument document)
        {
            return;
        }

        if (ReferenceEquals(document, _overviewDocument))
        {
            _viewModel.CloseBinaryCommand.Execute(null);
            return;
        }

        if (ReferenceEquals(document, _startDocument))
        {
            return;
        }

        DocumentPane.Children.Remove(document);
    }

    private void TogglePanel(string contentId)
    {
        var anchorable = DockManager.Layout
            .Descendents()
            .OfType<LayoutAnchorable>()
            .FirstOrDefault(item => string.Equals(item.ContentId, contentId, StringComparison.Ordinal));

        if (anchorable is null)
        {
            Log.Warning("Panel {ContentId} was not found in the docking layout.", contentId);
            return;
        }

        if (anchorable.IsVisible)
        {
            anchorable.Hide();
        }
        else
        {
            anchorable.Show();
            anchorable.IsActive = true;
        }
    }

    private void ApplyDockTheme()
    {
        DockManager.Theme = CreateDockTheme(_themeService.Current);
    }

    private static AvalonDock.Themes.Theme CreateDockTheme(AppTheme theme)
    {
        AvalonDock.Themes.Theme dockTheme = theme == AppTheme.Dark
            ? new VS2022DarkTheme()
            : new VS2022LightTheme();

        // AvalonDock's VS theme paints the title-bar grip as a tiled dot
        // pattern. At 100% DPI that reads as noise across the whole caption,
        // so keep the grip color-only by replacing those brushes with
        // transparent ones before the theme templates are realised.
        if (dockTheme is AvalonDock.Themes.DictionaryTheme dictionaryTheme
            && dictionaryTheme.ThemeResourceDictionary is { } resources)
        {
            var transparent = System.Windows.Media.Brushes.Transparent;
            foreach (var key in resources.Keys.Cast<object>()
                         .Where(key => key.ToString()?.Contains("Grip", StringComparison.OrdinalIgnoreCase) == true)
                         .ToArray())
            {
                resources[key] = transparent;
            }
        }

        return dockTheme;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyDockTheme();
        }
        else
        {
            Dispatcher.BeginInvoke(ApplyDockTheme);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsPaletteOpen))
        {
            return;
        }

        if (!_viewModel.IsPaletteOpen)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                PaletteInput.Focus();
                Keyboard.Focus(PaletteInput);
                PaletteInput.SelectAll();
            },
            DispatcherPriority.Input);
    }

    private void PaletteInput_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var commands = _viewModel.FilteredPaletteCommands;

        switch (e.Key)
        {
            case Key.Escape:
                _viewModel.CloseCommandPaletteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
            case Key.Up:
                if (commands.Count == 0)
                {
                    return;
                }

                var current = _viewModel.SelectedPaletteCommand is { } selected
                    ? commands.IndexOf(selected)
                    : 0;
                var next = e.Key == Key.Down
                    ? Math.Min(commands.Count - 1, current + 1)
                    : Math.Max(0, current - 1);
                _viewModel.SelectedPaletteCommand = commands[next];
                PaletteList.ScrollIntoView(commands[next]);
                e.Handled = true;
                break;

            case Key.Enter:
                _viewModel.ExecutePaletteCommandCommand.Execute(_viewModel.SelectedPaletteCommand);
                e.Handled = true;
                break;
        }
    }

    private void PaletteList_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(PaletteList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container?.DataContext is PaletteCommand command)
        {
            _viewModel.SelectedPaletteCommand = command;
            _viewModel.ExecutePaletteCommandCommand.Execute(command);
        }
    }

    private void PaletteOverlay_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender))
        {
            _viewModel.CloseCommandPaletteCommand.Execute(null);
        }
    }

    private void Minimize_OnClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void MaximizeRestore_OnClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void Window_OnStateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeRestoreIcon();
    }

    private void UpdateMaximizeRestoreIcon()
    {
        MaxRestoreIcon.Kind = WindowState == WindowState.Maximized
            ? MahApps.Metro.IconPacks.PackIconCodiconsKind.ChromeRestore
            : MahApps.Metro.IconPacks.PackIconCodiconsKind.ChromeMaximize;
    }

    private void Window_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            await _viewModel.OpenFileAsync(files[0]);
        }
    }

    private async void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && !IsCaptureMode && _viewModel.IsProjectDirty)
        {
            e.Cancel = true;
            var decision = _dialogs.ShowSaveChanges(
                "Unsaved project changes",
                $"Project '{_viewModel.ProjectDisplayName}' has unsaved annotations. Save before closing?");

            if (decision == SaveChangesDecision.Cancel)
            {
                return;
            }

            if (decision == SaveChangesDecision.Save)
            {
                await _viewModel.SaveProjectCommand.ExecuteAsync(null);
                if (_viewModel.IsProjectDirty)
                {
                    return;
                }
            }

            _allowClose = true;
            Close();
            return;
        }

        DetachEvents();

        if (!IsCaptureMode && _settingsService.Current.RememberWindowSize)
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;

            if (bounds.Width >= MinWidth && bounds.Height >= MinHeight)
            {
                _settingsService.UpdateWindowBounds(
                    bounds.Width,
                    bounds.Height,
                    bounds.Left,
                    bounds.Top);
            }
        }

        Log.Information("Main window closing.");
    }

    private void DetachEvents()
    {
        if (_eventsDetached)
        {
            return;
        }

        _eventsDetached = true;
        _viewModel.BinaryLoaded -= OnBinaryLoaded;
        _viewModel.BinaryClosed -= OnBinaryClosed;
        _viewModel.OpenDisassemblyRequested -= ShowDisassemblyDocument;
        _viewModel.OpenAnalysisBrowserRequested -= ShowAnalysisBrowserDocument;
        _viewModel.OpenCallGraphRequested -= ShowCallGraphDocument;
        _viewModel.OpenGraphRequested -= ShowGraphDocument;
        _viewModel.OpenHexRequested -= ShowHexDocument;
        _viewModel.PanelToggleRequested -= TogglePanel;
        _viewModel.CloseActiveDocumentRequested -= CloseActiveDocument;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _themeService.ThemeChanged -= OnThemeChanged;
    }
}

