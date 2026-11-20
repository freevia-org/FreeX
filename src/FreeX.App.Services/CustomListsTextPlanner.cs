namespace FreeX.App.Services;

/// <summary>
/// Text form of the Custom Lists editor: one list per line, entries separated by commas or tabs
/// (the same shape Excel accepts when a list is typed or pasted into its "List entries" box).
/// </summary>
public static class CustomListsTextPlanner
{
    public static string Format(IEnumerable<IReadOnlyList<string>> lists) =>
        string.Join(Environment.NewLine, lists.Select(list => string.Join(", ", list)));

    public static List<List<string>> Parse(string? text)
    {
        var lines = (text ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split([',', '\t', ';']).AsEnumerable());
        return AppOptions.NormalizeCustomLists(lines);
    }
}
