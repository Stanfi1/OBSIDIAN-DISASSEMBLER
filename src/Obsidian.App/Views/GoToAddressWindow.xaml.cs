using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Obsidian.Core.Formatting;
using Obsidian.Core.Model;

namespace Obsidian.App.Views;

public partial class GoToAddressWindow : Window
{
    private readonly AddressMap _addressMap;
    private AddressResolution? _resolution;

    public GoToAddressWindow(AddressMap addressMap)
    {
        _addressMap = addressMap;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            AddressInput.Focus();
            AddressInput.SelectAll();
            Validate();
        };
    }

    public AddressResolution? Result => _resolution;

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Input_OnChanged(object sender, RoutedEventArgs e) => Validate();

    private void Validate()
    {
        if (AddressInput is null
            || RvaOption is null
            || VaOption is null
            || OffsetOption is null
            || GoButton is null
            || ErrorText is null
            || PreviewText is null
            || SectionText is null)
        {
            return;
        }

        _resolution = null;
        GoButton.IsEnabled = false;
        ErrorText.Text = string.Empty;

        var text = AddressInput.Text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        if (text.Length == 0)
        {
            PreviewText.Text = "Enter an address to resolve it.";
            SectionText.Text = string.Empty;
            return;
        }

        if (!ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            PreviewText.Text = "—";
            SectionText.Text = string.Empty;
            ErrorText.Text = "Use hexadecimal digits only. The 0x prefix is optional.";
            return;
        }

        var space = RvaOption.IsChecked == true
            ? AddressSpace.Rva
            : VaOption.IsChecked == true
                ? AddressSpace.VirtualAddress
                : AddressSpace.FileOffset;

        if (!_addressMap.TryResolve(space, value, out var resolution))
        {
            PreviewText.Text = "0x" + value.ToString("X", CultureInfo.InvariantCulture) + " does not map to the image.";
            SectionText.Text = space switch
            {
                AddressSpace.Rva => "No section has file-backed data at that RVA.",
                AddressSpace.VirtualAddress => "The virtual address is outside the image or has no file-backed data.",
                _ => "The file offset is outside the image.",
            };
            return;
        }

        _resolution = resolution;
        GoButton.IsEnabled = true;

        PreviewText.Text =
            $"RVA {Hex.Rva(resolution.Rva)}   VA {Hex.Va(resolution.VirtualAddress)}   offset {Hex.Offset(resolution.FileOffset)}";
        SectionText.Text = resolution.Section is null
            ? "Header region · not inside a section"
            : $"{resolution.Section.Name} · {resolution.Section.Kind} · {resolution.Section.Permissions}";
    }

    private void Go_OnClick(object sender, RoutedEventArgs e)
    {
        if (_resolution is null)
        {
            return;
        }

        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
