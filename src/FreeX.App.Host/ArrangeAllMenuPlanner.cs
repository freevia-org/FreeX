using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class ArrangeAllMenuPlanner
{
    public static bool IsChecked(object? tag, WorkbookWindowArrangement current) =>
        string.Equals(GetTagText(tag), current.ToString(), StringComparison.Ordinal);

    public static bool TryParseArrangement(object? tag, out WorkbookWindowArrangement arrangement)
    {
        if (tag is string text &&
            TryParseDefinedArrangement(text, out arrangement))
        {
            return true;
        }

        arrangement = default;
        return false;
    }

    private static string? GetTagText(object? tag) =>
        tag?.ToString();

    private static bool TryParseDefinedArrangement(
        string text,
        out WorkbookWindowArrangement arrangement) =>
        Enum.TryParse(text, ignoreCase: false, out arrangement) &&
        Enum.IsDefined(arrangement);
}
