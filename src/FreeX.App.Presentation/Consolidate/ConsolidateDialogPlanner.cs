using FreeX.Core.Commands;
using FreeX.Core.Model;
using Free.Shared.Localization;

namespace FreeX.App.Presentation.Consolidate;

public delegate bool ConsolidateSourceRangesParser(
    string input,
    out IReadOnlyList<GridRange> ranges,
    out string? invalidPart);

public delegate bool ConsolidateDestinationParser(string input, out CellAddress destination);

public delegate bool ConsolidateReferenceParser(string input, out GridRange range);

public static class ConsolidateDialogPlanner
{
    public const double WpfWindowWidth = 420;
    public const double CaptureWidth = 380;
    public const double CaptureHeight = 420;
    public const double CaptureContentWidth = 341;
    public const double CaptureContentHeight = 361;
    public const double MinWidth = 360;
    public const double ReferencesListHeight = 72;

    private static readonly IReadOnlyList<(ConsolidateFunction Function, string Label)> FunctionChoiceValues =
        Array.AsReadOnly(
        [
            (ConsolidateFunction.Sum, "Sum"),
            (ConsolidateFunction.Count, "Count"),
            (ConsolidateFunction.Average, "Average"),
            (ConsolidateFunction.Max, "Max"),
            (ConsolidateFunction.Min, "Min"),
            (ConsolidateFunction.Product, "Product"),
            (ConsolidateFunction.CountNumbers, "Count Numbers"),
            (ConsolidateFunction.StdDev, "StdDev"),
            (ConsolidateFunction.StdDevp, "StdDevp"),
            (ConsolidateFunction.Var, "Var"),
            (ConsolidateFunction.Varp, "Varp"),
        ]);

    public static IReadOnlyList<(ConsolidateFunction Function, string Label)> FunctionChoices => FunctionChoiceValues;

    public static IReadOnlyList<string> SplitSourceRangeText(string sourceRangesText) =>
        WorkbookRangeTextCodec.SplitReferences(sourceRangesText, allowSemicolon: true);

    public static string JoinSourceRanges(IEnumerable<string> sourceRanges) =>
        string.Join("; ", sourceRanges.Select(item => item.Trim()).Where(item => item.Length > 0));

    public static bool HasPendingReferenceText(IEnumerable<string> existingReferences, string? referenceText)
    {
        var pendingReferences = SplitSourceRangeText(referenceText ?? "");
        if (pendingReferences.Count == 0)
            return false;

        var existing = NormalizeReferences(existingReferences);
        return pendingReferences.Any(pending =>
            !existing.Contains(pending, StringComparer.OrdinalIgnoreCase));
    }

    public static bool TryAddReference(
        SheetId sheetId,
        IEnumerable<string> existingReferences,
        string referenceText,
        out IReadOnlyList<string> updatedReferences,
        out ConsolidateDialogIssue issue) =>
        TryAddReference(
            sheetId,
            _ => null,
            existingReferences,
            referenceText,
            out updatedReferences,
            out issue);

    public static bool TryAddReference(
        SheetId sheetId,
        Func<string, SheetId?> resolveSheetId,
        IEnumerable<string> existingReferences,
        string referenceText,
        out IReadOnlyList<string> updatedReferences,
        out ConsolidateDialogIssue issue) =>
        TryAddReference(
            existingReferences,
            referenceText,
            (string input, out IReadOnlyList<GridRange> ranges, out string? invalidPart) =>
                ConsolidateInputParser.TryParseSourceRanges(input, sheetId, resolveSheetId, out ranges, out invalidPart),
            rejectDuplicateReferences: false,
            out updatedReferences,
            out issue);

    public static bool TryAddReference(
        IEnumerable<string> existingReferences,
        string referenceText,
        ConsolidateSourceRangesParser parseSourceRanges,
        bool rejectDuplicateReferences,
        out IReadOnlyList<string> updatedReferences,
        out ConsolidateDialogIssue issue)
    {
        ArgumentNullException.ThrowIfNull(existingReferences);
        ArgumentNullException.ThrowIfNull(parseSourceRanges);

        var references = NormalizeReferences(existingReferences);
        updatedReferences = references;
        issue = ConsolidateDialogIssue.None;

        var reference = referenceText.Trim();
        if (!parseSourceRanges(reference, out var ranges, out var invalidPart) || ranges.Count != 1)
        {
            issue = new ConsolidateDialogIssue(
                ConsolidateDialogIssueKind.InvalidSourceRange,
                string.IsNullOrWhiteSpace(invalidPart) ? null : invalidPart);
            return false;
        }

        if (rejectDuplicateReferences && references.Contains(reference, StringComparer.OrdinalIgnoreCase))
        {
            issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.DuplicateSourceReference, reference);
            return false;
        }

