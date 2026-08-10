namespace FreeW.App.Presentation.Editing;

public enum CommentInitialsWordSelection
{
    LeadingWords,
    FirstAndLastWords,
}

public enum CommentInitialsSeparatorMode
{
    AnyWhitespace,
    Spaces,
}

public sealed record CommentInitialsDerivationSpec(
    int MaximumInitials,
    CommentInitialsWordSelection WordSelection,
    CommentInitialsSeparatorMode SeparatorMode,
    string EmptyFallback);

/// <summary>Portable comment stamp and badge initials rules used by the native FreeW renderers.</summary>
public static class CommentInitialsPolicy
{
    public static CommentInitialsDerivationSpec FirstThreeWords { get; } = new(
        3,
        CommentInitialsWordSelection.LeadingWords,
        CommentInitialsSeparatorMode.AnyWhitespace,
        "?");

    public static CommentInitialsDerivationSpec FirstAndLastWords { get; } = new(
        2,
        CommentInitialsWordSelection.FirstAndLastWords,
        CommentInitialsSeparatorMode.Spaces,
        string.Empty);

    public static string Derive(string? author, CommentInitialsDerivationSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.MaximumInitials <= 0)
            throw new ArgumentOutOfRangeException(nameof(spec), "Maximum initials must be positive.");

        var parts = Split(author, spec.SeparatorMode);
        if (parts.Length == 0)
            return spec.EmptyFallback;

        var selected = spec.WordSelection == CommentInitialsWordSelection.FirstAndLastWords && parts.Length > 1
            ? new[] { parts[0], parts[^1] }
            : parts.Take(spec.MaximumInitials).ToArray();
        var initials = string.Concat(selected
            .Take(spec.MaximumInitials)
            .Select(part => char.ToUpperInvariant(part[0])));
        return initials.Length == 0 ? spec.EmptyFallback : initials;
    }

    public static string ResolveBadge(string? initials, string? author, string fallback = "C")
    {
        if (!string.IsNullOrWhiteSpace(initials))
            return initials.Trim()[..1].ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(author))
            return author.Trim()[..1].ToUpperInvariant();
        return fallback;
    }

    private static string[] Split(string? author, CommentInitialsSeparatorMode separatorMode)
    {
        if (string.IsNullOrWhiteSpace(author))
            return [];

        return separatorMode == CommentInitialsSeparatorMode.AnyWhitespace
            ? author.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            : author.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
