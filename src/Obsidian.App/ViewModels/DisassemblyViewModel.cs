using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Obsidian.Core.Formatting;
using Obsidian.Core.Model;
using Obsidian.Core.Services;
using Serilog;

namespace Obsidian.App.ViewModels;

public sealed class InstructionRowViewModel
{
    public InstructionRowViewModel(DisassembledInstruction instruction, BinaryImage image)
    {
        Instruction = instruction;
        AddressText = Hex.Rva(instruction.Rva);
        VirtualAddressText = Hex.Va(instruction.VirtualAddress);
        FileOffsetText = instruction.FileOffset is { } offset ? Hex.Offset(offset) : "—";
        BytesText = string.Join(' ', instruction.Bytes.Select(value => value.ToString("X2")));
        Mnemonic = instruction.Mnemonic;
        Operands = instruction.Operands;
        FlowText = instruction.Flow switch
        {
            InstructionFlow.Call => "Call",
            InstructionFlow.IndirectCall => "Indirect call",
            InstructionFlow.UnconditionalBranch => "Jump",
            InstructionFlow.ConditionalBranch => "Conditional jump",
            InstructionFlow.IndirectBranch => "Indirect jump",
            InstructionFlow.Return => "Return",
            InstructionFlow.Interrupt => "Interrupt",
            InstructionFlow.Invalid => "Invalid",
            _ => "Next",
        };
        TargetText = instruction.TargetVirtualAddress is { } target
            ? instruction.TargetRva is { } targetRva
                ? $"{Hex.Rva(targetRva)} ({Hex.Va(target)})"
                : Hex.Va(target)
            : null;
        IsInvalid = !instruction.IsValid;
        HasStaticTarget = instruction.HasStaticTarget;
        ToolTip =
            $"{AddressText}  {BytesText}\n" +
            $"VA {VirtualAddressText}  ·  offset {FileOffsetText}  ·  {instruction.SectionName}\n" +
            $"Flow: {FlowText}" +
            (TargetText is null ? string.Empty : $"  ·  target {TargetText}");
    }

    public DisassembledInstruction Instruction { get; }

    public int Index => Instruction.Index;

    public string AddressText { get; }

    public string VirtualAddressText { get; }

    public string FileOffsetText { get; }

    public string BytesText { get; }

    public string Mnemonic { get; }

    public string Operands { get; }

    public string FlowText { get; }

    public string? TargetText { get; }

    public bool IsInvalid { get; }

    public bool HasStaticTarget { get; }

    public string ToolTip { get; }
}

