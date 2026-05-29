namespace FreeX.App.Host;

internal static class AbsoluteCellReferenceNormalizer
{
    public static string? Normalize(string input)
    {
        var value = input.AsSpan().Trim();
        if (value.IsEmpty)
            return null;

        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        var write = 0;

        ConsumeOptionalAbsoluteMarker(value, ref index);

        if (!ConsumeColumn(value, buffer, ref index, ref write))
            return null;

        ConsumeOptionalAbsoluteMarker(value, ref index);

        if (!ConsumeRow(value, buffer, ref index, ref write) || index != value.Length)
            return null;

        return new string(buffer[..write]);
    }

    private static void ConsumeOptionalAbsoluteMarker(ReadOnlySpan<char> value, ref int index)
    {
        if (index < value.Length && value[index] == '$')
            index++;
    }

    private static bool ConsumeColumn(ReadOnlySpan<char> value, Span<char> buffer, ref int index, ref int write)
    {
        var start = index;
        while (index < value.Length && char.IsLetter(value[index]))
            buffer[write++] = value[index++];

        return index != start;
    }

    private static bool ConsumeRow(ReadOnlySpan<char> value, Span<char> buffer, ref int index, ref int write)
    {
        var start = index;
        while (index < value.Length && char.IsDigit(value[index]))
            buffer[write++] = value[index++];

        return index != start;
    }
}
