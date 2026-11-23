using System.Windows;
using Serilog;

namespace Obsidian.App.Services;

/// <summary>
/// Swaps the semantic palette dictionary at runtime. Every control style uses
/// DynamicResource, so a theme change does not rebuild the visual tree.
/// </summary>
public sealed class ThemeService
{
    private readonly ILogger _logger;
    private bool _initialized;

    public ThemeService(ILogger logger)
    {
        _logger = logger.ForContext<ThemeService>();
    }

    public AppTheme Current { get; private set; } = AppTheme.Dark;

    public event EventHandler? ThemeChanged;

    public void Apply(AppTheme theme, bool force = false)
    {
        if (!force && _initialized && Current == theme)
        {
            return;
        }

        var application = Application.Current
            ?? throw new InvalidOperationException("Themes can only be applied after the WPF application exists.");

        var source = new Uri(
            $"pack://application:,,,/Obsidian;component/Themes/Palette.{theme}.xaml",
            UriKind.Absolute);
        var dictionary = new ResourceDictionary { Source = source };

        var dictionaries = application.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(
            d => d.Source?.OriginalString.Contains("Themes/Palette.", StringComparison.OrdinalIgnoreCase) == true);

        if (existing is not null)
        {
            dictionaries.Remove(existing);
        }

        dictionaries.Insert(0, dictionary);
        Current = theme;
        _initialized = true;

        _logger.Information("Theme applied: {Theme}", theme);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public AppTheme Toggle()
    {
        var next = Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        Apply(next);
        return next;
    }
}
