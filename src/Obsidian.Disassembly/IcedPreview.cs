using Iced.Intel;

namespace Obsidian.Disassembly;

/// <summary>One instruction decoded from a proposed patch payload.</summary>
public sealed record InstructionPreview(
    bool IsValid,
    int Length,
    string Text,
    string? Error);

/// <summary>
/// Small validation helper for the patch dialog. It does not replace the
/// disassembler: it decodes exactly one instruction so the UI can reject
/// invalid or length-changing patch bytes before touching the image.
/// </summary>
public static class IcedPreview
{
    public static InstructionPreview Decode(ReadOnlySpan<byte> bytes, ulong ip, bool is64Bit)
    {
        if (bytes.IsEmpty)
        {
            return new InstructionPreview(false, 0, string.Empty, "Enter at least one byte.");
        }

        if (bytes.Length > 15)
        {
            return new InstructionPreview(false, 0, string.Empty, "x86 instructions are at most 15 bytes.");
        }

        var reader = new ByteArrayCodeReader(bytes.ToArray());
        var decoder = Decoder.Create(is64Bit ? 64 : 32, reader);
        decoder.IP = ip;
        var instruction = decoder.Decode();

        if (instruction.IsInvalid)
        {
            return new InstructionPreview(false, instruction.Length, string.Empty, "The bytes do not decode as a valid instruction.");
        }

        if (instruction.Length != bytes.Length)
        {
            return new InstructionPreview(
                false,
                instruction.Length,
                string.Empty,
                $"The bytes decode as a {instruction.Length}-byte instruction, not {bytes.Length} bytes.");
        }

        var formatter = new IntelFormatter();
        formatter.Options.SpaceAfterOperandSeparator = true;
        formatter.Options.UppercaseHex = false;
        formatter.Options.HexPrefix = "0x";
        formatter.Options.HexSuffix = string.Empty;
        formatter.Options.ShowBranchSize = false;

        var output = new StringOutput();
        formatter.Format(instruction, output);
        return new InstructionPreview(true, instruction.Length, output.ToString(), null);
    }
}
