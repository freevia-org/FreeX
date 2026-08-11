namespace FreeW.App.Presentation.Editing;

/// <summary>Portable comment stamp and badge initials rules used by the native FreeW renderers.</summary>
public static class CommentInitialsPolicy
{
    public static string Derive(string? author)
    {
        var parts = Split(author);
        if (parts.Length == 0)
            return string.Empty;

        var selected = parts.Length > 1
            ? new[] { parts[0], parts[^1] }
            : parts;
        var initials = string.Concat(selected
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0])));
        return initials;
    }

    public static string ResolveBadge(string? initials, string? author, string fallback = "C")
    {
        if (!string.IsNullOrWhiteSpace(initials))
            return initials.Trim()[..1].ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(author))
            return author.Trim()[..1].ToUpperInvariant();
        return fallback;
    }

    private static string[] Split(string? author)
    {
        if (string.IsNullOrWhiteSpace(author))
            return [];

        return author.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }
}
