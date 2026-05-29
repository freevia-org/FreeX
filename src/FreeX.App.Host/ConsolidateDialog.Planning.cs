using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class ConsolidateDialog
{
    public static IReadOnlyList<string> SplitSourceRangeText(string sourceRangesText) =>
        ConsolidateDialogPlanner.SplitSourceRangeText(sourceRangesText);

    public static string JoinSourceRanges(IEnumerable<string> sourceRanges) =>
        ConsolidateDialogPlanner.JoinSourceRanges(sourceRanges);

    public static bool HasPendingReferenceText(IEnumerable<string> existingReferences, string? referenceText) =>
        ConsolidateDialogPlanner.HasPendingReferenceText(existingReferences, referenceText);

    public static bool TryAddReference(
        SheetId sheetId,
        IEnumerable<string> existingReferences,
        string referenceText,
        out IReadOnlyList<string> updatedReferences,
        out string? error) =>
        ConsolidateDialogPlanner.TryAddReference(
            sheetId,
            existingReferences,
            referenceText,
            out updatedReferences,
            out error);

    public static ConsolidateDialogResult CreateResult(
        IEnumerable<GridRange> sourceRanges,
        CellAddress destinationCell,
        ConsolidateFunction function,
        bool useTopRowLabels = false,
        bool useLeftColumnLabels = false,
        bool createLinksToSourceData = false) =>
        ConsolidateDialogPlanner.CreateResult(
            sourceRanges,
            destinationCell,
            function,
            useTopRowLabels,
            useLeftColumnLabels,
            createLinksToSourceData);

    public static bool HaveSameSize(IEnumerable<GridRange> sourceRanges) =>
        ConsolidateDialogPlanner.HaveSameSize(sourceRanges);

    public static bool TryParse(
        SheetId sheetId,
        string sourceRangesText,
        string destinationCellText,
        out ConsolidateDialogResult result,
        out string? error) =>
        ConsolidateDialogPlanner.TryParse(
            sheetId,
            sourceRangesText,
            destinationCellText,
            out result,
            out error);

    public static bool TryParse(
        SheetId sheetId,
        string sourceRangesText,
        string destinationCellText,
        ConsolidateFunction function,
        bool useTopRowLabels,
        bool useLeftColumnLabels,
        bool createLinksToSourceData,
        out ConsolidateDialogResult result,
        out string? error) =>
        ConsolidateDialogPlanner.TryParse(
            sheetId,
            sourceRangesText,
            destinationCellText,
            function,
            useTopRowLabels,
            useLeftColumnLabels,
            createLinksToSourceData,
            out result,
            out error);

    public static ConsolidateRangeSelectionRequest CreateRangeSelectionRequest(
        ConsolidateRangeSelectionTarget target,
        string currentText) =>
        ConsolidateDialogPlanner.CreateRangeSelectionRequest(target, currentText);

    private static string FunctionLabel(ConsolidateFunction function) =>
        ConsolidateDialogPlanner.FunctionLabel(function);
}
