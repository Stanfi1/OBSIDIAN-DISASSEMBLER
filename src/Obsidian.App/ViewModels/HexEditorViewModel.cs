using System.Collections;
using System.Collections.Specialized;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Obsidian.Core.Formatting;
using Obsidian.Core.Model;
using Obsidian.Core.Searching;

namespace Obsidian.App.ViewModels;

public enum HexAddressMode
{
    FileOffset = 0,
    Rva,
    VirtualAddress,
}

public enum HexSearchMode
{
    Hex = 0,
    Ascii,
}

public sealed class HexRowViewModel
{
    public HexRowViewModel(long offset, ReadOnlySpan<byte> bytes, string addressText)
    {
        Offset = offset;
        ByteCount = bytes.Length;
        AddressText = addressText;

        var hex = new StringBuilder(48);
        var ascii = new StringBuilder(16);
        for (var index = 0; index < 16; index++)
        {
            if (index == 8)
            {
                hex.Append(' ');
            }

            if (index < bytes.Length)
            {
                var value = bytes[index];
                hex.Append(value.ToString("X2"));
                ascii.Append(value is >= 32 and < 127 ? (char)value : '.');
            }
            else
            {
                hex.Append("  ");
                ascii.Append(' ');
            }

            if (index != 15)
            {
                hex.Append(' ');
            }
        }

        HexText = hex.ToString();
        AsciiText = ascii.ToString();
    }

    public long Offset { get; }

    public int ByteCount { get; }

    public string AddressText { get; }

    public string HexText { get; }

    public string AsciiText { get; }
}

