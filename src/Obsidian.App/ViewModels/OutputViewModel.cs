using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Obsidian.App.Services;
using Serilog;

namespace Obsidian.App.ViewModels;

public sealed partial class OutputViewModel : ObservableObject
{
    private readonly UiLogSink _sink;
    private readonly ILogger _logger;

    public OutputViewModel(UiLogSink sink, ILogger logger)
    {
        _sink = sink;
        _logger = logger.ForContext<OutputViewModel>();
        Entries.CollectionChanged += (_, _) => HasEntries = Entries.Count > 0;
        HasEntries = Entries.Count > 0;
    }

    public ObservableCollection<UiLogEntry> Entries => _sink.Entries;

    [ObservableProperty]
    private bool _hasEntries;

    public string LogDirectory => ShellPaths.LogDirectory;

    [RelayCommand]
    private void Clear()
    {
        _sink.Clear();
        _logger.Debug("Output window cleared by the user.");
    }

    [RelayCommand]
    private void CopyAll()
    {
        try
        {
            Clipboard.SetText(_sink.GetText());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not copy the output log to the clipboard.");
        }
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
        }
    }
}
