namespace FreeX.App.Host;

public static class PivotStyleCatalog
{
    public static readonly string[] BuiltInStyleNames =
    [
        ..Enumerable.Range(1, 28).Select(index => $"PivotStyleLight{index}"),
        ..Enumerable.Range(1, 28).Select(index => $"PivotStyleMedium{index}"),
        ..Enumerable.Range(1, 28).Select(index => $"PivotStyleDark{index}")
    ];

    public static IReadOnlyList<string> GetStyleNames(string? currentStyleName = null)
    {
        var normalizedCurrent = NormalizeStyleName(currentStyleName);
        if (BuiltInStyleNames.Contains(normalizedCurrent, StringComparer.OrdinalIgnoreCase))
            return BuiltInStyleNames;

        return [..BuiltInStyleNames, normalizedCurrent];
    }

    public static string NormalizeStyleName(string? styleName) =>
        string.IsNullOrWhiteSpace(styleName) ? "PivotStyleLight16" : styleName.Trim();
}
