namespace FreeX.App.Host;

internal static class TextToColumnsFixedWidthBreakPlanner
{
    private const int FirstBreakPosition = 1;

    public static IReadOnlyList<int> AddBreakPosition(
        IReadOnlyList<int> breakPositions,
        int position,
        int maxLength)
    {
        if (!CanContainBreaks(maxLength))
            return NormalizeBreakPositions(breakPositions);

        var clamped = Math.Clamp(position, FirstBreakPosition, LastBreakPosition(maxLength));
        return NormalizeBreakPositions(breakPositions.Append(clamped));
    }

    public static IReadOnlyList<int> MoveBreakPosition(
        IReadOnlyList<int> breakPositions,
        int index,
        int position,
        int maxLength)
    {
        if (!IsExistingBreakIndex(breakPositions, index))
            return NormalizeBreakPositions(breakPositions);

        var updated = breakPositions.ToList();
        updated.RemoveAt(index);
        return AddBreakPosition(updated, position, maxLength);
    }

    public static IReadOnlyList<int> RemoveBreakPosition(
        IReadOnlyList<int> breakPositions,
        int index)
    {
        if (!IsExistingBreakIndex(breakPositions, index))
            return NormalizeBreakPositions(breakPositions);

        var updated = breakPositions.ToList();
        updated.RemoveAt(index);
        return NormalizeBreakPositions(updated);
    }

    public static IReadOnlyList<int> ParseBreakPositions(string? text) =>
        NormalizeBreakPositions(ParsePositiveBreakPositions(text));

    public static bool TryParseBreakPositions(string? text, int maxLength, out IReadOnlyList<int> positions)
    {
        positions = [];
        var parts = ParseParts(text);
        if (parts.Length == 0 || !CanContainBreaks(maxLength))
            return false;

        var validPositions = new List<int>();
        foreach (var part in parts)
        {
            if (!TryParseBreakPosition(part, maxLength, out var position))
                return false;

            validPositions.Add(position);
        }

        positions = NormalizeBreakPositions(validPositions);
        return positions.Count > 0;
    }

    private static IEnumerable<int> ParsePositiveBreakPositions(string? text)
    {
        foreach (var part in ParseParts(text))
        {
            if (TryParsePositiveBreakPosition(part, out var position))
                yield return position;
        }
    }

    private static bool TryParseBreakPosition(string part, int maxLength, out int position) =>
        TryParsePositiveBreakPosition(part, out position) && position < maxLength;

    private static bool TryParsePositiveBreakPosition(string part, out int position) =>
        int.TryParse(part, out position) && position >= FirstBreakPosition;

    private static bool CanContainBreaks(int maxLength) =>
        maxLength > FirstBreakPosition;

    private static int LastBreakPosition(int maxLength) =>
        maxLength - 1;

    private static bool IsExistingBreakIndex(IReadOnlyList<int> breakPositions, int index) =>
        index >= 0 && index < breakPositions.Count;

    private static IReadOnlyList<int> NormalizeBreakPositions(IEnumerable<int> breakPositions) =>
        breakPositions
            .Distinct()
            .Order()
            .ToList();

    private static string[] ParseParts(string? text) =>
        (text ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
