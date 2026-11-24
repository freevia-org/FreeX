namespace FreeW.App.Presentation.Editing;

/// <summary>Portable comment stamp and badge initials rules used by the native FreeW renderers.</summary>
public static class CommentInitialsPolicy
{
    /// <summary>
    /// The default initials for a comment author: the shared Office rule (first letter of each
    /// name word, upper-cased), surrogate-pair safe.
    /// </summary>
    public static string Derive(string? author) =>
        Free.Shared.AppServices.AuthorInitials.Derive(author);

    public static string ResolveBadge(string? initials, string? author, string fallback = "C")
    {
        if (!string.IsNullOrWhiteSpace(initials))
            return Free.Shared.AppServices.AuthorInitials.FirstTextElement(initials.Trim()).ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(author))
            return Free.Shared.AppServices.AuthorInitials.FirstTextElement(author.Trim()).ToUpperInvariant();
        return fallback;
    }
}
