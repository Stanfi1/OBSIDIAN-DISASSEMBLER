using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Obsidian.Core.Model;
using Obsidian.Disassembly;

namespace Obsidian.App.Views;

public partial class PatchInstructionWindow : Window
{
    private readonly DisassembledInstruction _instruction;
    private readonly BinaryImage _image;

    public PatchInstructionWindow(DisassembledInstruction instruction, BinaryImage image)
    {
        InitializeComponent();
        _instruction = instruction;
        _image = image;

        AddressText.Text = $"{Core.Formatting.Hex.Rva(instruction.Rva)}  ·  {Core.Formatting.Hex.Va(instruction.VirtualAddress)}  ·  {instruction.SectionName}";
        OriginalText.Text = "Original:  " + FormatBytes(instruction.Bytes);
        BytesBox.Text = FormatBytes(instruction.Bytes);
        BytesBox.SelectAll();
        Validate();
    }

    public byte[]? PatchedBytes { get; private set; }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BytesBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Validate();

    private void Validate()
    {
        if (ApplyButton is null || PreviewText is null || ErrorText is null)
        {
            return;
        }

        if (!TryParseHex(BytesBox.Text, out var bytes, out var error))
        {
            ApplyButton.IsEnabled = false;
            PreviewText.Text = "—";
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (bytes.Length != _instruction.Bytes.Length)
        {
            ApplyButton.IsEnabled = false;
            PreviewText.Text = "—";
            ErrorText.Text = $"Length must be {_instruction.Bytes.Length} bytes (the original instruction length).";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        var preview = IcedPreview.Decode(bytes, _instruction.VirtualAddress, _image.Architecture == BinaryArchitecture.X64);
        PreviewText.Text = preview.IsValid ? preview.Text : "—";
        ErrorText.Text = preview.Error ?? string.Empty;
        ErrorText.Visibility = preview.IsValid ? Visibility.Collapsed : Visibility.Visible;
        ApplyButton.IsEnabled = preview.IsValid && !bytes.AsSpan().SequenceEqual(_instruction.Bytes);
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ApplyButton.IsEnabled || !TryParseHex(BytesBox.Text, out var bytes, out _))
        {
            return;
        }

        PatchedBytes = bytes;
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static bool TryParseHex(string text, out byte[] bytes, out string error)
    {
        bytes = [];
        error = string.Empty;
        var compact = text
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (compact.Length == 0)
        {
            error = "Enter the replacement bytes.";
            return false;
        }

        if (compact.Length % 2 != 0)
        {
            error = "Hex bytes must contain an even number of digits.";
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(compact);
            return true;
        }
        catch (FormatException)
        {
            error = "Only hexadecimal digits are allowed.";
            return false;
        }
    }

    private static string FormatBytes(ReadOnlySpan<byte> bytes) =>
        string.Join(' ', bytes.ToArray().Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
}
