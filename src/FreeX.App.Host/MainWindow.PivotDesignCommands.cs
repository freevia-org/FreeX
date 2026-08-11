using System.Linq;
using System.Windows;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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
        if (TryGetActivePivotTable(out var sheet, out var pivotTable))
            ApplyPivotDesignOptions(
                sheet,
                pivotTable,
                PivotOptionsPlanner.CaptureDesignValues(pivotTable) with
                {
                    BlankLineAfterItems = !pivotTable.BlankLineAfterItems,
                });
    }

    private void PivotStyleGalleryBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotStyleGalleryDialog();
    }

    private void PivotRowHeadersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out var sheet, out var pivotTable))
            ApplyPivotDesignOptions(
                sheet,
                pivotTable,
                PivotOptionsPlanner.CaptureDesignValues(pivotTable) with
                {
                    ShowRowHeaders = !pivotTable.ShowRowHeaders,
                });
    }

    private void PivotColumnHeadersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out var sheet, out var pivotTable))
            ApplyPivotDesignOptions(
                sheet,
                pivotTable,
                PivotOptionsPlanner.CaptureDesignValues(pivotTable) with
                {
                    ShowColumnHeaders = !pivotTable.ShowColumnHeaders,
                });
    }

    private void PivotBandedRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out var sheet, out var pivotTable))
            ApplyPivotDesignOptions(
                sheet,
                pivotTable,
                PivotOptionsPlanner.CaptureDesignValues(pivotTable) with
                {
                    ShowRowStripes = !pivotTable.ShowRowStripes,
                });
    }

    private void PivotBandedColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out var sheet, out var pivotTable))
            ApplyPivotDesignOptions(
                sheet,
                pivotTable,
                PivotOptionsPlanner.CaptureDesignValues(pivotTable) with
                {
                    ShowColumnStripes = !pivotTable.ShowColumnStripes,
                });
    }

    private void PivotExpandCollapseButtonsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out var sheet, out var pivotTable))
            ApplyPivotDesignOptions(
                sheet,
                pivotTable,
                PivotOptionsPlanner.CaptureDesignValues(pivotTable),
                showExpandCollapseButtons: !pivotTable.ShowExpandCollapseButtons);
    }

    private void PivotFieldHeadersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out var sheet, out var pivotTable))
            ApplyPivotDesignOptions(
                sheet,
                pivotTable,
                PivotOptionsPlanner.CaptureDesignValues(pivotTable) with
                {
                    ShowFieldHeaders = !pivotTable.ShowFieldHeaders,
                });
    }

    private void ApplyPivotOptions(PivotTableModel pivotTable, PivotOptionsDialogValues values)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        ApplyPivotApplicationPlan(
            PivotApplication.PlanDialogOptions(
                new PivotApplicationTarget(sheet, pivotTable),
                values),
            "PivotTable Options");
    }

    private void ApplyPivotDesignOptions(
        Sheet sheet,
        PivotTableModel pivotTable,
        PivotDesignOptionsValues values,
        bool? showExpandCollapseButtons = null) =>
        ApplyPivotApplicationPlan(
            PivotApplication.PlanDesignOptions(
                new PivotApplicationTarget(sheet, pivotTable),
                values,
                showExpandCollapseButtons),
            "PivotTable Options");

    private void ShowPivotTableOptionsDialog()
    {
        if (!TryGetActivePivotTable(out _, out var pivotTable))
            return;

        ShowPivotTableOptionsDialog(pivotTable);
    }

    private void ShowPivotTableOptionsDialog(CellAddress address)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var pivotTable = PivotUiPlanner.FindPivotTableContainingCell(sheet, address);
        if (pivotTable is null)
            return;

        ShowPivotTableOptionsDialog(pivotTable);
    }

    private void ShowPivotTableOptionsDialog(PivotTableModel pivotTable)
    {
        PivotCacheModel? cache = null;
        foreach (var item in _workbook.PivotCaches)
        {
            if (item.CacheId != pivotTable.CacheId)
                continue;

            cache = item;
            break;
        }

        var dialog = new PivotTableOptionsDialog(pivotTable, cache) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPivotOptions(pivotTable, dialog.Result);
    }

    private void ShowPivotStyleGalleryDialog()
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var dialog = new PivotStyleGalleryDialog(pivotTable.StyleName) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPivotDesignOptions(
            sheet,
            pivotTable,
            PivotOptionsPlanner.CaptureDesignValues(pivotTable) with
            {
                StyleName = dialog.Result.StyleName,
            });
    }
}
