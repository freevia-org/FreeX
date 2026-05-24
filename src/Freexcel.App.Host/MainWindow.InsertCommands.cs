using System;
using System.Linq;
using System.Diagnostics;
using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void InsertCurrentDateOrTime(bool insertTime)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var value = insertTime
            ? DateTimeEntryService.CurrentTime(DateTime.Now)
            : DateTimeEntryService.CurrentDate(DateTime.Now);
        if (!TryExecuteRepeatableCurrentRangeCommand(
                insertTime ? "Insert Time" : "Insert Date",
                range,
                currentRange => CreateSingleCellEditCommand(currentRange.Start, Cell.FromValue(value)),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? [range.Start]);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void TableBtn_Click(object sender, RoutedEventArgs e) => ApplyTableFormat(0);

    private void RecommendedPivotTablesMenuItem_Click(object sender, RoutedEventArgs e) => PivotTableBtn_Click(sender, e);

    private void SparklineLineBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline("line");
    private void SparklineColumnBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline("column");
    private void SparklineWinLossBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline("winloss");

    private void InsertSparkline(string type)
    {
        var selected = SheetGrid.SelectedRange;
        var dialog = new SparklineDialog(
            selected?.ToString() ?? "",
            "",
            SparklineInputParser.ParseDialogKindChoice(type))
        { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!SparklineInputParser.TryParseDataRange(dialog.Result.DataRangeText, _currentSheetId, out var dataRange))
        {
            MessageBox.Show("Invalid data range.", "Insert Sparkline", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!SparklineInputParser.TryParseLocation(dialog.Result.LocationText, _currentSheetId, out var location))
        {
            MessageBox.Show("Invalid location cell.", "Insert Sparkline", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var kind = SparklineInputParser.ToModelKind(dialog.Result.Kind);

        var fallbackLocationRange = new GridRange(location, location);
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Sparkline",
                fallbackLocationRange,
                currentRange => new AddSparklineCommand(_currentSheetId, dataRange, currentRange.Start, kind)))
            return;

        SetActiveCell(location);
        EnsureCellVisible(location);
        UpdateViewport();
    }

    private void InsertLinkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } selectedRange) return;
        var prefill = HyperlinkDialogPrefill.FromCell(_workbook.GetSheet(_currentSheetId), selectedRange.Start);
        var dialog = new HyperlinkDialog(prefill.Target, prefill.DisplayText) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Link",
                selectedRange,
                currentRange => new SetHyperlinkCommand(
                    _currentSheetId,
                    currentRange.Start,
                    dialog.Result.Target,
                    dialog.Result.DisplayText,
                    new HyperlinkMetadata(
                        ToCoreHyperlinkTargetKind(dialog.Result.LinkType),
                        dialog.Result.ScreenTip,
                        dialog.Result.Bookmark))))
            return;
        UpdateViewport();
    }

    private bool TryOpenHyperlink(CellAddress address)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (!HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan) || plan is null)
            return false;

        if (plan.Kind == HyperlinkNavigationKind.WorksheetCell)
        {
            if (TryNavigateToWorkbookReference(plan.Target))
                return true;

            MessageBox.Show("The hyperlink target could not be found.", "Open Hyperlink", MessageBoxButton.OK, MessageBoxImage.Warning);
            return true;
        }

        try
        {
            Process.Start(new ProcessStartInfo(plan.Target) { UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show("The hyperlink target could not be opened.", "Open Hyperlink", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        return true;
    }

    private bool TryNavigateToWorkbookReference(string reference)
    {
        if (!TryParseWorkbookReference(reference, out var sheetName, out var row, out var col))
            return false;

        var sheet = _workbook.Sheets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, sheetName, StringComparison.OrdinalIgnoreCase));
        if (sheet is null)
            return false;

        var address = new CellAddress(sheet.Id, row, col);
        NavigateToCell(address);
        return true;
    }

    private static bool TryParseWorkbookReference(string reference, out string sheetName, out uint row, out uint col)
    {
        sheetName = "";
        row = 0;
        col = 0;

        var trimmed = reference.Trim();
        var bang = trimmed.LastIndexOf('!');
        if (bang <= 0 || bang == trimmed.Length - 1)
            return false;

        sheetName = trimmed[..bang].Trim().Trim('\'').Replace("''", "'");
        var cellText = trimmed[(bang + 1)..].Trim().TrimStart('$');
        var letterCount = cellText.TakeWhile(char.IsLetter).Count();
        if (letterCount == 0 || letterCount == cellText.Length)
            return false;

        var colText = cellText[..letterCount].Replace("$", "", StringComparison.Ordinal);
        var rowText = cellText[letterCount..].TrimStart('$');
        if (!uint.TryParse(rowText, out row) || row is < 1 or > CellAddress.MaxRow)
            return false;

        try
        {
            col = CellAddress.ColumnNameToNumber(colText);
        }
        catch
        {
            return false;
        }

        return col is >= 1 and <= CellAddress.MaxCol && sheetName.Length > 0;
    }

    private static HyperlinkTargetKind ToCoreHyperlinkTargetKind(HyperlinkLinkType linkType) =>
        linkType switch
        {
            HyperlinkLinkType.CreateNewDocument => HyperlinkTargetKind.CreateNewDocument,
            HyperlinkLinkType.PlaceInThisDocument => HyperlinkTargetKind.PlaceInThisDocument,
            HyperlinkLinkType.EmailAddress => HyperlinkTargetKind.EmailAddress,
            _ => HyperlinkTargetKind.ExistingFileOrWebPage
        };

    private void InsertCommentBtn_Click(object sender, RoutedEventArgs e) => ReviewNewThreadedCommentBtn_Click(sender, e);

    private void HeaderFooterBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var dialog = new HeaderFooterDialog(sheet) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteGroupedSheetCommand(
                "Header & Footer",
                sheetId => new SetHeaderFooterCommand(
                    sheetId,
                    dialog.Header,
                    dialog.Footer,
                    dialog.FirstPageHeader,
                    dialog.FirstPageFooter,
                    dialog.EvenPageHeader,
                    dialog.EvenPageFooter,
                    dialog.DifferentFirstPage,
                    dialog.DifferentOddEvenPages,
                    dialog.ScaleWithDocument,
                    dialog.AlignWithMargins,
                    dialog.HeaderPictures,
                    dialog.FooterPictures,
                    dialog.FirstPageHeaderPictures,
                    dialog.FirstPageFooterPictures,
                    dialog.EvenPageHeaderPictures,
                    dialog.EvenPageFooterPictures)))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private void SymbolPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SymbolPickerDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedChar == '\0') return;
        if (SheetGrid.SelectedRange is null) return;
        var selectedChar = dlg.SelectedChar;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Symbol",
                SheetGrid.SelectedRange.Value,
                currentRange =>
                {
                    var currentAddress = currentRange.Start;
                    var currentSheet = _workbook.GetSheet(_currentSheetId);
                    var currentExisting = currentSheet?.GetCell(currentAddress)?.Value as TextValue;
                    var currentText = (currentExisting?.Value ?? "") + selectedChar;
                    return CreateSingleCellEditCommand(currentAddress, Cell.FromValue(new TextValue(currentText)));
                }))
            return;
        UpdateViewport();
    }
}
