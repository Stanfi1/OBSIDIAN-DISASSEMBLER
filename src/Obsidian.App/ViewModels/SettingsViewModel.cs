using CommunityToolkit.Mvvm.ComponentModel;
using Obsidian.App.Services;

namespace Obsidian.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(
        AppTheme currentTheme,
        bool rememberWindowSize,
        string version,
        string dataDirectory)
    {
        _isDarkTheme = currentTheme == AppTheme.Dark;
        _isLightTheme = currentTheme == AppTheme.Light;
        _rememberWindowSize = rememberWindowSize;
        Version = version;
        DataDirectory = dataDirectory;
    }

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private bool _isLightTheme;

    [ObservableProperty]
    private bool _rememberWindowSize;

    public string Version { get; }

    public string DataDirectory { get; }

    public AppTheme SelectedTheme => IsDarkTheme ? AppTheme.Dark : AppTheme.Light;

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (value)
        {
            IsLightTheme = false;
        }
    }

    partial void OnIsLightThemeChanged(bool value)
    {
        if (value)
        {
            IsDarkTheme = false;
        }
    }
}
