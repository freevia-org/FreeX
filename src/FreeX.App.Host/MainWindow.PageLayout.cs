using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void PageLayoutDeferredBtn_Click(object sender, RoutedEventArgs e)
    {
        var commandName = (sender as System.Windows.Controls.Button)?.Content?.ToString()
            ?? UiText.Get("MainWindowMessage_DeferredCommandFallbackName");
        var message = DeferredCommandMessages.WorkbookTheme(commandName);
        _messageService.ShowInfo(message.Body, message.Title);
    }

    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }

    private void ThemeOfficeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookTheme.Office);

    private void ThemeColorfulMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeWorkflow.CreateColorfulTheme());

    private void ThemeGrayscaleMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeWorkflow.CreateGrayscaleTheme());

    private void ThemeCustomizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowWorkbookThemeDialog(WorkbookThemeDialogMode.Theme);
    }

    private void ShowWorkbookThemeDialog(WorkbookThemeDialogMode mode)
    {
        var dialog = new WorkbookThemeDialog(_workbook.Theme, mode) { Owner = this };
        if (dialog.ShowDialog() == true)
            ApplyWorkbookTheme(dialog.ResultTheme);
    }

    private void ThemeColorsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }

    private void ThemeColorsOfficeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeWorkflow.ApplyOfficeColors(_workbook.Theme).WithName(_workbook.Theme.Name));

    private void ThemeColorsColorfulMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeWorkflow.ApplyColorfulColors(_workbook.Theme).WithName(_workbook.Theme.Name));

    private void ThemeColorsGrayscaleMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeWorkflow.ApplyGrayscaleColors(_workbook.Theme).WithName(_workbook.Theme.Name));

    private void ThemeColorsCustomizeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ShowWorkbookThemeDialog(WorkbookThemeDialogMode.Colors);

    private void ThemeFontsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }

    private void ThemeFontsOfficeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(_workbook.Theme.WithFonts(WorkbookTheme.Office.MajorFontName, WorkbookTheme.Office.MinorFontName));

    private void ThemeFontsArialMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(_workbook.Theme.WithFonts("Arial", "Arial"));

    private void ThemeFontsTimesMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(_workbook.Theme.WithFonts("Times New Roman", "Times New Roman"));

    private void ThemeFontsCustomizeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ThemeCustomizeMenuItem_Click(sender, e);

    private void ThemeEffectsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }

    private void ThemeEffectsOfficeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(_workbook.Theme.WithEffects(WorkbookTheme.Office.EffectsName));

    private void ThemeEffectsSubtleMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(_workbook.Theme.WithEffects("Subtle"));

    private void ThemeEffectsRefinedMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(_workbook.Theme.WithEffects("Refined"));

    private void ThemeEffectsCustomizeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ShowWorkbookThemeDialog(WorkbookThemeDialogMode.Effects);

    private void ApplyWorkbookTheme(WorkbookTheme theme)
    {
        if (!TryExecuteCommand(new SetWorkbookThemeCommand(theme), "Themes"))
            return;

        UpdateViewport();
    }

    private void BackgroundBtn_Click(object sender, RoutedEventArgs e)
    {
        BackgroundChooseMenuItem_Click(sender, e);
    }

    private async void BackgroundChooseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var openPlan = SheetBackgroundPickerPlanner.BuildOpenDialogPlan();
        var result = WpfFileDialogService.ShowOpenDialog(
            this,
            UiText.Get("MainWindowDialog_ImageFilesFilter"),
            checkFileExists: openPlan.CheckFileExists,
            multiselect: openPlan.Multiselect,
            title: UiText.Get("MainWindowDialog_SheetBackgroundTitle"));

        if (!result.Chosen)
            return;

        if (!SheetBackgroundPickerPlanner.IsSupportedImagePath(result.FileName!))
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_SheetBackgroundUnsupportedImageType"),
                UiText.Get("MainWindowMessage_SheetBackgroundTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(result.FileName!);
        }
        catch (IOException ex)
        {
            ShowOwnedMessage(
                UiText.Format("MainWindowMessage_SheetBackgroundReadFailed", ex.Message),
                UiText.Get("MainWindowMessage_SheetBackgroundTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowOwnedMessage(
                UiText.Format("MainWindowMessage_SheetBackgroundReadFailed", ex.Message),
                UiText.Get("MainWindowMessage_SheetBackgroundTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!SheetBackgroundPickerPlanner.TryBuildBackgroundImage(bytes, result.FileName!, out var background)
            || background is null)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_SheetBackgroundUnsupportedImageType"),
                UiText.Get("MainWindowMessage_SheetBackgroundTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteGroupedSheetCommand("Sheet Background", sheetId => new SetWorksheetBackgroundCommand(sheetId, background)))
            return;

        UpdateViewport();
    }

    private void BackgroundClearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteGroupedSheetCommand("Clear Sheet Background", sheetId => new ClearWorksheetBackgroundCommand(sheetId)))
            return;

        UpdateViewport();
    }

    private void PageMarginsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void MarginNormalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand(
            "Page Margins",
            sheetId => PageLayoutRibbonCommandPlanner.BuildMarginsCommand(sheetId, WorksheetPageMargins.Normal));
    }

    private void MarginWideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand(
            "Page Margins",
            sheetId => PageLayoutRibbonCommandPlanner.BuildMarginsCommand(sheetId, WorksheetPageMargins.Wide));
    }

    private void MarginNarrowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand(
            "Page Margins",
            sheetId => PageLayoutRibbonCommandPlanner.BuildMarginsCommand(sheetId, WorksheetPageMargins.Narrow));
    }

    private void MarginCustomMenuItem_Click(object sender, RoutedEventArgs e)
    {
        PageSetupDialogBtn_Click(sender, e);
    }

    private void PageOrientBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void OrientPortraitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand(
            "Orientation",
            sheetId => PageLayoutRibbonCommandPlanner.BuildOrientationCommand(sheetId, WorksheetPageOrientation.Portrait));
    }

    private void OrientLandscapeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand(
            "Orientation",
            sheetId => PageLayoutRibbonCommandPlanner.BuildOrientationCommand(sheetId, WorksheetPageOrientation.Landscape));
    }

    private void PageSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void SizeLetter_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand(
            "Paper Size",
            sheetId => PageLayoutRibbonCommandPlanner.BuildPaperSizeCommand(sheetId, WorksheetPaperSize.Letter));
    }

    private void SizeA4_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand(
            "Paper Size",
            sheetId => PageLayoutRibbonCommandPlanner.BuildPaperSizeCommand(sheetId, WorksheetPaperSize.A4));
    }

    private void SizeLegal_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand(
            "Paper Size",
            sheetId => PageLayoutRibbonCommandPlanner.BuildPaperSizeCommand(sheetId, WorksheetPaperSize.Legal));
    }

    private void SizeExecutive_Click(object sender, RoutedEventArgs e) => ShowPageSetupDialog(PageSetupInitialFocusTarget.PageOrientation);
    private void SizeStatement_Click(object sender, RoutedEventArgs e) => ShowPageSetupDialog(PageSetupInitialFocusTarget.PageOrientation);
    private void SizeTabloid_Click(object sender, RoutedEventArgs e) => ShowPageSetupDialog(PageSetupInitialFocusTarget.PageOrientation);
    private void SizeA3_Click(object sender, RoutedEventArgs e) => ShowPageSetupDialog(PageSetupInitialFocusTarget.PageOrientation);
    private void SizeA5_Click(object sender, RoutedEventArgs e) => ShowPageSetupDialog(PageSetupInitialFocusTarget.PageOrientation);
    private void SizeB4_Click(object sender, RoutedEventArgs e) => ShowPageSetupDialog(PageSetupInitialFocusTarget.PageOrientation);
    private void SizeB5_Click(object sender, RoutedEventArgs e) => ShowPageSetupDialog(PageSetupInitialFocusTarget.PageOrientation);

    private void PrintAreaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void PrintAreaSetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand(
                "Print Area",
                sheetId => PageLayoutRibbonCommandPlanner.BuildSetPrintAreaCommand(sheetId, range)))
            return;
        RefreshStatusBar();
    }

    private void PrintAreaClearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteGroupedSheetCommand(
                "Print Area",
                sheetId => PageLayoutRibbonCommandPlanner.BuildClearPrintAreaCommand(sheetId)))
            return;
        RefreshStatusBar();
    }

    private void ScaleToFitBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPageSetupDialog(PageSetupInitialFocusTarget.ScaleToFit);
    }

    private void InitializePageLayoutScaleToFitControls()
    {
        // The visible Scale-to-Fit combos are the *rendered* declarative controls; they are populated
        // and wired by PopulateAndWireRenderedPageLayoutCombos (called from TryApplyDeclarativeRibbon).
        // This runs on Loaded *before* the ribbon swap, so it just primes the value display once the
        // rendered combos exist (the swap re-populates and re-syncs them anyway).
        SyncPageLayoutScaleToFitControls(_workbook.GetSheet(_currentSheetId));
    }

    private void SyncPageLayoutScaleToFitControls(Sheet? sheet)
    {
        var scaleToFit = sheet?.ScaleToFit ?? WorksheetScaleToFit.Default;
        _suppressToolbarSync = true;
        try
        {
            if (FindRenderedRibbonControl("Scale Width") is ComboBox widthBox)
                SetComboBoxTextIfChanged(widthBox, PageLayoutInputParser.FormatScalePages(scaleToFit.FitToPagesWide));
            if (FindRenderedRibbonControl("Scale Height") is ComboBox heightBox)
                SetComboBoxTextIfChanged(heightBox, PageLayoutInputParser.FormatScalePages(scaleToFit.FitToPagesTall));
            if (FindRenderedRibbonControl("Scale Percent") is ComboBox percentBox)
                SetComboBoxTextIfChanged(percentBox, PageLayoutInputParser.FormatScalePercent(scaleToFit.ScalePercent));
        }
        finally
        {
            _suppressToolbarSync = false;
        }
    }

    private void PageLayoutScaleWidthBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync || (sender as ComboBox)?.SelectedItem is null) return;
        CommitPageLayoutScaleWidthBoxText(sender as ComboBox);
    }

    private void PageLayoutScaleWidthBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _suppressToolbarSync) return;
        CommitPageLayoutScaleWidthBoxText(sender as ComboBox);
        e.Handled = true;
    }

    private void PageLayoutScaleWidthBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        CommitPageLayoutScaleWidthBoxText(sender as ComboBox);
    }

    private void PageLayoutScaleHeightBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync || (sender as ComboBox)?.SelectedItem is null) return;
        CommitPageLayoutScaleHeightBoxText(sender as ComboBox);
    }

    private void PageLayoutScaleHeightBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _suppressToolbarSync) return;
        CommitPageLayoutScaleHeightBoxText(sender as ComboBox);
        e.Handled = true;
    }

    private void PageLayoutScaleHeightBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        CommitPageLayoutScaleHeightBoxText(sender as ComboBox);
    }

    private void PageLayoutScalePercentBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync || (sender as ComboBox)?.SelectedItem is null) return;
        CommitPageLayoutScalePercentBoxText(sender as ComboBox);
    }

    private void PageLayoutScalePercentBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _suppressToolbarSync) return;
        CommitPageLayoutScalePercentBoxText(sender as ComboBox);
        e.Handled = true;
    }

    private void PageLayoutScalePercentBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        CommitPageLayoutScalePercentBoxText(sender as ComboBox);
    }

    private void CommitPageLayoutScaleWidthBoxText(ComboBox? combo)
    {
        if (combo is null) return;
        var current = _workbook.GetSheet(_currentSheetId)?.ScaleToFit ?? WorksheetScaleToFit.Default;
        var text = GetComboBoxText(combo);
        if (!PageLayoutInputParser.TryParseScalePages(text, out var wide))
        {
            SyncPageLayoutScaleToFitControls(_workbook.GetSheet(_currentSheetId));
            return;
        }

        ApplyPageLayoutScaleToFit(
            PageLayoutRibbonCommandPlanner.ResolveScaleToFitFromPageDimensions(current, wide, current.FitToPagesTall));
    }

    private void CommitPageLayoutScaleHeightBoxText(ComboBox? combo)
    {
        if (combo is null) return;
        var current = _workbook.GetSheet(_currentSheetId)?.ScaleToFit ?? WorksheetScaleToFit.Default;
        var text = GetComboBoxText(combo);
        if (!PageLayoutInputParser.TryParseScalePages(text, out var tall))
        {
            SyncPageLayoutScaleToFitControls(_workbook.GetSheet(_currentSheetId));
            return;
        }

        ApplyPageLayoutScaleToFit(
            PageLayoutRibbonCommandPlanner.ResolveScaleToFitFromPageDimensions(current, current.FitToPagesWide, tall));
    }

    private void CommitPageLayoutScalePercentBoxText(ComboBox? combo)
    {
        if (combo is null) return;
        var text = GetComboBoxText(combo);
        if (!PageLayoutInputParser.TryParseScalePercent(text, out var percent))
        {
            SyncPageLayoutScaleToFitControls(_workbook.GetSheet(_currentSheetId));
            return;
        }

        ApplyPageLayoutScaleToFit(PageLayoutRibbonCommandPlanner.ResolveScalePercent(percent));
    }

    private void ApplyPageLayoutScaleToFit(WorksheetScaleToFit scaleToFit)
    {
        if (!TryExecuteGroupedSheetCommand(
                "Scale To Fit",
                sheetId => PageLayoutRibbonCommandPlanner.BuildScaleToFitCommand(sheetId, scaleToFit)))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private static string GetComboBoxText(ComboBox comboBox) =>
        comboBox.SelectedItem?.ToString() ?? comboBox.Text ?? "";

    private static void SetComboBoxTextIfChanged(ComboBox comboBox, string text)
    {
        if (comboBox.Items.Contains(text))
        {
            if (!Equals(comboBox.SelectedItem, text))
                comboBox.SelectedItem = text;
            return;
        }

        if (string.Equals(comboBox.Text, text, StringComparison.Ordinal))
            return;

        comboBox.SelectedItem = null;
        comboBox.Text = text;
    }

    private void PageBreaksBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        {
            OpenRibbonContextMenu(btn, cm);
            return;
        }

        ShowPageBreakDialog(GetDefaultPageBreakDialogValue());
    }

    private void InsertPageBreakMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        if (SheetGrid.SelectedRange is not { } selectedRange) return;

        var plan = PageLayoutRibbonCommandPlanner.PlanInsertPageBreaks(
            selectedRange,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);
        TryExecuteGroupedSheetCommand(
            "Page Breaks",
            sheetId => PageLayoutRibbonCommandPlanner.BuildPageBreaksCommand(sheetId, plan));
    }

    private void RemovePageBreakMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        if (SheetGrid.SelectedRange is not { } selectedRange) return;

        var plan = PageLayoutRibbonCommandPlanner.PlanRemovePageBreaks(
            selectedRange,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);
        TryExecuteGroupedSheetCommand(
            "Page Breaks",
            sheetId => PageLayoutRibbonCommandPlanner.BuildPageBreaksCommand(sheetId, plan));
    }

    private void ResetAllPageBreaksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var plan = PageLayoutRibbonCommandPlanner.PlanResetPageBreaks();
        TryExecuteGroupedSheetCommand(
            "Page Breaks",
            sheetId => PageLayoutRibbonCommandPlanner.BuildPageBreaksCommand(sheetId, plan));
    }

    private void ShowPageBreakDialog(string defaultValue)
    {
        var dialog = new PageBreakDialog(defaultValue) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPageBreakDialogResult(dialog.Result);
    }

    private string GetDefaultPageBreakDialogValue()
    {
        if (SheetGrid.SelectedRange is not { } selectedRange)
            return "row 2";

        if (SelectionRangeService.IsWholeColumnSelection(selectedRange))
            return $"column {selectedRange.Start.Col.ToString(CultureInfo.InvariantCulture)}";

        return $"row {selectedRange.Start.Row.ToString(CultureInfo.InvariantCulture)}";
    }

    private void ApplyPageBreakDialogResult(PageBreakDialogResult result)
    {
        if (result.Action == PageBreakDialogAction.Clear)
        {
            var resetPlan = PageLayoutRibbonCommandPlanner.PlanResetPageBreaks();
            TryExecuteGroupedSheetCommand(
                "Page Breaks",
                sheetId => PageLayoutRibbonCommandPlanner.BuildPageBreaksCommand(sheetId, resetPlan));
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var rowBreaks = sheet.RowPageBreaks.ToList();
        var columnBreaks = sheet.ColumnPageBreaks.ToList();

        if (result.RowBreak is { } rowBreak && !rowBreaks.Contains(rowBreak))
            rowBreaks.Add(rowBreak);
        if (result.ColumnBreak is { } columnBreak && !columnBreaks.Contains(columnBreak))
            columnBreaks.Add(columnBreak);

        TryExecuteGroupedSheetCommand(
            "Page Breaks",
            sheetId => PageLayoutRibbonCommandPlanner.BuildPageBreaksCommand(sheetId, rowBreaks, columnBreaks));
    }

    private void PrintTitlesBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        ShowPageSetupDialog(PageSetupInitialFocusTarget.RepeatRows);
    }

    private void PageSetupDialogBtn_Click(object sender, RoutedEventArgs e) =>
        ShowPageSetupDialog(PageSetupInitialFocusTarget.PageOrientation);

    private void ShowPageSetupDialog(PageSetupInitialFocusTarget initialFocusTarget)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        PageSetupDialog? dialog = null;
        dialog = new PageSetupDialog(
            sheet,
            SheetGrid.SelectedRange,
            request => ApplyPageSetupRangeSelection(dialog, request),
            initialFocusTarget) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var fields = dialog.Fields;
        var submission = PageSetupSubmissionPlanner.TryBuild(sheet, fields, dialog.RequestedAction);
        if (!submission.Success)
        {
            var validation = submission.Validation!;
            DialogMessageHelper.ShowWarning(
                this,
                validation.Message.Resolve(UiText.Get),
                UiText.Get(PageSetupSubmissionPlanner.DefaultCaptionResourceKey));
            return;
        }

        var targetCommandBuilds = CurrentGroupedEditSheetIds()
            .Select(sheetId => submission.Submission!.TryBuildCompositeCommandForTarget(sheet, sheetId))
            .ToList();
        var invalidTargetCommand = targetCommandBuilds.FirstOrDefault(build => !build.Success);
        if (invalidTargetCommand is not null)
        {
            var validation = invalidTargetCommand.Validation!;
            DialogMessageHelper.ShowWarning(
                this,
                validation.Message.Resolve(UiText.Get),
                UiText.Get(PageSetupSubmissionPlanner.DefaultCaptionResourceKey));
            return;
        }

        var command = targetCommandBuilds.Count > 1
            ? new CompositeWorkbookCommand("Page Setup", targetCommandBuilds.Select(build => build.Command!).ToList())
            : targetCommandBuilds[0].Command!;
        if (!TryExecuteCommand(command, "Page Setup"))
            return;

        UpdateViewport();
        RefreshStatusBar();
        if (submission.Submission!.RequestedAction == PageSetupDialogAction.Options)
        {
            ShowPageSetupPrinterOptions();
            return;
        }

        if (submission.Submission.RequestedAction is PageSetupDialogAction.Print or PageSetupDialogAction.PrintPreview)
            PrintButton_Click(this, new RoutedEventArgs());
    }

    private void ApplyPageSetupRangeSelection(PageSetupDialog? dialog, PageSetupRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange =>
            {
                var rangeText = PageSetupRangeSelectionFormatter.Format(
                    request.Target,
                    selectedRange,
                    _options.UseR1C1ReferenceStyle);
                dialog.ApplyRangeSelection(request.Target, rangeText);
            });
    }

    private void ShowPageSetupPrinterOptions()
    {
        NativePrintDialogService.ShowPrinterOptionsDialog(this);
    }

    private void PrintGridlinesChk_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        TryExecuteCommand(
            PageLayoutRibbonCommandPlanner.BuildPrintGridlinesCommand(_currentSheetId, isChecked, sheet?.PrintHeadings ?? false),
            "Print Gridlines");
    }

    private void PrintHeadingsChk_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        TryExecuteCommand(
            PageLayoutRibbonCommandPlanner.BuildPrintHeadingsCommand(_currentSheetId, sheet?.PrintGridlines ?? false, isChecked),
            "Print Headings");
    }

    // ── Formulas tab ──────────────────────────────────────────────────────────
}