        references.Add(reference);
        updatedReferences = references;
        return true;
    }

    public static ConsolidateDialogResult CreateResult(
        IEnumerable<GridRange> sourceRanges,
        CellAddress destinationCell,
        ConsolidateFunction function,
        bool useTopRowLabels = false,
        bool useLeftColumnLabels = false,
        bool createLinksToSourceData = false)
    {
        var ranges = sourceRanges.ToList();
        if (ranges.Count == 0)
            throw new ArgumentException("At least one source range is required.", nameof(sourceRanges));

        return new ConsolidateDialogResult(
            ranges,
            destinationCell,
            function,
            useTopRowLabels,
            useLeftColumnLabels,
            createLinksToSourceData);
    }

    public static bool HaveSameSize(IEnumerable<GridRange> sourceRanges)
    {
        var ranges = sourceRanges.ToList();
        if (ranges.Count < 2)
            return true;

        var rowCount = ranges[0].RowCount;
        var colCount = ranges[0].ColCount;
        return ranges.All(range => range.RowCount == rowCount && range.ColCount == colCount);
    }

    public static bool TryParse(
        SheetId sheetId,
        string sourceRangesText,
        string destinationCellText,
        out ConsolidateDialogResult result,
        out ConsolidateDialogIssue issue) =>
        TryParse(
            sheetId,
            _ => null,
            sourceRangesText,
            destinationCellText,
            ConsolidateFunction.Sum,
            useTopRowLabels: false,
            useLeftColumnLabels: false,
            createLinksToSourceData: false,
            out result,
            out issue);

    public static bool TryParse(
        SheetId sheetId,
        Func<string, SheetId?> resolveSheetId,
        string sourceRangesText,
        string destinationCellText,
        out ConsolidateDialogResult result,
        out ConsolidateDialogIssue issue) =>
        TryParse(
            sheetId,
            resolveSheetId,
            sourceRangesText,
            destinationCellText,
            ConsolidateFunction.Sum,
            useTopRowLabels: false,
            useLeftColumnLabels: false,
            createLinksToSourceData: false,
            out result,
            out issue);

    public static bool TryParse(
        SheetId sheetId,
        string sourceRangesText,
        string destinationCellText,
        ConsolidateFunction function,
        bool useTopRowLabels,
        bool useLeftColumnLabels,
        bool createLinksToSourceData,
        out ConsolidateDialogResult result,
        out ConsolidateDialogIssue issue) =>
        TryParse(
            sheetId,
            _ => null,
            sourceRangesText,
            destinationCellText,
            function,
            useTopRowLabels,
            useLeftColumnLabels,
            createLinksToSourceData,
            out result,
            out issue);

    public static bool TryParse(
        SheetId sheetId,
        Func<string, SheetId?> resolveSheetId,
        string sourceRangesText,
        string destinationCellText,
        ConsolidateFunction function,
        bool useTopRowLabels,
        bool useLeftColumnLabels,
        bool createLinksToSourceData,
        out ConsolidateDialogResult result,
        out ConsolidateDialogIssue issue) =>
        TryParse(
            sourceRangesText,
            destinationCellText,
            (string input, out IReadOnlyList<GridRange> ranges, out string? invalidPart) =>
                ConsolidateInputParser.TryParseSourceRanges(input, sheetId, resolveSheetId, out ranges, out invalidPart),
            (string input, out CellAddress destination) =>
                ConsolidateInputParser.TryParseDestination(input, sheetId, resolveSheetId, out destination),
            function,
            useTopRowLabels,
            useLeftColumnLabels,
            createLinksToSourceData,
            out result,
            out issue);

    public static bool TryParse(
        string sourceRangesText,
        string destinationCellText,
        ConsolidateSourceRangesParser parseSourceRanges,
        ConsolidateDestinationParser parseDestination,
        ConsolidateFunction function,
        bool useTopRowLabels,
        bool useLeftColumnLabels,
        bool createLinksToSourceData,
        out ConsolidateDialogResult result,
        out ConsolidateDialogIssue issue)
    {
        ArgumentNullException.ThrowIfNull(parseSourceRanges);
        ArgumentNullException.ThrowIfNull(parseDestination);

        result = default!;
        issue = ConsolidateDialogIssue.None;

        if (!parseSourceRanges(sourceRangesText, out var ranges, out var invalidPart))
        {
            issue = string.IsNullOrWhiteSpace(invalidPart)
                ? new ConsolidateDialogIssue(ConsolidateDialogIssueKind.NoSourceRanges)
                : new ConsolidateDialogIssue(ConsolidateDialogIssueKind.InvalidSourceRange, invalidPart);
            return false;
        }

        if (ranges.Count == 0)
        {
            issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.NoSourceRanges);
            return false;
        }

        if (!HaveSameSize(ranges))
        {
            issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.MismatchedSourceSizes);
            return false;
        }

        if (!parseDestination(destinationCellText, out var destination))
        {
            issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.InvalidDestinationCell);
            return false;
        }

        result = CreateResult(
            ranges,
            destination,
            function,
            useTopRowLabels,
            useLeftColumnLabels,
            createLinksToSourceData);
        return true;
    }

    public static bool TryPlanApply(
        Workbook workbook,
        IReadOnlyList<string> sourceReferences,
        string destinationCellText,
        ConsolidateReferenceParser parseReference,
        ConsolidateOptions options,
        out ConsolidateApplyPlan plan,
        out ConsolidateDialogIssue issue)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sourceReferences);
        ArgumentNullException.ThrowIfNull(parseReference);
        ArgumentNullException.ThrowIfNull(options);

        plan = default!;
        issue = ConsolidateDialogIssue.None;

        if (sourceReferences.Count == 0)
        {
            issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.NoSourceRanges);
            return false;
        }

        var ranges = new List<GridRange>(sourceReferences.Count);
        foreach (var reference in sourceReferences)
        {
            if (!parseReference(reference, out var sourceRange))
            {
                issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.InvalidSourceRange, reference);
                return false;
            }

            if (workbook.GetSheet(sourceRange.Start.Sheet) is null)
            {
                issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.InvalidSourceRange, reference);
                return false;
            }

            ranges.Add(sourceRange);
        }

        if (!HaveSameSize(ranges))
        {
            issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.MismatchedSourceSizes);
            return false;
        }

        if (!parseReference(destinationCellText, out var destinationRange) ||
            destinationRange.Start != destinationRange.End)
        {
            issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.InvalidDestinationCell);
            return false;
        }

        return TryPlanApply(workbook, ranges, destinationRange.Start, options, out plan, out issue);
    }

    public static bool TryPlanApply(
        Workbook workbook,
        IReadOnlyList<GridRange> sourceRanges,
        CellAddress destinationCell,
        ConsolidateOptions options,
        out ConsolidateApplyPlan plan,
        out ConsolidateDialogIssue issue)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sourceRanges);
        ArgumentNullException.ThrowIfNull(options);

        plan = default!;
        issue = ConsolidateDialogIssue.None;

        if (sourceRanges.Count == 0)
        {
            issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.NoSourceRanges);
            return false;
        }

        if (!HaveSameSize(sourceRanges))
        {
            issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.MismatchedSourceSizes);
            return false;
        }

        var ranges = new List<GridRange>(sourceRanges.Count);
        var sources = new List<ConsolidateSource>(sourceRanges.Count);
        foreach (var sourceRange in sourceRanges)
        {
            var sheet = workbook.GetSheet(sourceRange.Start.Sheet);
            if (sheet is null)
            {
                issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.InvalidSourceRange);
                return false;
            }

            ranges.Add(sourceRange);
            sources.Add(ConsolidateSource.FromGrid(ConsolidateApplyPlanner.ReadSource(sheet, sourceRange)));
        }

        var destinationSheet = workbook.GetSheet(destinationCell.Sheet);
        if (destinationSheet is null)
        {
            issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.InvalidDestinationCell);
            return false;
        }

        var result = ConsolidatePlanner.Plan(sources, options);
        if (result.IsEmpty)
        {
            issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.NoOutput);
            return false;
        }

        var edits = ConsolidateApplyPlanner.MapToEdits(destinationSheet.Id, result, destinationCell);
        if (edits.Count != result.Cells.Count)
        {
            issue = new ConsolidateDialogIssue(ConsolidateDialogIssueKind.OutsideWorksheetBounds);
            return false;
        }

        var overwrites = ConsolidateApplyPlanner.FindOverwriteTargets(destinationSheet, edits);
        plan = new ConsolidateApplyPlan(ranges, destinationCell, options, result, edits, overwrites);
        return true;
    }

    public static ConsolidateRangeSelectionRequest CreateRangeSelectionRequest(
        ConsolidateRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    public static ValidationPresentationDescriptor<ConsolidateDialogFocusTarget> DescribeIssue(
        ConsolidateDialogIssue issue,
        ConsolidateDialogMessageContext context,
        ConsolidateDialogTextProfile profile)
    {
        var focusTarget = issue.Kind == ConsolidateDialogIssueKind.InvalidDestinationCell
            ? ConsolidateDialogFocusTarget.Destination
            : ConsolidateDialogFocusTarget.Reference;

        return new(
            profile == ConsolidateDialogTextProfile.Wpf
                ? DescribeWpfIssue(issue, context)
                : DescribeAvaloniaIssue(issue, context),
            focusTarget);
    }

    public static ValidationPresentationDescriptor<ConsolidateDialogFocusTarget> DescribePendingReference(
        ConsolidateDialogTextProfile profile) =>
        new(
            LocalizedTextDescriptor.Resource("Consolidate_AddTheReferenceBeforeClickingOk"),
            ConsolidateDialogFocusTarget.Reference);

    private static LocalizedTextDescriptor DescribeWpfIssue(
        ConsolidateDialogIssue issue,
        ConsolidateDialogMessageContext context)
    {
        if (context == ConsolidateDialogMessageContext.AddReference)
        {
            return issue.Kind == ConsolidateDialogIssueKind.InvalidSourceRange &&
                   !string.IsNullOrWhiteSpace(issue.InvalidPart)
                ? LocalizedTextDescriptor.Resource("Consolidate_EnterValidSourceRangeWithPart", issue.InvalidPart)
                : LocalizedTextDescriptor.Resource("Consolidate_EnterValidSourceRange");
        }

        return issue.Kind switch
        {
            ConsolidateDialogIssueKind.InvalidSourceRange when !string.IsNullOrWhiteSpace(issue.InvalidPart) =>
                LocalizedTextDescriptor.Resource("Consolidate_EnterValidSourceRangeWithPart", issue.InvalidPart),
            ConsolidateDialogIssueKind.MismatchedSourceSizes =>
                LocalizedTextDescriptor.Resource("Consolidate_SourceRangesMustBeSameSize"),
            ConsolidateDialogIssueKind.InvalidDestinationCell =>
                LocalizedTextDescriptor.Resource("Consolidate_EnterValidDestinationCell"),
            _ => LocalizedTextDescriptor.Resource("Consolidate_EnterAtLeastOneValidSourceRange")
        };
    }

    private static LocalizedTextDescriptor DescribeAvaloniaIssue(
        ConsolidateDialogIssue issue,
        ConsolidateDialogMessageContext context)
    {
        if (context == ConsolidateDialogMessageContext.AddReference)
        {
            return LocalizedTextDescriptor.Resource(
                issue.Kind == ConsolidateDialogIssueKind.DuplicateSourceReference
                    ? "TableLoc_ConsolidateSourceAlreadyListed"
                    : "TableLoc_ConsolidateEnterValidSource");
        }

        return issue.Kind switch
        {
            ConsolidateDialogIssueKind.InvalidSourceRange when !string.IsNullOrWhiteSpace(issue.InvalidPart) =>
                LocalizedTextDescriptor.Resource("TableLoc_ConsolidateCannotResolveSource", issue.InvalidPart),
            ConsolidateDialogIssueKind.MismatchedSourceSizes =>
                LocalizedTextDescriptor.Resource("Consolidate_SourceRangesMustBeSameSize"),
            ConsolidateDialogIssueKind.InvalidDestinationCell =>
                LocalizedTextDescriptor.Resource("TableLoc_ConsolidateEnterValidDestination"),
            ConsolidateDialogIssueKind.NoOutput =>
                LocalizedTextDescriptor.Resource("TableLoc_ConsolidateNoOutput"),
            ConsolidateDialogIssueKind.OutsideWorksheetBounds =>
                LocalizedTextDescriptor.Resource("TableLoc_ConsolidateOutsideBounds"),
            _ => LocalizedTextDescriptor.Resource("TableLoc_ConsolidateAddAtLeastOne")
        };
    }

    private static List<string> NormalizeReferences(IEnumerable<string> sourceRanges) =>
        sourceRanges.Select(item => item.Trim()).Where(item => item.Length > 0).ToList();
}