/// <summary>
/// Lazily constructed, virtualizable row list. WPF asks an IList for visible
/// indices; rows are cached so item identity stays stable while scrolling.
/// </summary>
public sealed class HexRowCollection :
    IList,
    IReadOnlyList<HexRowViewModel>,
    INotifyCollectionChanged
{
    private readonly HexEditorViewModel _owner;
    private readonly Dictionary<int, HexRowViewModel> _cache = [];

    public HexRowCollection(HexEditorViewModel owner)
    {
        _owner = owner;
        Count = (int)((owner.Image.Bytes.Length + 15L) / 16L);
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int Count { get; }

    public bool IsReadOnly => true;

    public bool IsFixedSize => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public HexRowViewModel this[int index] => GetRow(index);

    object? IList.this[int index]
    {
        get => GetRow(index);
        set => throw new NotSupportedException();
    }

    public void Refresh()
    {
        _cache.Clear();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public HexRowViewModel GetRow(int index)
    {
        if (index < 0 || index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (_cache.TryGetValue(index, out var cached))
        {
            return cached;
        }

        var offset = (long)index * 16;
        var remaining = _owner.Image.Bytes.Length - offset;
        var length = (int)Math.Clamp(remaining, 0, 16);
        var bytes = _owner.Image.Bytes.Span.Slice((int)offset, length);

        var row = new HexRowViewModel(offset, bytes, FormatAddress(offset));
        _cache[index] = row;
        return row;
    }

    public IEnumerator<HexRowViewModel> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return GetRow(index);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Contains(object? value) => IndexOf(value) >= 0;

    public int IndexOf(object? value) =>
        value is HexRowViewModel row ? (int)(row.Offset / 16) : -1;

    public void CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (var i = 0; i < Count; i++)
        {
            array.SetValue(GetRow(i), index + i);
        }
    }

    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    private string FormatAddress(long offset) => _owner.AddressMode switch
    {
        HexAddressMode.Rva => _owner.Image.AddressMap.TryFileOffsetToRva(offset, out var rva)
            ? Hex.Rva(rva)
            : "--------",
        HexAddressMode.VirtualAddress => _owner.Image.AddressMap.TryFileOffsetToVa(offset, out var va)
            ? Hex.Va(va)
            : "----------------",
        _ => Hex.Offset(offset),
    };
}

public sealed partial class HexEditorViewModel : ObservableObject
{
    private CancellationTokenSource? _searchCts;

    public HexEditorViewModel(BinaryImage image)
    {
        Image = image;
        Rows = new HexRowCollection(this);
        StatusText = "Ready";
        SelectionText = "No selection";
    }

    public BinaryImage Image { get; }

    public HexRowCollection Rows { get; }

    public string FileInfoText =>
        $"{Image.FileName} · {Hex.Size(Image.FileSize)} · {Rows.Count:N0} rows";

    public string AddressHeader => AddressMode switch
    {
        HexAddressMode.Rva => "RVA",
        HexAddressMode.VirtualAddress => "Virtual address",
        _ => "Offset",
    };

    public event Action<int>? ScrollToRowRequested;

    [ObservableProperty]
    private HexAddressMode _addressMode = HexAddressMode.FileOffset;

    [ObservableProperty]
    private HexSearchMode _searchMode = HexSearchMode.Hex;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _goToText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private string _selectionText = string.Empty;

    [ObservableProperty]
    private long _selectedOffset;

    public bool IsFileOffsetMode
    {
        get => AddressMode == HexAddressMode.FileOffset;
        set
        {
            if (value)
            {
                AddressMode = HexAddressMode.FileOffset;
            }
        }
    }

    public bool IsRvaMode
    {
        get => AddressMode == HexAddressMode.Rva;
        set
        {
            if (value)
            {
                AddressMode = HexAddressMode.Rva;
            }
        }
    }

    public bool IsVirtualAddressMode
    {
        get => AddressMode == HexAddressMode.VirtualAddress;
        set
        {
            if (value)
            {
                AddressMode = HexAddressMode.VirtualAddress;
            }
        }
    }

    public bool IsHexSearch
    {
        get => SearchMode == HexSearchMode.Hex;
        set
        {
            if (value)
            {
                SearchMode = HexSearchMode.Hex;
            }
        }
    }

    public bool IsAsciiSearch
    {
        get => SearchMode == HexSearchMode.Ascii;
        set
        {
            if (value)
            {
                SearchMode = HexSearchMode.Ascii;
            }
        }
    }

    partial void OnAddressModeChanged(HexAddressMode value)
    {
        OnPropertyChanged(nameof(IsFileOffsetMode));
        OnPropertyChanged(nameof(IsRvaMode));
        OnPropertyChanged(nameof(IsVirtualAddressMode));
        OnPropertyChanged(nameof(AddressHeader));
        Rows.Refresh();
        ScrollToRowRequested?.Invoke((int)Math.Clamp(SelectedOffset / 16, 0, Math.Max(0, Rows.Count - 1)));
    }

    partial void OnSearchModeChanged(HexSearchMode value)
    {
        OnPropertyChanged(nameof(IsHexSearch));
        OnPropertyChanged(nameof(IsAsciiSearch));
    }

    [RelayCommand]
    private async Task FindNextAsync() => await FindAsync(forward: true);

    [RelayCommand]
    private async Task FindPreviousAsync() => await FindAsync(forward: false);

    [RelayCommand]
    private void GoToOffset()
    {
        var value = GoToText.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        if (!ulong.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var parsed)
            || parsed >= (ulong)Image.FileSize)
        {
            SetStatus("Enter a file offset in hexadecimal that is inside the file.", true);
            return;
        }

        NavigateToOffset((long)parsed);
        SetStatus($"Offset {Hex.Offset((long)parsed)}", false);
    }

    public void NavigateToOffset(long offset)
    {
        if (offset < 0 || offset >= Image.FileSize)
        {
            SetStatus("The offset is outside the file.", true);
            return;
        }

        SelectedOffset = offset;
        var row = (int)(offset / 16);
        ScrollToRowRequested?.Invoke(row);
    }

    public void NotifySelection(IReadOnlyList<HexRowViewModel> rows, bool preserveOffset = false)
    {
        if (rows.Count == 0)
        {
            SelectionText = "No selection";
            return;
        }

        var ordered = rows.OrderBy(row => row.Offset).ToArray();
        var start = ordered[0].Offset;
        var endExclusive = ordered[^1].Offset + ordered[^1].ByteCount;
        if (!preserveOffset)
        {
            SelectedOffset = start;
        }

        SelectionText = ordered.Length == 1
            ? $"{Hex.Offset(start)} · {ordered[0].ByteCount} bytes"
            : $"{Hex.Offset(start)}–{Hex.Offset(endExclusive - 1)} · {endExclusive - start:N0} bytes · {ordered.Length} rows";
    }

    public void ReportCopied(int rows)
    {
        SetStatus($"Copied {rows} row{(rows == 1 ? string.Empty : "s")} to the clipboard.", false);
    }

    public void ReportClipboardError(string message) => SetStatus(message, true);

    public void ReportNoSelection() => SetStatus("Select at least one row to copy.", true);

    private async Task FindAsync(bool forward)
    {
        byte[] pattern;
        try
        {
            pattern = BuildPattern();
        }
        catch (FormatException)
        {
            SetStatus("Use an even number of hexadecimal digits.", true);
            return;
        }

        if (pattern.Length == 0)
        {
            SetStatus("Enter a search pattern.", true);
            return;
        }

        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;

        SetStatus("Searching…", false);

        try
        {
            var startOffset = SelectedOffset;
            var bytes = Image.Bytes;
            var found = await Task.Run(
                () => forward
                    ? ByteSearch.FindForward(bytes.Span, pattern, startOffset, cts.Token)
                    : ByteSearch.FindBackward(bytes.Span, pattern, startOffset, cts.Token),
                cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (found < 0)
            {
                SetStatus("Pattern not found.", true);
                return;
            }

            NavigateToOffset(found);
            SetStatus($"Match at {Hex.Offset(found)}", false);
        }
        catch (OperationCanceledException)
        {
            // A newer search superseded this one.
        }
        catch (Exception ex)
        {
            SetStatus("Search failed: " + ex.Message, true);
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                _searchCts = null;
            }

            cts.Dispose();
        }
    }

    private byte[] BuildPattern()
    {
        if (SearchMode == HexSearchMode.Ascii)
        {
            return Encoding.UTF8.GetBytes(SearchText);
        }

        var cleaned = new string(SearchText.Where(character => !char.IsWhiteSpace(character)).ToArray());
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[2..];
        }

        if (cleaned.Length == 0)
        {
            return [];
        }

        if (cleaned.Length % 2 != 0)
        {
            throw new FormatException();
        }

        return Convert.FromHexString(cleaned);
    }

    private void SetStatus(string status, bool isError)
    {
        StatusText = status;
        IsStatusError = isError;
    }
}
