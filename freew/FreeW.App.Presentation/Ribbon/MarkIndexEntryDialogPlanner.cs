using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public enum IndexEntryReferenceKind
{
    CurrentPage,
    PageRange,
    CrossReference
}

public sealed record MarkIndexEntryDialogState(
    string MainEntry,
    string Subentry,
    IndexEntryReferenceKind ReferenceKind,
    string BookmarkName,
    string CrossReference,
    bool BoldPageNumber,
    bool ItalicPageNumber);

public sealed record MarkIndexEntryValidation(string Message);

/// <summary>Host-neutral state and validation for Word's References &gt; Mark Entry dialog.</summary>
public static class MarkIndexEntryDialogPlanner
{
    public const double DialogWidth = 390;
    public const double ContentHorizontalMargin = 16;
    public const double ContentTopMargin = 16;
    public const double LabelBottomMargin = 4;
    public const double FieldBottomMargin = 10;
    public const double OptionBottomMargin = 6;
    public const double StatusBottomMargin = 8;
    public const double ActionRowTopMargin = 10;
    public const double ActionRowBottomMargin = 16;
    public const string Title = "Mark Index Entry";
    public const string MainEntryLabel = "Main entry:";
    public const string SubentryLabel = "Subentry (optional):";
    public const string OptionsLabel = "Options:";
    public const string CurrentPageLabel = "Current page";
    public const string PageRangeLabel = "Page range:";
    public const string CrossReferenceLabel = "Cross-reference:";
    public const string PageNumberFormatLabel = "Page number format:";
    public const string BoldLabel = "Bold";
    public const string ItalicLabel = "Italic";
    public const string MarkButtonLabel = "Mark";
    public const string MarkAllButtonLabel = "Mark All";
    public const string CancelButtonLabel = "Cancel";
    public const string DefaultCrossReference = "See ";
    public const string MissingMainEntryMessage = "Enter the main index entry before marking.";
    public const string MissingCrossReferenceMessage = "Enter the cross-reference text before marking.";
    public const string MissingBookmarkMessage = "Select a bookmark for the page range before marking.";

    public static MarkIndexEntryDialogState BuildInitialState(string? selectedText) =>
        new(
            (selectedText ?? string.Empty).Trim(),
            string.Empty,
            IndexEntryReferenceKind.CurrentPage,
            string.Empty,
            DefaultCrossReference,
            BoldPageNumber: false,
            ItalicPageNumber: false);

    public static bool CanMarkAll(string? selectedText, IndexEntryReferenceKind referenceKind) =>
        !string.IsNullOrWhiteSpace(selectedText)
        && referenceKind != IndexEntryReferenceKind.PageRange;

    public static bool TryBuildMark(
        MarkIndexEntryDialogState state,
        out IndexMark? mark,
        out MarkIndexEntryValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(state);
        var mainEntry = state.MainEntry.Trim();
        if (mainEntry.Length == 0)
        {
            mark = null;
            validation = new MarkIndexEntryValidation(MissingMainEntryMessage);
            return false;
        }

        var crossReference = state.ReferenceKind == IndexEntryReferenceKind.CrossReference
            ? state.CrossReference.Trim()
            : string.Empty;
        if (state.ReferenceKind == IndexEntryReferenceKind.CrossReference && crossReference.Length == 0)
        {
            mark = null;
            validation = new MarkIndexEntryValidation(MissingCrossReferenceMessage);
            return false;
        }

        var bookmarkName = state.ReferenceKind == IndexEntryReferenceKind.PageRange
            ? state.BookmarkName.Trim()
            : string.Empty;
        if (state.ReferenceKind == IndexEntryReferenceKind.PageRange && bookmarkName.Length == 0)
        {
            mark = null;
            validation = new MarkIndexEntryValidation(MissingBookmarkMessage);
            return false;
        }

        mark = new IndexMark(
            mainEntry,
            state.Subentry.Trim(),
            crossReference,
            BoldPageNumber: state.ReferenceKind != IndexEntryReferenceKind.CrossReference && state.BoldPageNumber,
            ItalicPageNumber: state.ReferenceKind != IndexEntryReferenceKind.CrossReference && state.ItalicPageNumber,
            BookmarkName: bookmarkName);
        validation = null;
        return true;
    }
}
