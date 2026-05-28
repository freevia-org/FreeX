namespace Freexcel.App.Host;

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

        if (value[index] == '$')
            index++;

        var columnStart = index;
        while (index < value.Length && char.IsLetter(value[index]))
            buffer[write++] = value[index++];

        if (index == columnStart)
            return null;

        if (index < value.Length && value[index] == '$')
            index++;

        var rowStart = index;
        while (index < value.Length && char.IsDigit(value[index]))
            buffer[write++] = value[index++];

        if (index == rowStart || index != value.Length)
            return null;

        return new string(buffer[..write]);
    }
}
