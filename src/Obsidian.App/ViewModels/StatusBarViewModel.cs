using CommunityToolkit.Mvvm.ComponentModel;

namespace Obsidian.App.ViewModels;

public sealed partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty]
    private string _message = "Ready";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _imageInfo = "No binary loaded";

    [ObservableProperty]
    private string _findingsInfo = string.Empty;

    [ObservableProperty]
    private string _themeName = "Dark";

    [ObservableProperty]
    private string _version = "0.1.0";
}