/// <summary>
/// Lazily materialized instruction rows for the WPF virtualizing list. The
/// underlying instruction list is immutable after disassembly, so row identity
/// is stable for the lifetime of the document.
/// </summary>
public sealed class InstructionRowCollection :
    IList,
    IReadOnlyList<InstructionRowViewModel>
{
    private readonly DisassemblyViewModel _owner;
    private readonly IReadOnlyList<DisassembledInstruction> _instructions;
    private readonly Dictionary<int, InstructionRowViewModel> _cache = [];

    public InstructionRowCollection(
        DisassemblyViewModel owner,
        IReadOnlyList<DisassembledInstruction> instructions)
    {
        _owner = owner;
        _instructions = instructions;
    }

    public int Count => _instructions.Count;

    public bool IsReadOnly => true;

    public bool IsFixedSize => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public InstructionRowViewModel this[int index] => GetRow(index);

    object? IList.this[int index]
    {
        get => GetRow(index);
        set => throw new NotSupportedException();
    }

    public InstructionRowViewModel GetRow(int index)
    {
        if (index < 0 || index >= _instructions.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (_cache.TryGetValue(index, out var cached))
        {
            return cached;
        }

        var row = new InstructionRowViewModel(_instructions[index], _owner.Image);
        _cache[index] = row;
        return row;
    }

    public IEnumerator<InstructionRowViewModel> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return GetRow(index);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Contains(object? value) => IndexOf(value) >= 0;

    public int IndexOf(object? value) =>
        value is InstructionRowViewModel row ? row.Index : -1;

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
}

public sealed partial class DisassemblyViewModel : ObservableObject
{
    private readonly IDisassembler _disassembler;
    private readonly ILogger _logger;
    private CancellationTokenSource? _cts;
    private Task? _loadTask;
    private uint? _pendingRva;

    public DisassemblyViewModel(BinaryImage image, IDisassembler disassembler, ILogger logger)
    {
        Image = image;
        _disassembler = disassembler;
        _logger = logger;
        IsSupported = disassembler.IsSupported(image.Architecture);
        Rows = new InstructionRowCollection(this, []);
        StatusText = IsSupported ? "Disassembly is not loaded." : "Unsupported architecture.";
    }

    public BinaryImage Image { get; }

    [ObservableProperty]
    private InstructionRowCollection _rows;

    [ObservableProperty]
    private DisassemblyImage? _result;

    [ObservableProperty]
    private bool _isSupported;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private InstructionRowViewModel? _selectedInstruction;

    [ObservableProperty]
    private int _selectedIndex = -1;

    public event Action<int>? ScrollToIndexRequested;

    public event Action<DisassembledInstruction?>? InstructionSelected;

    public bool HasResult => Result is { HasInstructions: true };

    public bool CanFollowTarget => SelectedInstruction?.HasStaticTarget == true;

    public string InstructionCountText => Result is { HasInstructions: true } result
        ? $"{result.Count:N0} instructions · {result.Sections.Count} section{(result.Sections.Count == 1 ? string.Empty : "s")}"
        : "No instructions";

    public string SectionSummary => Result is { Sections.Count: > 0 } result
        ? string.Join("  ·  ", result.Sections.Select(section => $"{section.Name}: {section.InstructionCount:N0}"))
        : string.Empty;

    public string FileInfoText => $"{Image.FileName} · {Image.ArchitectureText} · {Image.FormatText}";

    public string ModeText => "Linear sweep · read-only";

    public Task LoadAsync() => _loadTask ??= LoadCoreAsync();

    public Task WaitForCompletionAsync() => _loadTask ?? LoadAsync();

    public void Cancel() => _cts?.Cancel();

    public void NavigateToEntryPoint() => NavigateToRva(Image.EntryPointRva);

    public bool NavigateToRva(uint rva)
    {
        if (Result is null || Rows.Count == 0)
        {
            _pendingRva = rva;
            StatusText = $"RVA {Hex.Rva(rva)} will be selected when disassembly completes.";
            return false;
        }

        var exact = Result.FindInstructionIndex(rva);
        var index = exact >= 0 ? exact : Result.FindNearestIndex(rva);
        if (index < 0)
        {
            StatusText = $"No decoded instruction near {Hex.Rva(rva)}.";
            return false;
        }

        var row = Rows[index];
        SelectedIndex = index;
        SelectedInstruction = row;
        ScrollToIndexRequested?.Invoke(index);
        StatusText = exact >= 0
            ? $"RVA {Hex.Rva(rva)} · instruction {index:N0}"
            : $"Nearest instruction: {row.AddressText} (requested {Hex.Rva(rva)}).";
        return true;
    }

    public void SelectRow(InstructionRowViewModel row)
    {
        if (SelectedIndex != row.Index)
        {
            SelectedIndex = row.Index;
        }

        if (!ReferenceEquals(SelectedInstruction, row))
        {
            SelectedInstruction = row;
        }
    }

    public void ReportCopied(int count) =>
        StatusText = $"Copied {count} instruction{(count == 1 ? string.Empty : "s")} to the clipboard.";

    public void ReportNoSelection() => StatusText = "Select at least one instruction to copy.";

    public void ReportClipboardError(string message) => StatusText = message;

    [RelayCommand]
    private void GoToAddress()
    {
        var text = GoToText.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        if (!ulong.TryParse(
                text,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
        {
            StatusText = "Enter a hexadecimal RVA or VA.";
            return;
        }

        uint rva;
        if (value >= Image.ImageBase && value - Image.ImageBase < Image.SizeOfImage)
        {
            rva = (uint)(value - Image.ImageBase);
        }
        else if (value < Image.SizeOfImage)
        {
            rva = (uint)value;
        }
        else
        {
            StatusText = "The address is outside the image.";
            return;
        }

        NavigateToRva(rva);
    }

    [RelayCommand]
    private void FollowTarget()
    {
        if (SelectedInstruction?.Instruction is not { } instruction)
        {
            StatusText = "Select an instruction first.";
            return;
        }

        if (instruction.TargetRva is { } targetRva)
        {
            if (NavigateToRva(targetRva))
            {
                StatusText = $"Followed {SelectedInstruction?.FlowText ?? "branch"} to {Hex.Rva(targetRva)}.";
            }

            return;
        }

        StatusText = instruction.TargetVirtualAddress is { } target
            ? $"Target {Hex.Va(target)} is not decoded executable code."
            : "The selected instruction has no static target.";
    }

    [RelayCommand]
    private void GoToEntryPoint() => NavigateToEntryPoint();

    [RelayCommand]
    private void CancelLoad() => Cancel();

    [ObservableProperty]
    private string _goToText = string.Empty;

    partial void OnSelectedInstructionChanged(InstructionRowViewModel? value)
    {
        OnPropertyChanged(nameof(CanFollowTarget));
        InstructionSelected?.Invoke(value?.Instruction);
    }

    private async Task LoadCoreAsync()
    {
        if (!IsSupported)
        {
            Message = "Iced disassembly supports x86 and x64 only. This binary cannot be decoded.";
            StatusText = Message;
            ProgressPercent = 100;
            return;
        }

        IsBusy = true;
        ProgressPercent = 0;
        StatusText = "Disassembling executable sections…";
        Message = null;
        _cts = new CancellationTokenSource();

        var progress = new Progress<LoadProgress>(update =>
        {
            if (update.Percent is { } percent)
            {
                ProgressPercent = percent;
            }

            StatusText = update.Message;
        });

        try
        {
            var result = await _disassembler.DisassembleAsync(Image, progress, _cts.Token);
            Result = result;
            SelectedInstruction = null;
            SelectedIndex = -1;
            Rows = new InstructionRowCollection(this, result.Instructions);
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(InstructionCountText));
            OnPropertyChanged(nameof(SectionSummary));
            Message = result.Message;
            StatusText = result.Message ?? $"Decoded {result.Count:N0} instructions.";
            ProgressPercent = 100;

            if (result.HasInstructions)
            {
                if (result.IsTruncated)
                {
                    _logger.Warning(
                        "Disassembly truncated at {InstructionCount} instructions in {SectionCount} sections: {Message}",
                        result.Count,
                        result.Sections.Count,
                        result.Message);
                }
                else
                {
                    _logger.Information(
                        "Disassembly complete: {InstructionCount} instructions in {SectionCount} sections",
                        result.Count,
                        result.Sections.Count);
                }

                if (_pendingRva is { } pendingRva)
                {
                    _pendingRva = null;
                    NavigateToRva(pendingRva);
                }
            }
            else
            {
                _logger.Warning("Disassembly produced no instructions: {Message}", result.Message);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Disassembly cancelled.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Disassembly failed");
            Message = "Disassembly failed: " + ex.Message;
            StatusText = Message;
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = 100;
            _cts?.Dispose();
            _cts = null;
        }
    }
}
