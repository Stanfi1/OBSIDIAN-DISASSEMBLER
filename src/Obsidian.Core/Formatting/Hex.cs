using System.Globalization;

namespace Obsidian.Core.Formatting;

/// <summary>Formatting helpers shared by every UI surface.</summary>
public static class Hex
{
    public static string Rva(uint value) => "0x" + value.ToString("X8", CultureInfo.InvariantCulture);

    public static string Va(ulong value) => "0x" + value.ToString("X16", CultureInfo.InvariantCulture);

    public static string Address(ulong value) =>
        value <= uint.MaxValue ? Rva((uint)value) : Va(value);

    public static string Offset(long value) =>
        value < 0 ? "—" : "0x" + value.ToString("X8", CultureInfo.InvariantCulture);

    public static string UInt32(uint value) => "0x" + value.ToString("X8", CultureInfo.InvariantCulture);

    public static string UInt16(ushort value) => "0x" + value.ToString("X4", CultureInfo.InvariantCulture);

    public static string UInt64(ulong value) => "0x" + value.ToString("X16", CultureInfo.InvariantCulture);

    public static string Byte(byte value) => "0x" + value.ToString("X2", CultureInfo.InvariantCulture);

    public static string Bytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        return string.Create(bytes.Length * 3 - 1, bytes.ToArray(), static (span, data) =>
        {
            const string alphabet = "0123456789ABCDEF";
            var index = 0;
            for (var i = 0; i < data.Length; i++)
            {
                if (i > 0)
                {
                    span[index++] = ' ';
                }

                span[index++] = alphabet[data[i] >> 4];
                span[index++] = alphabet[data[i] & 0xF];
            }
        });
    }

    public static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes.ToString("N0", CultureInfo.InvariantCulture)} B"
            : string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", value, units[unit]);
    }

    public static string Timestamp(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    public static string Percent(double value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
