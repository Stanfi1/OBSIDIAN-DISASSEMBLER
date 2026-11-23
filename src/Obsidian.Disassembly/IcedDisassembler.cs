using Iced.Intel;
using Obsidian.Core.Model;
using Obsidian.Core.Services;

namespace Obsidian.Disassembly;

/// <summary>
/// Iced-based linear-sweep disassembler for native x86/x64 PE images.
/// It deliberately does not claim function-aware analysis: it decodes each
/// executable section from its start, so data embedded in code can appear as
/// plausible instructions.
/// </summary>
public sealed class IcedDisassembler : IDisassembler
{
    public const int MaxInstructions = 1_000_000;

    public bool IsSupported(BinaryArchitecture architecture) => SupportsArchitecture(architecture);

    private static bool SupportsArchitecture(BinaryArchitecture architecture) =>
        architecture is BinaryArchitecture.X86 or BinaryArchitecture.X64;

    public Task<DisassemblyImage> DisassembleAsync(
        BinaryImage image,
        IProgress<LoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Task.Run(() => Decode(image, progress, cancellationToken), cancellationToken);
    }

    private static DisassemblyImage Decode(
        BinaryImage image,
        IProgress<LoadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!SupportsArchitecture(image.Architecture))
        {
            var unsupportedMessage = "Iced disassembly supports x86 and x64 only. ARM64 decoding is not implemented.";
            progress?.Report(new LoadProgress(LoadStage.Disassembling, unsupportedMessage, 100));
            return DisassemblyImage.Empty(image.Architecture, image.ImageBase, unsupportedMessage);
        }

        var bitness = image.Architecture == BinaryArchitecture.X64 ? 64 : 32;
        var jobs = new List<DecodeJob>();
        long totalBytes = 0;

        foreach (var section in image.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((!section.IsExecutable && !section.ContainsCode) || section.RawSize == 0)
            {
                continue;
            }

            var available = image.Bytes.Length - (long)section.RawOffset;
            if (available <= 0)
            {
                continue;
            }

            var length = (int)Math.Min((long)section.RawSize, available);
            if (section.VirtualSize > 0)
            {
                length = Math.Min(length, (int)Math.Min(section.VirtualSize, int.MaxValue));
            }

            if (length <= 0)
            {
                continue;
            }

            jobs.Add(new DecodeJob(section, (int)section.RawOffset, length, image.ImageBase + section.VirtualAddress));
            totalBytes += length;
        }

        if (jobs.Count == 0)
        {
            const string noCodeMessage = "No executable section with file-backed code was found.";
            progress?.Report(new LoadProgress(LoadStage.Disassembling, noCodeMessage, 100));
            return DisassemblyImage.Empty(image.Architecture, image.ImageBase, noCodeMessage);
        }

        progress?.Report(new LoadProgress(LoadStage.Disassembling, "Disassembling executable sections…", 0));

        var instructions = new List<DisassembledInstruction>((int)Math.Min(totalBytes, 250_000));
        var sections = new List<DisassemblySection>();
        var formatter = new IntelFormatter();
        formatter.Options.SpaceAfterOperandSeparator = true;
        formatter.Options.UppercaseHex = false;
        formatter.Options.HexPrefix = "0x";
        formatter.Options.HexSuffix = string.Empty;
        formatter.Options.ShowBranchSize = false;
        var output = new StringOutput();
        long processedBytes = 0;
        var truncated = false;
        var instructionIndex = 0;

        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var code = image.Bytes.Slice(job.FileOffset, job.Length);
            var decoder = Decoder.Create(bitness, new MemoryCodeReader(code));
            decoder.IP = job.StartVirtualAddress;

            var offset = 0;
            var sectionStart = instructions.Count;

