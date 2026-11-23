namespace Obsidian.Core.Searching;

/// <summary>
/// Byte-pattern search with wrap-around, shared by the hex editor and future
/// string/bytes scanners. Offsets are byte offsets.
/// </summary>
public static class ByteSearch
{
    /// <summary>Finds the next occurrence after <paramref name="startOffset"/>.</summary>
    public static long FindForward(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> pattern,
        long startOffset,
        CancellationToken cancellationToken = default)
    {
        if (pattern.IsEmpty || pattern.Length > haystack.Length)
        {
            return -1;
        }

        var lastStart = haystack.Length - pattern.Length;
        var start = (int)Math.Clamp(startOffset, 0, Math.Max(0, haystack.Length - 1));
        var begin = Math.Clamp(start + 1, 0, haystack.Length);

        if (begin <= lastStart)
        {
            var index = haystack[begin..].IndexOf(pattern);
            if (index >= 0)
            {
                return begin + index;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Include the bytes immediately before the start so a match that
        // begins at the wrap boundary is still found. Because the suffix was
        // already searched with no hit, a hit here must start before begin.
        var wrapLength = Math.Min(haystack.Length, begin + pattern.Length - 1);
        return haystack[..wrapLength].IndexOf(pattern);
    }

    /// <summary>Finds the previous occurrence before <paramref name="startOffset"/>.</summary>
    public static long FindBackward(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> pattern,
        long startOffset,
        CancellationToken cancellationToken = default)
    {
        if (pattern.IsEmpty || pattern.Length > haystack.Length)
        {
            return -1;
        }

        var lastStart = haystack.Length - pattern.Length;
        var start = (int)Math.Clamp(startOffset, 0, Math.Max(0, haystack.Length - 1));
        var reverseStart = Math.Min(start - 1, lastStart);

        for (var index = reverseStart; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (haystack.Slice(index, pattern.Length).SequenceEqual(pattern))
            {
                return index;
            }
        }

        for (var index = lastStart; index > start; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (haystack.Slice(index, pattern.Length).SequenceEqual(pattern))
            {
                return index;
            }
        }

        return -1;
    }
}
