using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Shell;

public sealed record FreeWEditorStatusSnapshot(
    int Words,
    int CharactersWithSpaces,
    int Paragraphs,
    int CurrentPage = 1,
    int TotalPages = 1,
    int CurrentSection = 1,
    int TotalSections = 1,
    string? SelectionText = null,
    bool IncludePageStatus = true,
    bool IncludeSectionStatus = true,
    bool IsEdited = false);

public sealed record FreeWEditorStatusContext(
    TextDocument Document,
    int CurrentPage = 1,
    int TotalPages = 1,
    int CurrentSection = 1,
    int TotalSections = 1,
    string? SelectionText = null,
    bool IncludePageStatus = true,
    bool IncludeSectionStatus = true,
    bool IsEdited = false);

public sealed record FreeWEditorStatusPlan(
    string PageStatus,
    string SectionStatus,
    string CountsStatus,
    string SummaryStatus);

public static class FreeWEditorStatusPlanner
{
    public static FreeWEditorStatusPlan Build(FreeWEditorStatusContext context) =>
        Build(Project(context));

    public static FreeWEditorStatusSnapshot Project(FreeWEditorStatusContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Document);

        var stats = string.IsNullOrEmpty(context.SelectionText)
            ? WordCount.Of(context.Document)
            : DocumentStats.Empty;
        return new FreeWEditorStatusSnapshot(
            stats.Words,
            stats.CharactersWithSpaces,
            stats.Paragraphs,
            context.CurrentPage,
            context.TotalPages,
            context.CurrentSection,
            context.TotalSections,
            context.SelectionText,
            context.IncludePageStatus,
            context.IncludeSectionStatus,
            context.IsEdited);
    }

    public static FreeWEditorStatusPlan Build(FreeWEditorStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var pageStatus = snapshot.IncludePageStatus
            ? SisterAppStatusBarTextPlanner.FormatDocumentPageStatus(snapshot.CurrentPage, snapshot.TotalPages)
            : string.Empty;
        var sectionStatus = snapshot.IncludeSectionStatus
            ? SisterAppStatusBarTextPlanner.FormatDocumentSectionStatus(snapshot.CurrentSection, snapshot.TotalSections)
            : string.Empty;
        var selectionStatus = BuildSelectionStatus(snapshot.SelectionText);
        var countsStatus = selectionStatus
            ?? SisterAppStatusBarTextPlanner.FormatDocumentCountsStatus(
                snapshot.Words,
                snapshot.CharactersWithSpaces,
                snapshot.Paragraphs);
        var summaryStatus = selectionStatus is null
            ? SisterAppStatusBarTextPlanner.FormatDocumentSummaryStatus(
                snapshot.Words,
                snapshot.CharactersWithSpaces,
                snapshot.Paragraphs,
                pageStatus,
                snapshot.IsEdited)
            : BuildSelectionSummary(pageStatus, selectionStatus, snapshot.IsEdited);

        return new FreeWEditorStatusPlan(pageStatus, sectionStatus, countsStatus, summaryStatus);
    }

    private static string? BuildSelectionStatus(string? selectionText)
    {
        if (string.IsNullOrEmpty(selectionText))
            return null;

        var selectionStats = DocumentStatistics.Compute(selectionText);
        return SisterAppStatusBarTextPlanner.FormatDocumentSelectionStatus(
            selectionStats.Words,
            selectionStats.CharactersWithSpaces);
    }

    private static string BuildSelectionSummary(string pageStatus, string selectionStatus, bool isEdited)
    {
        var text = string.IsNullOrWhiteSpace(pageStatus)
            ? selectionStatus
            : $"{pageStatus}{SisterAppStatusBarTextPlanner.SegmentSeparator}{selectionStatus}";

        return isEdited
            ? $"{text}{SisterAppStatusBarTextPlanner.SegmentSeparator}\u2022 edited"
            : text;
    }
}