            while (offset < job.Length && instructionIndex < MaxInstructions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var startVirtualAddress = job.StartVirtualAddress + (ulong)offset;
                var decoded = decoder.Decode();
                var remaining = job.Length - offset;
                var declaredLength = decoded.Length > 0 ? decoded.Length : 1;
                var actualLength = Math.Min(declaredLength, remaining);
                var complete = actualLength == declaredLength;
                var valid = complete && !decoded.IsInvalid;

                InstructionFlow flow;
                string mnemonic;
                string operands;
                ulong? targetVirtualAddress = null;
                ulong? dataTargetVirtualAddress = null;

                if (valid)
                {
                    formatter.Format(decoded, output);
                    var text = output.ToStringAndReset().Trim();
                    var separator = text.IndexOf(' ');
                    if (separator > 0)
                    {
                        mnemonic = text[..separator];
                        operands = text[(separator + 1)..].Trim();
                    }
                    else
                    {
                        mnemonic = text;
                        operands = string.Empty;
                    }

                    flow = MapFlow(decoded.FlowControl);

                    if ((flow is InstructionFlow.Call or InstructionFlow.UnconditionalBranch or InstructionFlow.ConditionalBranch)
                        && !decoded.IsJmpFar
                        && !decoded.IsCallFar)
                    {
                        targetVirtualAddress = decoded.NearBranchTarget;
                    }

                    if (decoded.IsIPRelativeMemoryOperand)
                    {
                        dataTargetVirtualAddress = decoded.IPRelativeMemoryAddress;
                    }
                }
                else
                {
                    var raw = code.Span.Slice(offset, actualLength);
                    mnemonic = "db";
                    operands = string.Join(' ', raw.ToArray().Select(value => $"0x{value:X2}"));
                    flow = InstructionFlow.Invalid;
                }

                var bytes = code.Slice(offset, actualLength).ToArray();
                var rva = (uint)(startVirtualAddress - image.ImageBase);
                uint? targetRva = null;

                if (targetVirtualAddress is { } target
                    && target >= image.ImageBase
                    && target - image.ImageBase < image.SizeOfImage)
                {
                    var candidateRva = (uint)(target - image.ImageBase);
                    var targetSection = image.AddressMap.GetSectionForRva(candidateRva);
                    if (targetSection is { IsExecutable: true } or { ContainsCode: true })
                    {
                        targetRva = candidateRva;
                    }
                }

                uint? dataTargetRva = null;
                if (dataTargetVirtualAddress is { } dataTarget
                    && dataTarget >= image.ImageBase
                    && dataTarget - image.ImageBase < image.SizeOfImage)
                {
                    dataTargetRva = (uint)(dataTarget - image.ImageBase);
                }

                var instruction = new DisassembledInstruction
                {
                    Index = instructionIndex,
                    Rva = rva,
                    VirtualAddress = startVirtualAddress,
                    FileOffset = image.AddressMap.TryRvaToFileOffset(rva, out var fileOffset) ? fileOffset : null,
                    Length = actualLength,
                    Bytes = bytes,
                    SectionName = job.Section.Name,
                    Mnemonic = mnemonic,
                    Operands = operands,
                    Flow = flow,
                    TargetRva = targetRva,
                    TargetVirtualAddress = targetVirtualAddress,
                    DataTargetRva = dataTargetRva,
                    DataTargetVirtualAddress = dataTargetVirtualAddress,
                    IsValid = valid,
                };

                instructions.Add(instruction);
                instructionIndex++;
                offset += actualLength;
                processedBytes += actualLength;

                if (decoder.IP <= startVirtualAddress)
                {
                    decoder.IP = startVirtualAddress + (ulong)actualLength;
                }

                if (instructionIndex % 4096 == 0)
                {
                    var percent = totalBytes <= 0 ? 100 : (int)Math.Clamp(processedBytes * 100 / totalBytes, 0, 100);
                    progress?.Report(new LoadProgress(
                        LoadStage.Disassembling,
                        $"Disassembling {job.Section.Name}… {instructionIndex:N0} instructions",
                        percent));
                }
            }

            var sectionCount = instructions.Count - sectionStart;
            if (sectionCount > 0)
            {
                sections.Add(new DisassemblySection(
                    job.Section.Name,
                    job.Section.VirtualAddress,
                    job.StartVirtualAddress,
                    sectionStart,
                    sectionCount,
                    (uint)job.Length));
            }

            if (instructionIndex >= MaxInstructions)
            {
                truncated = true;
                break;
            }

            var sectionPercent = totalBytes <= 0 ? 100 : (int)Math.Clamp(processedBytes * 100 / totalBytes, 0, 100);
            progress?.Report(new LoadProgress(
                LoadStage.Disassembling,
                $"Disassembled {job.Section.Name} ({sectionCount:N0} instructions)",
                sectionPercent));
        }

        var resultMessage = truncated
            ? $"Linear disassembly truncated at {MaxInstructions:N0} instructions."
            : null;

        progress?.Report(new LoadProgress(
            LoadStage.Disassembling,
            truncated ? resultMessage! : $"Disassembly complete: {instructions.Count:N0} instructions",
            100));

        return new DisassemblyImage
        {
            Instructions = instructions,
            Sections = sections,
            Architecture = image.Architecture,
            ImageBase = image.ImageBase,
            IsTruncated = truncated,
            Message = resultMessage,
        };
    }

    private static InstructionFlow MapFlow(FlowControl flow) => flow switch
    {
        FlowControl.Next => InstructionFlow.Next,
        FlowControl.Call => InstructionFlow.Call,
        FlowControl.IndirectCall => InstructionFlow.IndirectCall,
        FlowControl.UnconditionalBranch => InstructionFlow.UnconditionalBranch,
        FlowControl.ConditionalBranch => InstructionFlow.ConditionalBranch,
        FlowControl.IndirectBranch => InstructionFlow.IndirectBranch,
        FlowControl.Return => InstructionFlow.Return,
        FlowControl.Interrupt => InstructionFlow.Interrupt,
        _ => InstructionFlow.Other,
    };

    private sealed record DecodeJob(PeSection Section, int FileOffset, int Length, ulong StartVirtualAddress);

    private sealed class MemoryCodeReader(ReadOnlyMemory<byte> code) : CodeReader
    {
        private int _position;

        public override int ReadByte() =>
            (uint)_position < (uint)code.Length ? code.Span[_position++] : -1;
    }
}
