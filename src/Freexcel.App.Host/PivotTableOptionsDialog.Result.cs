using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class PivotTableOptionsDialog
{
    public static PivotTableOptionsDialogResult FromPivotTable(PivotTableModel pivotTable, PivotCacheModel? cache = null) =>
        CreateResult(
            pivotTable.ShowRowGrandTotals,
            pivotTable.ShowColumnGrandTotals,
            pivotTable.ShowSubtotals,
            pivotTable.SubtotalPlacement,
            pivotTable.RepeatItemLabels,
            pivotTable.BlankLineAfterItems,
            pivotTable.StyleName,
            pivotTable.ShowRowHeaders,
            pivotTable.ShowColumnHeaders,
            pivotTable.ShowRowStripes,
            pivotTable.ShowColumnStripes,
            pivotTable.ReportLayout,
            pivotTable.EmptyValueText,
            refreshOnOpen: cache?.RefreshOnLoad ?? false,
            saveSourceData: cache?.SaveData ?? true,
            enableRefresh: cache?.EnableRefresh ?? true,
            preserveSourceSortFilter: cache?.PreserveSourceSortFilter ?? true,
            missingItemsLimit: cache?.MissingItemsLimit,
            printTitles: pivotTable.PrintTitles,
            printExpandCollapseButtons: pivotTable.PrintExpandCollapseButtons,
            altTextTitle: pivotTable.AltTextTitle,
            altTextDescription: pivotTable.AltTextDescription,
            compactRowLabelIndent: pivotTable.CompactRowLabelIndent,
            showExpandCollapseButtons: pivotTable.ShowExpandCollapseButtons,
            autofitColumnsOnUpdate: pivotTable.AutofitColumnsOnUpdate,
            preserveFormattingOnUpdate: pivotTable.PreserveFormattingOnUpdate,
            showFieldHeaders: pivotTable.ShowFieldHeaders,
            showContextualTooltips: pivotTable.ShowContextualTooltips,
            showPropertiesInTooltips: pivotTable.ShowPropertiesInTooltips,
            showClassicLayout: pivotTable.ShowClassicLayout,
            mergeAndCenterLabels: pivotTable.MergeAndCenterLabels,
            showItemsWithNoDataOnRows: pivotTable.ShowItemsWithNoDataOnRows,
            showItemsWithNoDataOnColumns: pivotTable.ShowItemsWithNoDataOnColumns,
            pageOverThenDown: pivotTable.PageOverThenDown,
            pageWrap: pivotTable.PageWrap);

    public static PivotTableOptionsDialogResult CreateResult(
        bool showRowGrandTotals,
        bool showColumnGrandTotals,
        bool showSubtotals,
        PivotSubtotalPlacement subtotalPlacement,
        bool repeatItemLabels,
        bool blankLineAfterItems,
        string styleName,
        bool showRowHeaders,
        bool showColumnHeaders,
        bool showRowStripes,
        bool showColumnStripes,
        PivotReportLayout reportLayout,
        string? emptyValueText = null,
        bool refreshOnOpen = false,
        bool saveSourceData = true,
        bool enableRefresh = true,
        bool preserveSourceSortFilter = true,
        int? missingItemsLimit = null,
        bool printTitles = false,
        bool printExpandCollapseButtons = false,
        string? altTextTitle = null,
        string? altTextDescription = null,
        int compactRowLabelIndent = 1,
        bool showExpandCollapseButtons = true,
        bool autofitColumnsOnUpdate = true,
        bool preserveFormattingOnUpdate = true,
        bool showFieldHeaders = true,
        bool showContextualTooltips = true,
        bool showPropertiesInTooltips = true,
        bool showClassicLayout = false,
        bool mergeAndCenterLabels = false,
        bool showItemsWithNoDataOnRows = false,
        bool showItemsWithNoDataOnColumns = false,
        bool pageOverThenDown = false,
        int pageWrap = 0) =>
        new(
            showRowGrandTotals,
            showColumnGrandTotals,
            showSubtotals,
            subtotalPlacement,
            repeatItemLabels,
            blankLineAfterItems,
            string.IsNullOrWhiteSpace(styleName) ? "PivotStyleLight16" : styleName.Trim(),
            showRowHeaders,
            showColumnHeaders,
            showRowStripes,
            showColumnStripes,
            reportLayout,
            NormalizeEmptyValueText(emptyValueText),
            refreshOnOpen,
            saveSourceData,
            enableRefresh,
            preserveSourceSortFilter,
            NormalizeMissingItemsLimit(missingItemsLimit),
            printTitles,
            printExpandCollapseButtons,
            NormalizeOptionalText(altTextTitle),
            NormalizeOptionalText(altTextDescription),
            NormalizeCompactRowLabelIndent(compactRowLabelIndent),
            showExpandCollapseButtons,
            autofitColumnsOnUpdate,
            preserveFormattingOnUpdate,
            showFieldHeaders,
            showContextualTooltips,
            showPropertiesInTooltips,
            showClassicLayout,
            mergeAndCenterLabels,
            showItemsWithNoDataOnRows,
            showItemsWithNoDataOnColumns,
            pageOverThenDown,
            NormalizePageWrap(pageWrap));

    private static string? NormalizeEmptyValueText(string? text) => NormalizeOptionalText(text);

    private static int ParseCompactRowLabelIndent(string? text) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? NormalizeCompactRowLabelIndent(value)
            : 1;

    private static int NormalizeCompactRowLabelIndent(int indent) => Math.Clamp(indent, 0, 15);

    private const string PageFieldLayoutDownThenOver = "Down, then over";
    private const string PageFieldLayoutOverThenDown = "Over, then down";
    private static readonly string[] PageFieldLayoutLabels = [PageFieldLayoutDownThenOver, PageFieldLayoutOverThenDown];

    private static bool PageFieldLayoutForLabel(string? label) =>
        string.Equals(label, PageFieldLayoutOverThenDown, StringComparison.OrdinalIgnoreCase);

    private static int ParsePageWrap(string? text) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? NormalizePageWrap(value)
            : 0;

    private static int NormalizePageWrap(int pageWrap) => Math.Clamp(pageWrap, 0, 255);

    private const int MaximumMissingItemsLimit = 1_048_576;
    private const string MissingItemsAutomatic = "Automatic";
    private const string MissingItemsNone = "None";
    private const string MissingItemsMaximum = "Maximum";
    private static readonly string[] MissingItemsLimitLabels = [MissingItemsAutomatic, MissingItemsNone, MissingItemsMaximum];

    private static int? NormalizeMissingItemsLimit(int? value) =>
        value switch
        {
            null => null,
            <= 0 => 0,
            _ => MaximumMissingItemsLimit
        };

    private static string LabelForMissingItemsLimit(int? value) =>
        value switch
        {
            null => MissingItemsAutomatic,
            <= 0 => MissingItemsNone,
            _ => MissingItemsMaximum
        };

    private static int? MissingItemsLimitForLabel(string? label) =>
        string.Equals(label, MissingItemsNone, StringComparison.OrdinalIgnoreCase)
            ? 0
            : string.Equals(label, MissingItemsMaximum, StringComparison.OrdinalIgnoreCase)
                ? MaximumMissingItemsLimit
                : null;

    private static string? NormalizeOptionalText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return text.Trim();
    }
}
