using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MahApps.Metro.IconPacks;
using Obsidian.App.ViewModels;
using Obsidian.Core.Formatting;

namespace Obsidian.App.Converters;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;
}

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Invert)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class StringToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasText = !string.IsNullOrWhiteSpace(value as string);
        if (Invert)
        {
            hasText = !hasText;
        }

        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is not null;
        if (Invert)
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class HexRvaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is uint rva ? Hex.Rva(rva) : "—";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class FindingIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Core.Model.PeFindingSeverity.Warning
            ? PackIconCodiconsKind.BracketError
            : PackIconCodiconsKind.Info;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps the tool-neutral node icon enum to the Codicons glyph set.</summary>
public sealed class ExplorerIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ExplorerIcon icon
            ? icon switch
            {
                ExplorerIcon.Binary => PackIconCodiconsKind.FileBinary,
                ExplorerIcon.Folder => PackIconCodiconsKind.RootFolder,
                ExplorerIcon.FolderOpen => PackIconCodiconsKind.RootFolderOpened,
                ExplorerIcon.Header => PackIconCodiconsKind.Files,
                ExplorerIcon.Section => PackIconCodiconsKind.FileCode,
                ExplorerIcon.Code => PackIconCodiconsKind.FileCode,
                ExplorerIcon.Data => PackIconCodiconsKind.Database,
                ExplorerIcon.Module => PackIconCodiconsKind.Package,
                ExplorerIcon.Function => PackIconCodiconsKind.SymbolMethod,
                ExplorerIcon.Export => PackIconCodiconsKind.Export,
                ExplorerIcon.String => PackIconCodiconsKind.SymbolString,
                ExplorerIcon.Resource => PackIconCodiconsKind.FileMedia,
                ExplorerIcon.Symbol => PackIconCodiconsKind.SymbolClass,
                ExplorerIcon.CrossReference => PackIconCodiconsKind.References,
                ExplorerIcon.Property => PackIconCodiconsKind.SymbolProperty,
                ExplorerIcon.Info => PackIconCodiconsKind.Info,
                ExplorerIcon.Warning => PackIconCodiconsKind.BracketError,
                ExplorerIcon.Analysis => PackIconCodiconsKind.Beaker,
                _ => PackIconCodiconsKind.FileBinary,
            }
            : PackIconCodiconsKind.FileBinary;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
