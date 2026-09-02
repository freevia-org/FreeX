namespace FreeX.App.Presentation.TextToColumns;

public sealed record TextToColumnsDelimiterPlan(
    TextToColumnsDelimiterKind PrimaryKind,
    string Delimiters);

/// <summary>Maps well-known delimiter kinds to the characters the splitter recognises.</summary>
public static class TextToColumnsDelimiters
{
    /// <summary>The character a single delimiter kind expands to.</summary>
    // r200: string, not char -- a one-character delimiter box can hold an astral character, which is
    // TWO UTF-16 code units. Taking one made the delimiter a lone surrogate half, and the splitter
    // scans the cell text per code unit, so it then split inside every unrelated astral character
    // sharing that high surrogate and wrote the halves into new cells.
    public static string CharacterFor(
        TextToColumnsDelimiterKind kind,
        string? customDelimiter = null) => kind switch
    {
        TextToColumnsDelimiterKind.Comma => ",",
        TextToColumnsDelimiterKind.Semicolon => ";",
        TextToColumnsDelimiterKind.Tab => "\t",
        TextToColumnsDelimiterKind.Space => " ",
        TextToColumnsDelimiterKind.Custom => string.IsNullOrEmpty(customDelimiter)
            ? throw new ArgumentException("A custom delimiter character is required.", nameof(customDelimiter))
            : LeadingTextElement(customDelimiter),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported delimiter kind.")
    };

    public static string DelimiterFor(
        TextToColumnsDelimiterKind kind,
        string? customDelimiter = null) => kind switch
    {
        TextToColumnsDelimiterKind.Comma => ",",
        TextToColumnsDelimiterKind.Semicolon => ";",
        TextToColumnsDelimiterKind.Tab => "\t",
        TextToColumnsDelimiterKind.Space => " ",
        TextToColumnsDelimiterKind.Custom => string.IsNullOrEmpty(customDelimiter)
            ? throw new ArgumentException("Custom delimiter is required.", nameof(customDelimiter))
            : LeadingTextElement(customDelimiter),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported delimiter kind.")
    };

    /// <summary>
    /// Expands a set of delimiter kinds into the concatenated set of delimiter characters. Duplicate
    /// kinds are ignored. Throws when the set is empty.
    /// </summary>
    public static string Resolve(
        IEnumerable<TextToColumnsDelimiterKind> kinds,
        string? customDelimiter = null)
    {
        ArgumentNullException.ThrowIfNull(kinds);

        var distinct = new List<TextToColumnsDelimiterKind>();
        foreach (var kind in kinds)
        {
            if (!distinct.Contains(kind))
                distinct.Add(kind);
        }

        if (distinct.Count == 0)
            throw new ArgumentException("Select at least one delimiter.", nameof(kinds));

        return string.Concat(distinct.Select(kind => CharacterFor(kind, customDelimiter)));
    }

    public static TextToColumnsDelimiterPlan CreatePlan(
        IEnumerable<TextToColumnsDelimiterKind> kinds,
        string? customDelimiter = null)
    {
        ArgumentNullException.ThrowIfNull(kinds);

        var distinct = new List<TextToColumnsDelimiterKind>();
        foreach (var kind in kinds)
        {
            if (!distinct.Contains(kind))
                distinct.Add(kind);
        }

        if (distinct.Count == 0)
            throw new ArgumentException("Select at least one delimiter.", nameof(kinds));

        var delimiters = string.Concat(distinct.Select(kind => DelimiterFor(kind, customDelimiter)));
        var primaryKind = distinct.Contains(TextToColumnsDelimiterKind.Custom)
            ? TextToColumnsDelimiterKind.Custom
            : distinct[0];

        return new TextToColumnsDelimiterPlan(primaryKind, delimiters);
    }

    /// <summary>The leading text element of <paramref name="value"/>, so an astral character stays whole.</summary>
    private static string LeadingTextElement(string value) =>
        value[..System.Globalization.StringInfo.GetNextTextElementLength(value)];
}
