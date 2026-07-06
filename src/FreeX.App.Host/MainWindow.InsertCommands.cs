using System;
using System.Linq;
using System.Windows;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.SparklineUI;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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

    private void PicturesBtn_Click(object sender, RoutedEventArgs e) => InsertPictureBtn_Click(sender, e);

    private void ShapesBtn_Click(object sender, RoutedEventArgs e) => DrawRectBtn_Click(sender, e);

    private void SparklineLineBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline("line");
    private void SparklineColumnBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline("column");
    private void SparklineWinLossBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline("winloss");

    private void InsertSparkline(string type)
    {
        var selected = SheetGrid.SelectedRange;
        SparklineDialog? dialog = null;
        dialog = new SparklineDialog(
            selected?.ToString() ?? "",
            "",
            SparklinePlanner.ParseKind(type),
            request => ApplySparklineRangeSelection(dialog, request),
            sheetId: _currentSheetId)
        { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        // The Location field accepts either a single cell (one sparkline) or a multi-row/column range
        // that expands into a sparkline group, matching Excel's "Insert Sparklines" dialog.
        var validation = SparklinePlanner.ValidateInsertGroup(
            dialog.Result.DataRangeText,
            dialog.Result.LocationText,
            _currentSheetId,
            out var members);
        if (validation == SparklineInputValidation.InvalidDataRange)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_InsertSparklineInvalidDataRange"),
                UiText.Get("MainWindowMessage_InsertSparklineTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (validation == SparklineInputValidation.InvalidLocation || members.Count == 0)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_InsertSparklineInvalidLocation"),
                UiText.Get("MainWindowMessage_InsertSparklineTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var kind = dialog.Result.Kind;
        var firstLocation = members[0].Location;

        var fallbackLocationRange = new GridRange(firstLocation, firstLocation);
        var useDialogLocationForInitialInsert = true;
        IWorkbookCommand CreateCommand()
        {
            // Every member of the group must share one nonzero GroupId so the group survives an
            // XLSX round-trip as a single <x14:sparklineGroup>; a lone member is simplest left
            // ungrouped (GroupId 0), matching an independently-inserted sparkline.
            if (members.Count == 1)
            {
                var currentRange = useDialogLocationForInitialInsert
                    ? fallbackLocationRange
                    : SheetGrid.SelectedRange ?? fallbackLocationRange;
                return new AddSparklineCommand(_currentSheetId, members[0].DataRange, currentRange.Start, kind);
            }

            var sheet = _workbook.GetSheet(_currentSheetId);
            if (sheet is null)
                return new AddSparklineCommand(_currentSheetId, members[0].DataRange, members[0].Location, kind);
            var groupId = SparklineGroupIdAllocator.NextGroupId(sheet.Sparklines);
            var commands = members
                .Select(member => (IWorkbookCommand)new AddSparklineCommand(
                    _currentSheetId, member.DataRange, member.Location, kind, groupId))
                .ToList();
            return new CompositeWorkbookCommand("Insert Sparkline", commands);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        useDialogLocationForInitialInsert = false;
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Insert Sparkline");
            return;
        }

        MarkWorkbookDirty();
        _repeatPostAction = null;
        InvalidateNavigationCaches();

        SetActiveCell(firstLocation);
        EnsureCellVisible(firstLocation);
        UpdateViewport();
    }

    private void ApplySparklineRangeSelection(
        SparklineDialog? dialog,
        SparklineRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange =>
            {
                var rangeText = request.Target == SparklineRangeSelectionTarget.Location
                    ? FormatCellReference(selectedRange.Start)
                    : FormatWorkbookRange(selectedRange);
                dialog.ApplyRangeSelection(request.Target, rangeText);
            });
    }

    private void InsertLinkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } selectedRange) return;
        var prefill = HyperlinkDialogPrefill.FromCell(_workbook.GetSheet(_currentSheetId), selectedRange.Start);
        var dialog = new HyperlinkDialog(prefill.Target, prefill.DisplayText) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Insert Link",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? selectedRange;
                    var address = GroupedSheetRangePlanner.RemapRangeToSheet(currentRange, sheetId).Start;
                    return new SetHyperlinkCommand(
                        sheetId,
                        address,
                        dialog.Result.Target,
                        dialog.Result.DisplayText,
                        new HyperlinkMetadata(
                            ToCoreHyperlinkTargetKind(dialog.Result.LinkType),
                            dialog.Result.ScreenTip,
                            dialog.Result.Bookmark));
                }))
            return;
        UpdateViewport();
    }

    private bool TryOpenHyperlink(CellAddress address)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (!HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, _currentFilePath, out var plan) || plan is null)
            return false;

        if (plan.Kind == HyperlinkNavigationKind.WorksheetCell)
        {
            if (TryNavigateToWorkbookReference(plan.Target))
                return true;

            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_OpenHyperlinkTargetNotFound"),
                UiText.Get("MainWindowMessage_OpenHyperlinkTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return true;
        }

        if (plan.Kind == HyperlinkNavigationKind.LocalFile)
        {
            OpenLocalFileHyperlink(plan);
            return true;
        }

        switch (ExternalUrlLauncher.Open(plan.Target))
        {
            case ExternalUrlLaunchResult.BlockedScheme:
                ShowOwnedMessage(
                    UiText.Get("MainWindowMessage_OpenHyperlinkBlockedScheme"),
                    UiText.Get("MainWindowMessage_OpenHyperlinkTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                break;
            case ExternalUrlLaunchResult.LaunchFailed:
                ShowOwnedMessage(
                    UiText.Get("MainWindowMessage_OpenHyperlinkOpenFailed"),
                    UiText.Get("MainWindowMessage_OpenHyperlinkTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                break;
        }

        return true;
    }

    private void OpenLocalFileHyperlink(HyperlinkNavigationPlan plan)
    {
        // The shared planner already resolved the (absolute) local path; only open it when it
        // maps to a supported workbook adapter, mirroring the cross-platform port's guard.
        if (string.IsNullOrWhiteSpace(plan.LocalPath) ||
            WorkbookOpenIngressPlanner.SelectOpenableFile(new[] { plan.LocalPath }, _fileAdapters) is not { } openablePath)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_OpenHyperlinkOpenFailed"),
                UiText.Get("MainWindowMessage_OpenHyperlinkTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _ = OpenStartupFileAsync(openablePath);
    }

    private bool TryOpenSelectedHyperlink()
    {
        if (SheetGrid.SelectedRange is not { } selectedRange)
            return false;

        return TryOpenHyperlink(selectedRange.Start);
    }

    private bool TryNavigateToWorkbookReference(string reference)
    {
        if (!TryParseWorkbookReference(reference, out var sheetName, out var row, out var col))
            return false;

        Sheet? sheet = null;
        foreach (var candidate in _workbook.Sheets)
        {
            if (!string.Equals(candidate.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                continue;

            sheet = candidate;
            break;
        }

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
                sheetId => PageSetupCommandFactory.BuildHeaderFooterCommand(
                    sheetId,
                    new PageSetupHeaderFooterRequest
                    {
                        Header = dialog.Header,
                        Footer = dialog.Footer,
                        FirstPageHeader = dialog.FirstPageHeader,
                        FirstPageFooter = dialog.FirstPageFooter,
                        EvenPageHeader = dialog.EvenPageHeader,
                        EvenPageFooter = dialog.EvenPageFooter,
                        DifferentFirstPage = dialog.DifferentFirstPage,
                        DifferentOddEvenPages = dialog.DifferentOddEvenPages,
                        ScaleHeaderFooterWithDocument = dialog.ScaleWithDocument,
                        AlignHeaderFooterWithMargins = dialog.AlignWithMargins,
                        HeaderPictures = dialog.HeaderPictures,
                        FooterPictures = dialog.FooterPictures,
                        FirstPageHeaderPictures = dialog.FirstPageHeaderPictures,
                        FirstPageFooterPictures = dialog.FirstPageFooterPictures,
                        EvenPageHeaderPictures = dialog.EvenPageHeaderPictures,
                        EvenPageFooterPictures = dialog.EvenPageFooterPictures
                    })))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private void SymbolPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SymbolPickerDialog { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.SelectedSymbol)) return;
        if (SheetGrid.SelectedRange is null) return;
        var selectedSymbol = dlg.SelectedSymbol;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Symbol",
                SheetGrid.SelectedRange.Value,
                currentRange =>
                {
                    var currentAddress = currentRange.Start;
                    var currentSheet = _workbook.GetSheet(_currentSheetId);
                    var currentExisting = currentSheet?.GetCell(currentAddress)?.Value as TextValue;
                    var currentText = (currentExisting?.Value ?? "") + selectedSymbol;
                    return CreateSingleCellEditCommand(currentAddress, Cell.FromValue(new TextValue(currentText)));
                }))
            return;
        UpdateViewport();
    }
}
