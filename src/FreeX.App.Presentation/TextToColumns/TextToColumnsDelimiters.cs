namespace FreeX.App.Presentation.TextToColumns;

/// <summary>Maps well-known delimiter kinds to the characters the splitter recognises.</summary>
public static class TextToColumnsDelimiters
{
    /// <summary>The character a single delimiter kind expands to.</summary>
    public static string CharacterFor(
        TextToColumnsDelimiterKind kind,
        char? customDelimiter = null) => kind switch
    {
        TextToColumnsDelimiterKind.Comma => ",",
        TextToColumnsDelimiterKind.Semicolon => ";",
        TextToColumnsDelimiterKind.Tab => "\t",
        TextToColumnsDelimiterKind.Space => " ",
        TextToColumnsDelimiterKind.Custom => customDelimiter is { } ch
            ? ch.ToString()
            : throw new ArgumentException("A custom delimiter character is required.", nameof(customDelimiter)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported delimiter kind.")
    };

    /// <summary>
    /// Expands a set of delimiter kinds into the concatenated set of delimiter characters. Duplicate
    /// kinds are ignored. Throws when the set is empty.
    /// </summary>
    public static string Resolve(
        IEnumerable<TextToColumnsDelimiterKind> kinds,
        char? customDelimiter = null)
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
}
