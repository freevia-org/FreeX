using System.Linq;
using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void PivotGrandTotalsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotSubtotalsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotReportLayoutBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotBlankRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                !pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotStyleGalleryBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotRowHeadersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                !pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotColumnHeadersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                !pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotBandedRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                !pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotBandedColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
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
                !pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void ApplyPivotOptions(PivotTableModel pivotTable, PivotTableOptionsDialogResult result) =>
        ApplyPivotOptions(
            pivotTable,
            result.ShowRowGrandTotals,
            result.ShowColumnGrandTotals,
            result.ShowSubtotals,
            result.SubtotalPlacement,
            result.RepeatItemLabels,
            result.BlankLineAfterItems,
            result.StyleName,
            result.ShowRowHeaders,
            result.ShowColumnHeaders,
            result.ShowRowStripes,
            result.ShowColumnStripes,
            result.ReportLayout,
            result.EmptyValueText,
            updateEmptyValueText: true,
            result.RefreshOnOpen,
            result.SaveSourceData,
            result.EnableRefresh,
            result.PreserveSourceSortFilter,
            result.MissingItemsLimit,
            true,
            result.PrintTitles,
            result.PrintExpandCollapseButtons,
            result.AltTextTitle,
            result.AltTextDescription,
            result.CompactRowLabelIndent,
            updateAltText: true,
            showExpandCollapseButtons: result.ShowExpandCollapseButtons,
            autofitColumnsOnUpdate: result.AutofitColumnsOnUpdate,
            preserveFormattingOnUpdate: result.PreserveFormattingOnUpdate,
            showFieldHeaders: result.ShowFieldHeaders,
            showContextualTooltips: result.ShowContextualTooltips,
            showPropertiesInTooltips: result.ShowPropertiesInTooltips,
            showClassicLayout: result.ShowClassicLayout,
            mergeAndCenterLabels: result.MergeAndCenterLabels,
            showItemsWithNoDataOnRows: result.ShowItemsWithNoDataOnRows,
            showItemsWithNoDataOnColumns: result.ShowItemsWithNoDataOnColumns,
            pageOverThenDown: result.PageOverThenDown,
            pageWrap: result.PageWrap);

    private void ApplyPivotOptions(
        PivotTableModel pivotTable,
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
        bool updateEmptyValueText = false,
        bool? refreshOnOpen = null,
        bool? saveSourceData = null,
        bool? enableRefresh = null,
        bool? preserveSourceSortFilter = null,
        int? missingItemsLimit = null,
        bool updateMissingItemsLimit = false,
        bool? printTitles = null,
        bool? printExpandCollapseButtons = null,
        string? altTextTitle = null,
        string? altTextDescription = null,
        int? compactRowLabelIndent = null,
        bool updateAltText = false,
        bool? showExpandCollapseButtons = null,
        bool? autofitColumnsOnUpdate = null,
        bool? preserveFormattingOnUpdate = null,
        bool? showFieldHeaders = null,
        bool? showContextualTooltips = null,
        bool? showPropertiesInTooltips = null,
        bool? showClassicLayout = null,
        bool? mergeAndCenterLabels = null,
        bool? showItemsWithNoDataOnRows = null,
        bool? showItemsWithNoDataOnColumns = null,
        bool? pageOverThenDown = null,
        int? pageWrap = null)
    {
        if (!TryExecuteCommand(
                new ConfigurePivotTableOptionsCommand(
                    _currentSheetId,
                    pivotTable.Name,
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
                    updateEmptyValueText,
                    refreshOnOpen,
                    saveSourceData,
                    enableRefresh,
                    preserveSourceSortFilter,
                    missingItemsLimit,
                    updateMissingItemsLimit,
                    printTitles,
                    printExpandCollapseButtons,
                    altTextTitle,
                    altTextDescription,
                    compactRowLabelIndent,
                    updateAltText,
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
                    pageWrap),
                "PivotTable Options"))
            return;

        UpdateViewport();
    }

    private void ShowPivotTableOptionsDialog()
    {
        if (!TryGetActivePivotTable(out _, out var pivotTable))
            return;

        var cache = _workbook.PivotCaches.FirstOrDefault(item => item.CacheId == pivotTable.CacheId);
        var dialog = new PivotTableOptionsDialog(pivotTable, cache) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPivotOptions(pivotTable, dialog.Result);
    }
}
