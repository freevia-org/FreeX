using FreeX.App.Presentation.Consolidate;
using FreeX.App.Presentation.Localization;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using SharedConsolidateDialogPlanner = FreeX.App.Presentation.Consolidate.ConsolidateDialogPlanner;

namespace FreeX.App.Host;

public sealed partial class ConsolidateDialog
{
    public static IReadOnlyList<string> SplitSourceRangeText(string sourceRangesText) =>
        SharedConsolidateDialogPlanner.SplitSourceRangeText(sourceRangesText);

    public static string JoinSourceRanges(IEnumerable<string> sourceRanges) =>
        SharedConsolidateDialogPlanner.JoinSourceRanges(sourceRanges);

    public static bool HasPendingReferenceText(IEnumerable<string> existingReferences, string? referenceText) =>
        SharedConsolidateDialogPlanner.HasPendingReferenceText(existingReferences, referenceText);

    public static bool TryAddReference(
        SheetId sheetId,
        IEnumerable<string> existingReferences,
        string referenceText,
        out IReadOnlyList<string> updatedReferences,
        out string? error) =>
        TryAddReference(
            sheetId,
            _ => null,
            existingReferences,
            referenceText,
            out updatedReferences,
            out error);

    public static bool TryAddReference(
        SheetId sheetId,
        Func<string, SheetId?> resolveSheetId,
        IEnumerable<string> existingReferences,
        string referenceText,
        out IReadOnlyList<string> updatedReferences,
        out string? error)
    {
        if (SharedConsolidateDialogPlanner.TryAddReference(
                sheetId,
                resolveSheetId,
                existingReferences,
                referenceText,
                out updatedReferences,
                out var issue))
        {
            error = null;
            return true;
        }

        error = FormatAddReferenceIssue(issue);
        return false;
    }

    public static ConsolidateDialogResult CreateResult(
        IEnumerable<GridRange> sourceRanges,
        CellAddress destinationCell,
        ConsolidateFunction function,
        bool useTopRowLabels = false,
        bool useLeftColumnLabels = false,
        bool createLinksToSourceData = false)
    {
        try
        {
            return SharedConsolidateDialogPlanner.CreateResult(
                sourceRanges,
                destinationCell,
                function,
                useTopRowLabels,
                useLeftColumnLabels,
                createLinksToSourceData);
        }
        catch (ArgumentException ex) when (ex.ParamName == nameof(sourceRanges))
        {
            throw new ArgumentException(UiText.Get("Consolidate_AtLeastOneSourceRangeRequired"), nameof(sourceRanges), ex);
        }
    }

    public static bool HaveSameSize(IEnumerable<GridRange> sourceRanges) =>
        SharedConsolidateDialogPlanner.HaveSameSize(sourceRanges);

    public static bool TryParse(
        SheetId sheetId,
        string sourceRangesText,
        string destinationCellText,
        out ConsolidateDialogResult result,
        out string? error) =>
        TryParse(
            sheetId,
            sourceRangesText,
            destinationCellText,
            ConsolidateFunction.Sum,
            useTopRowLabels: false,
            useLeftColumnLabels: false,
            createLinksToSourceData: false,
            out result,
            out error);

    public static bool TryParse(
        SheetId sheetId,
        Func<string, SheetId?> resolveSheetId,
        string sourceRangesText,
        string destinationCellText,
        out ConsolidateDialogResult result,
        out string? error) =>
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
            out error);

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
        out string? error)
    {
        var success = TryParseWithPresentation(
            sheetId,
            resolveSheetId,
            sourceRangesText,
            destinationCellText,
            function,
            useTopRowLabels,
            useLeftColumnLabels,
            createLinksToSourceData,
            out result,
            out var presentation);
        error = presentation?.Message.Resolve(UiText.Get, UiText.Format);
        return success;
    }

    internal static bool TryParseWithPresentation(
        SheetId sheetId,
        Func<string, SheetId?> resolveSheetId,
        string sourceRangesText,
        string destinationCellText,
        ConsolidateFunction function,
        bool useTopRowLabels,
        bool useLeftColumnLabels,
        bool createLinksToSourceData,
        out ConsolidateDialogResult result,
        out ValidationPresentationDescriptor<ConsolidateDialogFocusTarget>? presentation)
    {
        if (SharedConsolidateDialogPlanner.TryParse(
                sheetId,
                resolveSheetId,
                sourceRangesText,
                destinationCellText,
                function,
                useTopRowLabels,
                useLeftColumnLabels,
                createLinksToSourceData,
                out result,
                out var issue))
        {
            presentation = null;
            return true;
        }

        presentation = SharedConsolidateDialogPlanner.DescribeIssue(
            issue,
            ConsolidateDialogMessageContext.FinalValidation,
            ConsolidateDialogTextProfile.Wpf);
        return false;
    }

    public static ConsolidateRangeSelectionRequest CreateRangeSelectionRequest(
        ConsolidateRangeSelectionTarget target,
        string currentText) =>
        SharedConsolidateDialogPlanner.CreateRangeSelectionRequest(target, currentText);

    private static string FunctionLabel(ConsolidateFunction function) =>
        function switch
        {
            ConsolidateFunction.CountNumbers => UiText.Get("Consolidate_FunctionCountNumbers"),
            ConsolidateFunction.StdDev => UiText.Get("Consolidate_FunctionStdDev"),
            ConsolidateFunction.StdDevp => UiText.Get("Consolidate_FunctionStdDevp"),
            _ => function.ToString()
        };

    private static string FormatAddReferenceIssue(ConsolidateDialogIssue issue) =>
        SharedConsolidateDialogPlanner
            .DescribeIssue(
                issue,
                ConsolidateDialogMessageContext.AddReference,
                ConsolidateDialogTextProfile.Wpf)
            .Message
            .Resolve(UiText.Get, UiText.Format);

}
