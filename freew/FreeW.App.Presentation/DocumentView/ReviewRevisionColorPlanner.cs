using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Assigns Word-style tracked-change colours to revision authors in first-appearance order.
/// The colour is display chrome only; callers must not serialize it as run formatting.
/// </summary>
public static class ReviewRevisionColorPlanner
{
    public const string FallbackColorHex = "#C00040";

    // The first three colours are the current visible-Word markup palette for the Alice/Bob/Carol
    // sequence in the tracked-change corpus. The remaining entries keep later authors distinct
    // and deterministic.
    private static readonly string[] AuthorPalette =
    [
        "#D13438",
        "#0078D4",
        "#5C2E91",
        "#C00040",
        "#ED7D31",
        "#5B9BD5",
    ];

    public static IReadOnlyDictionary<string, string> BuildAuthorColors(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in RevisionList.Enumerate(document))
        {
            var author = NormalizeAuthor(entry.Author);
            if (author is null || colors.ContainsKey(author))
                continue;

            colors.Add(author, AuthorPalette[colors.Count % AuthorPalette.Length]);
        }

        return colors;
    }

    public static string ResolveColorHex(
        IReadOnlyDictionary<string, string> authorColors,
        string? author)
    {
        ArgumentNullException.ThrowIfNull(authorColors);

        var normalized = NormalizeAuthor(author);
        return normalized is not null && authorColors.TryGetValue(normalized, out var color)
            ? color
            : FallbackColorHex;
    }

    public static string ResolveColorHex(TextDocument document, string? author) =>
        ResolveColorHex(BuildAuthorColors(document), author);

    private static string? NormalizeAuthor(string? author)
    {
        var normalized = author?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
