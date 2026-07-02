using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record CompareDocumentsPromptState(
    string DefaultAuthor,
    string RevisedTitle);

public sealed record CombineDocumentsPromptState(
    string DefaultAuthorA,
    string DefaultAuthorB,
    string ReviewerATitle);

public sealed record CompareDocumentsDialogResult(
    string OriginalFilePath,
    string Author,
    CompareSettings Settings);

public sealed record CombineDocumentsDialogResult(
    string OriginalFilePath,
    string ReviewerBFilePath,
    string AuthorA,
    string AuthorB);

public sealed record CompareDocumentsExecutionInput(
    TextDocument Original,
    TextDocument Revised,
    string Author,
    string? DateXml,
    CompareSettings Settings);

public sealed record CombineDocumentsExecutionInput(
    TextDocument Original,
    TextDocument RevisedA,
    string AuthorA,
    TextDocument RevisedB,
    string AuthorB,
    string? DateXml);

public static class ReviewCompareCombineWorkflow
{
    public const string DefaultReviewerB = "Reviewer 2";
    public const string FallbackReviewer = "Reviewer";

    public static CompareDocumentsPromptState BuildComparePrompt(
        TextDocument revised,
        string? currentFileName,
        string? fallbackAuthor)
    {
        ArgumentNullException.ThrowIfNull(revised);

        return new CompareDocumentsPromptState(
            ResolveAuthor(revised.Properties.Author, fallbackAuthor),
            ResolveDocumentTitle(revised, currentFileName));
    }

    public static CombineDocumentsPromptState BuildCombinePrompt(
        TextDocument revisedA,
        string? currentFileName,
        string? fallbackAuthorA,
        string? fallbackAuthorB = null)
    {
        ArgumentNullException.ThrowIfNull(revisedA);

        return new CombineDocumentsPromptState(
            ResolveAuthor(revisedA.Properties.Author, fallbackAuthorA),
            ResolveAuthor(null, fallbackAuthorB, DefaultReviewerB),
            ResolveDocumentTitle(revisedA, currentFileName));
    }

    public static string CreateRevisionDateXml(DateTimeOffset timestamp) =>
        timestamp
            .ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public static TextDocument ExecuteCompare(CompareDocumentsExecutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Original);
        ArgumentNullException.ThrowIfNull(input.Revised);
        ArgumentNullException.ThrowIfNull(input.Settings);

        return DocumentCompare.Compare(
            input.Original,
            input.Revised,
            NormalizeRequiredAuthor(input.Author, nameof(input.Author)),
            input.DateXml,
            input.Settings);
    }

    public static TextDocument ExecuteCombine(CombineDocumentsExecutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Original);
        ArgumentNullException.ThrowIfNull(input.RevisedA);
        ArgumentNullException.ThrowIfNull(input.RevisedB);

        return DocumentCombine.Combine(
            input.Original,
            input.RevisedA,
            NormalizeRequiredAuthor(input.AuthorA, nameof(input.AuthorA)),
            input.RevisedB,
            NormalizeRequiredAuthor(input.AuthorB, nameof(input.AuthorB)),
            input.DateXml);
    }

    public static string TruncatePathForDialog(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var fileName = ExtractFileName(path);
        if (fileName.Length == 0)
            return path;

        var parent = ExtractFileName(TrimTrailingSeparator(ParentPath(path)));
        return parent.Length == 0 ? fileName : $"...\\{parent}\\{fileName}";
    }

    private static string ResolveAuthor(string? modelAuthor, string? fallbackAuthor, string fallbackIfEmpty = FallbackReviewer)
    {
        var author = TrimOrNull(modelAuthor) ?? TrimOrNull(fallbackAuthor) ?? fallbackIfEmpty;
        return author;
    }

    private static string ResolveDocumentTitle(TextDocument document, string? currentFileName)
    {
        var title = TrimOrNull(document.Properties.Title);
        if (title is not null)
            return title;

        var fileName = currentFileName is null ? null : TrimOrNull(ExtractFileName(currentFileName));
        return fileName ?? string.Empty;
    }

    private static string NormalizeRequiredAuthor(string author, string parameterName)
    {
        var trimmed = author.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("A reviewer name is required.", parameterName);
        return trimmed;
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string ParentPath(string path)
    {
        var trimmed = TrimTrailingSeparator(path);
        var separator = LastSeparatorIndex(trimmed);
        return separator < 0 ? string.Empty : trimmed[..separator];
    }

    private static string ExtractFileName(string path)
    {
        var trimmed = TrimTrailingSeparator(path);
        var separator = LastSeparatorIndex(trimmed);
        return separator < 0 ? trimmed : trimmed[(separator + 1)..];
    }

    private static string TrimTrailingSeparator(string path) =>
        path.TrimEnd('\\', '/');

    private static int LastSeparatorIndex(string path) =>
        Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
}
