using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class ArrangeAllMenuPlanner
{
    public static bool IsChecked(object? tag, WorkbookWindowArrangement current) =>
        string.Equals(tag?.ToString(), current.ToString(), StringComparison.Ordinal);

    public static bool TryParseArrangement(object? tag, out WorkbookWindowArrangement arrangement)
    {
        if (tag is string text &&
            Enum.TryParse(text, ignoreCase: false, out arrangement) &&
            Enum.IsDefined(arrangement))
        {
            return true;
        }

        arrangement = default;
        return false;
    }
}
