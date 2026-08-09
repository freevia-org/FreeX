using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.ThemeUI;
using FreeX.App.Services;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private PageLayoutCommandSession CreatePageLayoutCommandSession() =>
        new(CurrentGroupedEditSheetIds());

    private bool TryExecutePageLayoutCommand(PageLayoutCommandExecutionPlan plan) =>
        TryExecuteCommand(plan.Command, plan.CommandLabel);

    private void PageLayoutDeferredBtn_Click(object sender, RoutedEventArgs e)
    {
        var commandName = (sender as System.Windows.Controls.Button)?.Content?.ToString()
            ?? UiText.Get("MainWindowMessage_DeferredCommandFallbackName");
        var message = WpfResourceKeyTextResolver.Resolve(DeferredCommandMessagePlanner.WorkbookTheme(commandName));
        _messageService.ShowInfo(message.Body, message.Title);
    }

    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }

    private void ThemeOfficeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeCatalog.OfficeThemePreset.CreateTheme());

    private void ThemeColorfulMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeCatalog.FreeXColorfulThemePreset.CreateTheme());

    private void ThemeGrayscaleMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeCatalog.GrayscaleThemePreset.CreateTheme());

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
        ApplyWorkbookTheme(WorkbookThemeCatalog.OfficeColorPreset.ApplyColors(_workbook.Theme));

    private void ThemeColorsColorfulMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeCatalog.FreeXColorfulColorPreset.ApplyColors(_workbook.Theme));

    private void ThemeColorsGrayscaleMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeCatalog.GrayscaleColorPreset.ApplyColors(_workbook.Theme));

    private void ThemeColorsCustomizeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ShowWorkbookThemeDialog(WorkbookThemeDialogMode.Colors);

    private void ThemeFontsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }

    private void ThemeFontsOfficeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeCatalog.OfficeFontPreset.ApplyFonts(_workbook.Theme));

    private void ThemeFontsArialMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeCatalog.ArialFontPreset.ApplyFonts(_workbook.Theme));

    private void ThemeFontsTimesMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeCatalog.TimesNewRomanFontPreset.ApplyFonts(_workbook.Theme));

    private void ThemeFontsCustomizeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ThemeCustomizeMenuItem_Click(sender, e);

    private void ThemeEffectsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }

    private void ThemeEffectsOfficeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeCatalog.OfficeEffectPreset.ApplyEffects(_workbook.Theme));

    private void ThemeEffectsSubtleMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeCatalog.SubtleEffectPreset.ApplyEffects(_workbook.Theme));

    private void ThemeEffectsRefinedMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeCatalog.RefinedEffectPreset.ApplyEffects(_workbook.Theme));

    private void ThemeEffectsCustomizeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ShowWorkbookThemeDialog(WorkbookThemeDialogMode.Effects);

    private void ApplyWorkbookTheme(WorkbookTheme theme)
    {
        var plan = WorkbookThemeCommandPlanner.PlanApply(theme);
        if (!TryExecuteCommand(plan.Command, plan.CommandLabel))
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

        if (!TryExecutePageLayoutCommand(
                CreatePageLayoutCommandSession().PlanSetBackground(background)))
            return;

        UpdateViewport();
    }

    private void BackgroundClearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecutePageLayoutCommand(
                CreatePageLayoutCommandSession().PlanClearBackground()))
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
        ApplyPageMarginsPreset(PageLayoutMarginPreset.Normal);
    }

    private void MarginWideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyPageMarginsPreset(PageLayoutMarginPreset.Wide);
    }

    private void MarginNarrowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyPageMarginsPreset(PageLayoutMarginPreset.Narrow);
    }

    private void ApplyPageMarginsPreset(PageLayoutMarginPreset preset)
    {
        TryExecutePageLayoutCommand(
            CreatePageLayoutCommandSession().PlanMarginsPreset(preset));
    }

    private void MarginCustomMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.CustomMargins);
    }

    private void PageOrientBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void OrientPortraitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyPageOrientationPreset(PageLayoutOrientationPreset.Portrait);
    }

    private void OrientLandscapeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyPageOrientationPreset(PageLayoutOrientationPreset.Landscape);
    }

    private void ApplyPageOrientationPreset(PageLayoutOrientationPreset preset)
    {
        TryExecutePageLayoutCommand(
            CreatePageLayoutCommandSession().PlanOrientationPreset(preset));
    }

    private void PageSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void SizeLetter_Click(object sender, RoutedEventArgs e)
    {
        ApplyPagePaperSizePreset(PageLayoutPaperSizePreset.Letter);
    }

    private void SizeA4_Click(object sender, RoutedEventArgs e)
    {
        ApplyPagePaperSizePreset(PageLayoutPaperSizePreset.A4);
    }

    private void SizeLegal_Click(object sender, RoutedEventArgs e)
    {
        ApplyPagePaperSizePreset(PageLayoutPaperSizePreset.Legal);
    }

    private void ApplyPagePaperSizePreset(PageLayoutPaperSizePreset preset)
    {
        TryExecutePageLayoutCommand(
            CreatePageLayoutCommandSession().PlanPaperSizePreset(preset));
    }

    private void SizeExecutive_Click(object sender, RoutedEventArgs e) => OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.ExtendedPaperSize);
    private void SizeStatement_Click(object sender, RoutedEventArgs e) => OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.ExtendedPaperSize);
    private void SizeTabloid_Click(object sender, RoutedEventArgs e) => OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.ExtendedPaperSize);
    private void SizeA3_Click(object sender, RoutedEventArgs e) => OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.ExtendedPaperSize);
    private void SizeA5_Click(object sender, RoutedEventArgs e) => OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.ExtendedPaperSize);
    private void SizeB4_Click(object sender, RoutedEventArgs e) => ApplyPagePaperSizePreset(PageLayoutPaperSizePreset.B4);
    private void SizeB5_Click(object sender, RoutedEventArgs e) => ApplyPagePaperSizePreset(PageLayoutPaperSizePreset.B5);

    private void PrintAreaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void PrintAreaSetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecutePageLayoutCommand(
                CreatePageLayoutCommandSession().PlanSetPrintArea(range)))
            return;
        RefreshStatusBar();
    }

    private void PrintAreaClearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecutePageLayoutCommand(
                CreatePageLayoutCommandSession().PlanClearPrintArea()))
            return;
        RefreshStatusBar();
    }

    private void ScaleToFitBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.ScaleToFit);
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
        ApplyPageLayoutScaleCommit(
            CreatePageLayoutCommandSession().PlanScaleCommit(PageLayoutScaleField.Width, current, text));
    }

    private void CommitPageLayoutScaleHeightBoxText(ComboBox? combo)
    {
        if (combo is null) return;
        var current = _workbook.GetSheet(_currentSheetId)?.ScaleToFit ?? WorksheetScaleToFit.Default;
        var text = GetComboBoxText(combo);
        ApplyPageLayoutScaleCommit(
            CreatePageLayoutCommandSession().PlanScaleCommit(PageLayoutScaleField.Height, current, text));
    }

    private void CommitPageLayoutScalePercentBoxText(ComboBox? combo)
    {
        if (combo is null) return;
        var text = GetComboBoxText(combo);
        var current = _workbook.GetSheet(_currentSheetId)?.ScaleToFit ?? WorksheetScaleToFit.Default;
        ApplyPageLayoutScaleCommit(
            CreatePageLayoutCommandSession().PlanScaleCommit(PageLayoutScaleField.Percent, current, text));
    }

    private void ApplyPageLayoutScaleCommit(PageLayoutScaleCommitPlan plan)
    {
        if (!plan.ShouldApply)
        {
            SyncPageLayoutScaleToFitControls(_workbook.GetSheet(_currentSheetId));
            return;
        }

        ApplyPageLayoutScaleToFit(plan.ScaleToFit);
    }

    private void ApplyPageLayoutScaleToFit(WorksheetScaleToFit scaleToFit)
    {
        if (!TryExecutePageLayoutCommand(
                CreatePageLayoutCommandSession().PlanScaleToFit(scaleToFit)))
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

        ShowPageBreakDialog(PageBreakDialogPlanner.BuildDefaultInput(SheetGrid.SelectedRange));
    }

    private void InsertPageBreakMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyPageBreakAction(PageBreakMenuAction.Insert);
    }

    private void RemovePageBreakMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyPageBreakAction(PageBreakMenuAction.Remove);
    }

    private void ResetAllPageBreaksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyPageBreakAction(PageBreakMenuAction.ResetAll);
    }

    private void ApplyPageBreakAction(PageBreakMenuAction action)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null && action != PageBreakMenuAction.ResetAll)
            return;

        if (SheetGrid.SelectedRange is not { } selectedRange)
        {
            if (action != PageBreakMenuAction.ResetAll)
                return;

            selectedRange = new GridRange(
                new CellAddress(_currentSheetId, 1, 1),
                new CellAddress(_currentSheetId, 1, 1));
        }

        var plan = CreatePageLayoutCommandSession().PlanPageBreakAction(
            action,
            selectedRange,
            sheet?.RowPageBreaks ?? [],
            sheet?.ColumnPageBreaks ?? []);

        TryExecutePageLayoutCommand(plan);
    }

    /// <summary>
    /// Applies a page-break line drag from Page Break Preview (GridView.PageBreakLineMoved): moves
    /// the dragged manual break to <paramref name="newIndex"/>, or removes it when the user dragged
    /// it off the print area (<paramref name="newIndex"/> is null), the same way Excel does.
    /// </summary>
    private void OnPageBreakLineMoved(PageBreakLineOrientation orientation, uint originalIndex, uint? newIndex)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        TryExecutePageLayoutCommand(
            CreatePageLayoutCommandSession().PlanMovePageBreak(
                orientation == PageBreakLineOrientation.Row ? PageBreakAxis.Row : PageBreakAxis.Column,
                originalIndex,
                newIndex,
                sheet.RowPageBreaks,
                sheet.ColumnPageBreaks));
    }

    private void ShowPageBreakDialog(string defaultValue)
    {
        var dialog = new PageBreakDialog(defaultValue) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPageBreakDialogResult(dialog.Result);
    }

    private void ApplyPageBreakDialogResult(PageBreakDialogResult result)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null && result.Action != PageBreakDialogAction.Clear)
            return;

        var plan = PageBreakDialogPlanner.PlanPageBreaks(
            result,
            sheet?.RowPageBreaks ?? [],
            sheet?.ColumnPageBreaks ?? []);

        TryExecutePageLayoutCommand(
            CreatePageLayoutCommandSession().PlanPageBreaks(plan));
    }

    private void PrintTitlesBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.PrintTitles);
    }

    private void PageSetupDialogBtn_Click(object sender, RoutedEventArgs e) =>
        OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.DialogButton);

    private void OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource source) =>
        ShowPageSetupDialog(PageSetupDialogPlanner.PlanOpen(source));

    private void ShowPageSetupDialog(PageSetupDialogOpenPlan openPlan)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        PageSetupDialog? dialog = null;
        dialog = new PageSetupDialog(
            sheet,
            SheetGrid.SelectedRange,
            request => ApplyPageSetupRangeSelection(dialog, request),
            openPlan.InitialFocusTarget) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var build = CreatePageLayoutCommandSession().TryPlanPageSetup(
            sheet,
            dialog.Fields,
            dialog.RequestedAction);
        if (!build.Success)
        {
            var validation = build.Validation!;
            DialogMessageHelper.ShowWarning(
                this,
                validation.Message.Resolve(UiText.Get),
                UiText.Get(PageSetupSubmissionPlanner.DefaultCaptionResourceKey));
            return;
        }

        var plan = build.Plan!;
        if (!TryExecutePageLayoutCommand(plan.Execution))
            return;

        UpdateViewport();
        RefreshStatusBar();
        switch (plan.FollowUpAction)
        {
            case PageSetupDialogFollowUpAction.ShowPrinterOptions:
                ShowPageSetupPrinterOptions();
                break;
            case PageSetupDialogFollowUpAction.Print:
            case PageSetupDialogFollowUpAction.PrintPreview:
                PrintButton_Click(this, new RoutedEventArgs());
                break;
        }
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
        TryExecutePageLayoutCommand(
            new PageLayoutCommandSession([_currentSheetId]).PlanPrintGridlines(
                isChecked,
                sheet?.PrintHeadings ?? false));
    }

    private void PrintHeadingsChk_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        TryExecutePageLayoutCommand(
            new PageLayoutCommandSession([_currentSheetId]).PlanPrintHeadings(
                sheet?.PrintGridlines ?? false,
                isChecked));
    }

    // ── Formulas tab ──────────────────────────────────────────────────────────
}
