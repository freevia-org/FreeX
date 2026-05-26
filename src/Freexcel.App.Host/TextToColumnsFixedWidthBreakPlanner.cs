namespace Freexcel.App.Host;

internal static class TextToColumnsFixedWidthBreakPlanner
{
    public static IReadOnlyList<int> AddBreakPosition(
        IReadOnlyList<int> breakPositions,
        int position,
        int maxLength)
    {
        if (maxLength <= 1)
            return NormalizeBreakPositions(breakPositions);

        var clamped = Math.Clamp(position, 1, maxLength - 1);
        return NormalizeBreakPositions(breakPositions.Append(clamped));
    }

    public static IReadOnlyList<int> MoveBreakPosition(
        IReadOnlyList<int> breakPositions,
        int index,
        int position,
        int maxLength)
    {
        if (index < 0 || index >= breakPositions.Count)
            return NormalizeBreakPositions(breakPositions);

        var updated = breakPositions.ToList();
        updated.RemoveAt(index);
        return AddBreakPosition(updated, position, maxLength);
    }

    public static IReadOnlyList<int> RemoveBreakPosition(
        IReadOnlyList<int> breakPositions,
        int index)
    {
        if (index < 0 || index >= breakPositions.Count)
            return NormalizeBreakPositions(breakPositions);

        var updated = breakPositions.ToList();
        updated.RemoveAt(index);
        return NormalizeBreakPositions(updated);
    }

    public static IReadOnlyList<int> ParseBreakPositions(string? text) =>
        ParseParts(text)
            .Select(part => int.TryParse(part, out var position) ? position : 0)
            .Where(position => position > 0)
            .Distinct()
            .Order()
            .ToList();

    public static bool TryParseBreakPositions(string? text, int maxLength, out IReadOnlyList<int> positions)
    {
        positions = [];
        var parts = ParseParts(text);
        if (parts.Length == 0 || maxLength <= 1)
            return false;

        var parsedPositions = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var position) || position <= 0 || position >= maxLength)
                return false;

            parsedPositions.Add(position);
        }

        positions = NormalizeBreakPositions(parsedPositions);
        return positions.Count > 0;
    }

    private static IReadOnlyList<int> NormalizeBreakPositions(IEnumerable<int> breakPositions) =>
        breakPositions
            .Distinct()
            .Order()
            .ToList();

    private static string[] ParseParts(string? text) =>
        (text ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
