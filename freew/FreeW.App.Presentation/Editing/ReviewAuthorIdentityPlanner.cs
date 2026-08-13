namespace FreeW.App.Presentation.Editing;

public sealed record CommentStampIdentity(string Author, string Initials);

/// <summary>Resolves the current review author and comment stamp without renderer-specific policy.</summary>
public static class ReviewAuthorIdentityPlanner
{
    public const string DefaultAuthor = "FreeW User";

    public static string ResolveAuthor(
        string? revisionAuthor,
        string? documentAuthor,
        string? operatingSystemAuthor)
    {
        var author = FirstNonBlank(revisionAuthor, documentAuthor, operatingSystemAuthor);
        return author ?? DefaultAuthor;
    }

    public static CommentStampIdentity BuildCommentStamp(
        string? revisionAuthor,
        string? documentAuthor,
        string? operatingSystemAuthor)
    {
        var author = ResolveAuthor(revisionAuthor, documentAuthor, operatingSystemAuthor);
        return new CommentStampIdentity(
            author,
            CommentInitialsPolicy.Derive(author));
    }

    private static string? FirstNonBlank(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate.Trim();
        }

        return null;
    }
}
