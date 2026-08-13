using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Owns workbook and worksheet state shared by row, column, and band-scoped cell mutations.
/// Cell payloads and axis-specific layout state remain with their command because their move
/// and restore ordering differs.
/// </summary>
internal sealed class RowColumnMutationSnapshot
{
    private readonly List<GridRange> _mergedRegions;
    private readonly List<KeyValuePair<CellAddress, string>>? _comments;
    private readonly List<KeyValuePair<CellAddress, string>>? _commentAuthors;
    private readonly List<CellAddress>? _shownComments;
    private readonly List<KeyValuePair<CellAddress, ThreadedComment>>? _threadedComments;
    private readonly List<KeyValuePair<CellAddress, string>>? _hyperlinks;
    private readonly List<KeyValuePair<CellAddress, HyperlinkMetadata>>? _hyperlinkMetadata;
    private readonly List<KeyValuePair<string, GridRange>>? _rangeHyperlinks;
    private readonly List<KeyValuePair<CellAddress, IReadOnlyList<CellTextRun>>>? _richTextRuns;
    private readonly List<KeyValuePair<CellAddress, CellPhoneticGuide>>? _phoneticGuides;
    private readonly List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _dataValidations;
    private readonly List<(ConditionalFormat Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _conditionalFormats;
    private readonly Dictionary<string, NamedRangeSnapshot> _namedRanges;
    private readonly Dictionary<(string Name, SheetId Sheet), (GridRange Range, NamedRangeMetadata Metadata)> _scopedNamedRanges;
    private readonly List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot> _chartVerbatimFormulas;
    private List<RowColumnShiftHelpers.HyperlinkOtherSheetChange>? _otherSheetHyperlinkBookmarks;

    internal Dictionary<CellAddress, string> FormulaTexts { get; } = [];
    private Dictionary<string, string> NamedFormulaTexts { get; } = [];
    private Dictionary<(string Name, SheetId Sheet), string> ScopedNamedFormulaTexts { get; } = [];
    private Dictionary<Guid, string?> ConditionalFormatFormulaTexts { get; } = [];
    private Dictionary<(Guid Id, int Slot), string?> ConditionalFormatThresholdTexts { get; } = [];
    private Dictionary<(Guid Id, int Slot), string?> DataValidationFormulaTexts { get; } = [];
    private Dictionary<Guid, string?> PromotedConditionalFormatFormulaTexts { get; } = [];
    private Dictionary<(Guid Id, int Slot), string?> PromotedConditionalFormatThresholdTexts { get; } = [];
    private Dictionary<(Guid Id, int Slot), string?> PromotedDataValidationFormulaTexts { get; } = [];

    private RowColumnMutationSnapshot(Workbook workbook, Sheet sheet)
    {
        _mergedRegions = sheet.MergedRegions.ToList();
        _comments = RowColumnShiftHelpers.CaptureDictionary(sheet.Comments);
        _commentAuthors = RowColumnShiftHelpers.CaptureDictionary(sheet.CommentAuthors);
        _shownComments = RowColumnShiftHelpers.CaptureAddressSet(sheet.ShownComments);
        _threadedComments = RowColumnShiftHelpers.CaptureDictionary(sheet.ThreadedComments);
        _hyperlinks = RowColumnShiftHelpers.CaptureDictionary(sheet.Hyperlinks);
        _hyperlinkMetadata = RowColumnShiftHelpers.CaptureDictionary(sheet.HyperlinkMetadata);
        _rangeHyperlinks = RowColumnShiftHelpers.CaptureRangeHyperlinks(sheet);
        _richTextRuns = RowColumnShiftHelpers.CaptureDictionary(sheet.RichTextRuns);
        _phoneticGuides = RowColumnShiftHelpers.CaptureDictionary(sheet.CellPhoneticGuides);
        var ruleRanges = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
        _dataValidations = ruleRanges.DataValidations;
        _conditionalFormats = ruleRanges.ConditionalFormats;
        _namedRanges = RowColumnShiftHelpers.CaptureNamedRanges(workbook);
        _scopedNamedRanges = RowColumnShiftHelpers.CaptureScopedNamedRanges(workbook);
        _chartVerbatimFormulas = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(workbook);
    }

    internal static RowColumnMutationSnapshot Capture(Workbook workbook, Sheet sheet) =>
        new(workbook, sheet);

    internal void RewriteReferences(Workbook workbook, Sheet sheet, RewriteOperation operation)
    {
        _otherSheetHyperlinkBookmarks = RowColumnShiftHelpers.ShiftHyperlinkBookmarks(
            workbook,
            sheet,
            operation,
            sheet.Name);

        FormulaTexts.Clear();
        RowColumnShiftHelpers.RewriteAllFormulas(workbook, operation, FormulaTexts);
        NamedFormulaTexts.Clear();
        ScopedNamedFormulaTexts.Clear();
        RowColumnShiftHelpers.RewriteNamedFormulas(
            workbook,
            operation,
            NamedFormulaTexts,
            ScopedNamedFormulaTexts);
        ConditionalFormatFormulaTexts.Clear();
        ConditionalFormatThresholdTexts.Clear();
        DataValidationFormulaTexts.Clear();
        RowColumnShiftHelpers.RewriteRuleFormulas(
            workbook,
            operation,
            ConditionalFormatFormulaTexts,
            ConditionalFormatThresholdTexts,
            DataValidationFormulaTexts);
        RowColumnShiftHelpers.RewriteChartVerbatimFormulas(workbook, operation);
    }

    internal void CaptureRulePromotionFormulas(
        Action<Dictionary<Guid, string?>, Dictionary<(Guid Id, int Slot), string?>, Dictionary<(Guid Id, int Slot), string?>> mutation)
    {
        PromotedConditionalFormatFormulaTexts.Clear();
        PromotedConditionalFormatThresholdTexts.Clear();
        PromotedDataValidationFormulaTexts.Clear();
        mutation(
            PromotedConditionalFormatFormulaTexts,
            PromotedConditionalFormatThresholdTexts,
            PromotedDataValidationFormulaTexts);
    }

    internal List<CellAddress> RestoreRewrittenFormulas(Workbook workbook)
    {
        var rewrittenAddresses = FormulaTexts.Keys.ToList();
        RowColumnShiftHelpers.RestoreFormulas(workbook, FormulaTexts);
        RowColumnShiftHelpers.RestoreNamedFormulas(workbook, NamedFormulaTexts, ScopedNamedFormulaTexts);
        RowColumnShiftHelpers.RestoreRuleFormulas(
            workbook,
            ConditionalFormatFormulaTexts,
            ConditionalFormatThresholdTexts,
            DataValidationFormulaTexts);
        return rewrittenAddresses;
    }

    internal void RestoreRulePromotionFormulas(Workbook workbook) =>
        RowColumnShiftHelpers.RestoreRuleFormulas(
            workbook,
            PromotedConditionalFormatFormulaTexts,
            PromotedConditionalFormatThresholdTexts,
            PromotedDataValidationFormulaTexts);

    internal void RestoreCommonState(Workbook workbook, Sheet sheet, bool restoreRulesInPlace)
    {
        sheet.ReplaceMergedRegions(_mergedRegions);
        if (restoreRulesInPlace)
            RowColumnShiftHelpers.RestoreRuleRangesInPlace(sheet, _dataValidations, _conditionalFormats);
        else
            RowColumnShiftHelpers.RestoreRuleRanges(sheet, _dataValidations, _conditionalFormats);
        RowColumnShiftHelpers.RestoreNamedRanges(workbook, _namedRanges);
        RowColumnShiftHelpers.RestoreScopedNamedRanges(workbook, _scopedNamedRanges);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Comments, _comments);
        RowColumnShiftHelpers.RestoreDictionary(sheet.CommentAuthors, _commentAuthors);
        RowColumnShiftHelpers.RestoreAddressSet(sheet.ShownComments, _shownComments);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ThreadedComments, _threadedComments);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Hyperlinks, _hyperlinks);
        RowColumnShiftHelpers.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadata);
        RowColumnShiftHelpers.RestoreHyperlinkBookmarks(workbook, _otherSheetHyperlinkBookmarks);
        RowColumnShiftHelpers.RestoreRangeHyperlinks(sheet, _rangeHyperlinks);
        RowColumnShiftHelpers.RestoreDictionary(sheet.RichTextRuns, _richTextRuns);
        RowColumnShiftHelpers.RestoreDictionary(sheet.CellPhoneticGuides, _phoneticGuides);
        RowColumnShiftHelpers.RestoreChartVerbatimFormulas(workbook, _chartVerbatimFormulas);
    }

    internal IReadOnlyList<CellAddress> BuildAffectedCells(
        IEnumerable<CellAddress> relocatedOrVacatedCells,
        bool includeRewrittenFormulaAddresses = true) =>
        RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
            relocatedOrVacatedCells,
            includeRewrittenFormulaAddresses ? FormulaTexts : []);
}
