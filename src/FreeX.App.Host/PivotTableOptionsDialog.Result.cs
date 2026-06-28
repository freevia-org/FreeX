using FreeX.Core.Model;
using FreeX.App.Presentation.PivotUI;

namespace FreeX.App.Host;

public sealed partial class PivotTableOptionsDialog
{
    public static PivotTableOptionsDialogResult FromPivotTable(PivotTableModel pivotTable, PivotCacheModel? cache = null) =>
        FromShared(PivotOptionsPlanner.CaptureDialogValues(pivotTable, cache));

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
        int pageWrap = 0,
        string? errorValueText = null,
        bool enableDrill = true) =>
        FromShared(PivotOptionsPlanner.CreateDialogValues(
            showRowGrandTotals,
            showColumnGrandTotals,
            showSubtotals,
            subtotalPlacement,
            repeatItemLabels,
            blankLineAfterItems,
            styleName,
            showRowHeaders,
            showColumnHeaders,
            showRowStripes,
            showColumnStripes,
            reportLayout,
            emptyValueText,
            refreshOnOpen,
            saveSourceData,
            enableRefresh,
            preserveSourceSortFilter,
            missingItemsLimit,
            printTitles,
            printExpandCollapseButtons,
            altTextTitle,
            altTextDescription,
            compactRowLabelIndent,
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
            pageWrap,
            errorValueText,
            enableDrill));

    private static PivotTableOptionsDialogResult FromShared(PivotOptionsDialogValues values) =>
        new(
            values.ShowRowGrandTotals,
            values.ShowColumnGrandTotals,
            values.ShowSubtotals,
            values.SubtotalPlacement,
            values.RepeatItemLabels,
            values.BlankLineAfterItems,
            values.StyleName,
            values.ShowRowHeaders,
            values.ShowColumnHeaders,
            values.ShowRowStripes,
            values.ShowColumnStripes,
            values.ReportLayout,
            values.EmptyValueText,
            values.RefreshOnOpen,
            values.SaveSourceData,
            values.EnableRefresh,
            values.PreserveSourceSortFilter,
            values.MissingItemsLimit,
            values.PrintTitles,
            values.PrintExpandCollapseButtons,
            values.AltTextTitle,
            values.AltTextDescription,
            values.CompactRowLabelIndent,
            values.ShowExpandCollapseButtons,
            values.AutofitColumnsOnUpdate,
            values.PreserveFormattingOnUpdate,
            values.ShowFieldHeaders,
            values.ShowContextualTooltips,
            values.ShowPropertiesInTooltips,
            values.ShowClassicLayout,
            values.MergeAndCenterLabels,
            values.ShowItemsWithNoDataOnRows,
            values.ShowItemsWithNoDataOnColumns,
            values.PageOverThenDown,
            values.PageWrap,
            values.ErrorValueText,
            values.EnableDrill);

    private const string PageFieldLayoutDownThenOver = "Down, then over";
    private const string PageFieldLayoutOverThenDown = "Over, then down";
    private static readonly string[] PageFieldLayoutLabels = [PageFieldLayoutDownThenOver, PageFieldLayoutOverThenDown];

    private static bool PageFieldLayoutForLabel(string? label) =>
        string.Equals(label, PageFieldLayoutOverThenDown, StringComparison.OrdinalIgnoreCase);

    private const string MissingItemsAutomatic = "Automatic";
    private const string MissingItemsNone = "None";
    private const string MissingItemsMaximum = "Maximum";
    private static readonly string[] MissingItemsLimitLabels = [MissingItemsAutomatic, MissingItemsNone, MissingItemsMaximum];

    private static string LabelForMissingItemsLimit(int? value) =>
        PivotOptionsPlanner.NormalizeMissingItemsLimit(value) switch
        {
            null => MissingItemsAutomatic,
            <= 0 => MissingItemsNone,
            _ => MissingItemsMaximum
        };

    private static int? MissingItemsLimitForLabel(string? label) =>
        string.Equals(label, MissingItemsNone, StringComparison.OrdinalIgnoreCase)
            ? 0
            : string.Equals(label, MissingItemsMaximum, StringComparison.OrdinalIgnoreCase)
                ? PivotOptionsPlanner.MaxMissingItemsLimit
                : null;
}
