namespace FreeX.App.Host;

internal sealed record TextToColumnsDelimiterPlan(
    TextToColumnsDelimiterKind PrimaryKind,
    string Delimiters);

internal static class TextToColumnsDelimiterPlanner
{
    public static string DelimiterFor(
        TextToColumnsDelimiterKind delimiterKind,
        string? customDelimiter = null) =>
        delimiterKind switch
        {
            TextToColumnsDelimiterKind.Comma => ",",
            TextToColumnsDelimiterKind.Semicolon => ";",
            TextToColumnsDelimiterKind.Tab => "\t",
            TextToColumnsDelimiterKind.Space => " ",
            TextToColumnsDelimiterKind.Custom => string.IsNullOrEmpty(customDelimiter)
                ? throw new ArgumentException("Custom delimiter is required.", nameof(customDelimiter))
                : customDelimiter,
            _ => throw new ArgumentOutOfRangeException(nameof(delimiterKind), delimiterKind, "Unsupported delimiter.")
        };

    public static TextToColumnsDelimiterPlan CreatePlan(
        IEnumerable<TextToColumnsDelimiterKind> delimiterKinds,
        string? customDelimiter = null)
    {
        var kinds = delimiterKinds.Distinct().ToList();
        if (kinds.Count == 0)
            throw new ArgumentException("Select at least one delimiter.", nameof(delimiterKinds));

        var delimiters = string.Concat(kinds.Select(kind => DelimiterFor(kind, customDelimiter)));
        var primaryKind = kinds.Contains(TextToColumnsDelimiterKind.Custom)
            ? TextToColumnsDelimiterKind.Custom
            : kinds[0];

        return new TextToColumnsDelimiterPlan(primaryKind, delimiters);
    }
}
