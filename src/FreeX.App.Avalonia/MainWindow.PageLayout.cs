using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.PageLayout;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Free.Shared.Shell.Avalonia;

using AvaloniaControlShapesLine = Avalonia.Controls.Shapes.Line;
using AvaloniaDock = Avalonia.Controls.Dock;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Page Setup dialog + Page Break Preview overlay for the Avalonia shell (realignment R7).
///
/// Page Setup edits the active sheet's portable page-setup model — orientation, paper size, margins,
/// scaling (adjust-to percent OR fit-to W×H), print area, and print titles (rows/columns to repeat) —
/// through <see cref="PageSetupDialogModel"/> and persists it via the workbook session command pipeline
/// (<see cref="SetPageSetupCommand"/> for the page setup, plus <see cref="SetPrintAreaCommand"/> /
/// <see cref="ClearPrintAreaCommand"/> for the print area, all undoable).
///
/// Page Break Preview is a grid view-mode that overlays page boundaries, the dimmed out-of-print-area
/// masks, automatic break lines, and "Page N" watermarks computed by
/// <see cref="PageBreakPreviewLayoutPlanner"/>, flattened to draw instructions by
/// <see cref="PageBreakPreviewInstructionBuilder"/>. It composites onto the rendered grid so it scrolls
/// with content.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly IBrush PageBreakMaskFill = Brush(120, 96, 110, 114);
    private static readonly IBrush PageBreakBorderBrush = Brush(11, 112, 116);
    private static readonly IBrush PageBreakLineBrush = Brush(11, 112, 116);
    private static readonly IBrush PageBreakWatermarkBrush = Brush(60, 11, 112, 116);
    private static AvaloniaCompactDialogChromeStyle PageLayoutDialogChromeStyle => new(FormulaBarFontFamily);

    private async Task ShowPageSetupDialogAsync(
        PageLayoutPageSetupOpenSource source = PageLayoutPageSetupOpenSource.DialogButton,
        bool openHeaderFooterTab = false)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var sheet = _session.ActiveSheet;
        var fields = await ShowPageSetupDialogCoreAsync(
            PageSetupDialogPlanner.PlanSurface(sheet),
            PageSetupDialogPlanner.PlanOpen(source),
            openHeaderFooterTab);
        if (fields is null)
            return;

        ApplyPageSetupFields(sheet, fields);
    }

    private Task ShowHeaderFooterDialogAsync() =>
        ShowPageSetupDialogAsync(openHeaderFooterTab: true);

    private void ApplyPageSetupFields(Sheet sheet, PageSetupDialogFields fields)
    {
        var submission = PageSetupSubmissionPlanner.TryBuild(sheet, fields);
        if (!submission.Success)
        {
            ShowEditIssue(PageLayoutStatusPlanner.ResolvePageSetupValidationIssue(submission.Validation!, UiText.Get));
            return;
        }

        var commandBuild = submission.Submission!.TryBuildCompositeCommandForTarget(sheet, sheet.Id);
        if (!commandBuild.Success)
        {
            ShowEditIssue(PageLayoutStatusPlanner.ResolvePageSetupValidationIssue(commandBuild.Validation!, UiText.Get));
            return;
        }

        var result = _session.ExecuteReviewCommand(commandBuild.Command!);
        var status = PageLayoutStatusPlanner.ResolveCommandStatus(
            PageLayoutStatusPlanner.PageSetupSubmission,
            result.Success,
            result.ErrorMessage,
            UiText.Get);
        if (!result.Success)
        {
            ShowEditIssue(status);
            return;
        }

        RefreshShell(status);
    }

    private void TogglePageBreakPreview()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        var plan = PageLayoutStatusPlanner.PlanPageBreakPreviewToggle(_session.ActiveSheet.ViewMode);
        var result = _session.SetWorksheetViewMode(plan.TargetViewMode);
        var status = PageLayoutStatusPlanner.ResolveCommandStatus(
            plan.Status,
            result.Success,
            result.ErrorMessage,
            UiText.Get);
        if (!result.Success)
        {
            ShowEditIssue(status);
            return;
        }

        RefreshShell(status);
    }

    private Canvas? BuildPageBreakPreviewOverlay(ViewportModel viewport, bool showHeadings, double zoomFactor)
    {
        var sheet = _session.ActiveSheet;
        if (!PageBreakPreviewInstructionBuilder.TryResolvePrintRange(sheet, out var printRange))
            return null;

        var displayViewport = PageBreakPreviewInstructionBuilder.ProjectToDisplaySpace(
            viewport,
            zoomFactor,
            MinimumDisplayedColumnWidth,
            MinimumDisplayedRowHeight);

        var rowHeaderWidth = showHeadings ? GetRowHeaderWidth(viewport, zoomFactor) : 0;
        var columnHeaderHeight = showHeadings ? GetColumnHeaderHeight(viewport, zoomFactor) : 0;
        var actualWidth = CalculateDisplayedGridWidth(viewport, showHeadings, zoomFactor);
        var actualHeight = CalculateDisplayedGridHeight(viewport, showHeadings, zoomFactor);

        var layout = PageBreakPreviewLayoutPlanner.Calculate(
            displayViewport,
            printRange,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks,
            sheet.PageOrder,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            rowHeaderWidth,
            columnHeaderHeight,
            actualWidth,
            actualHeight,
            sheet.RowHeights,
            sheet.DefaultRowHeight,
            sheet.ColumnWidths,
            sheet.DefaultColumnWidth,
            sheet.HeaderMargin,
            sheet.FooterMargin,
            sheet.IsRowEffectivelyHidden,
            sheet.IsColEffectivelyHidden);

        var instructions = PageBreakPreviewInstructionBuilder.Build(layout);
        if (instructions.IsEmpty)
            return null;

        var overlay = new Canvas
        {
            Width = actualWidth,
            Height = actualHeight,
            ClipToBounds = true,
            IsHitTestVisible = false,
        };
        AutomationProperties.SetAutomationId(overlay, "PageBreakPreviewOverlay");

        RenderPageBreakInstructions(overlay, instructions);
        return overlay;
    }

    private static void RenderPageBreakInstructions(Canvas overlay, PageBreakPreviewInstructions instructions)
    {
        foreach (var mask in instructions.Masks)
        {
            var rect = new AvaloniaRectangle
            {
                Width = mask.Width,
                Height = mask.Height,
                Fill = PageBreakMaskFill,
            };
            Canvas.SetLeft(rect, mask.Left);
            Canvas.SetTop(rect, mask.Top);
            overlay.Children.Add(rect);
        }

        foreach (var watermark in instructions.Watermarks)
        {
            var text = new TextBlock
            {
                Text = watermark.Text,
                FontSize = watermark.FontSize,
                FontWeight = FontWeight.Bold,
                Foreground = PageBreakWatermarkBrush,
                Width = watermark.Width,
                Height = watermark.Height,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
            Canvas.SetLeft(text, watermark.Left);
            Canvas.SetTop(text, watermark.Top);
            overlay.Children.Add(text);
        }

        foreach (var border in instructions.Borders)
            AddPageBorder(overlay, border);

        foreach (var line in instructions.Lines)
        {
            var shape = new AvaloniaControlShapesLine
            {
                StartPoint = new Point(line.X1, line.Y1),
                EndPoint = new Point(line.X2, line.Y2),
                Stroke = PageBreakLineBrush,
                StrokeThickness = 1,
                StrokeDashArray = [4, 3],
            };
            overlay.Children.Add(shape);
        }
    }

    private static void AddPageBorder(Canvas overlay, PageBreakBorderInstruction border)
    {
        var left = border.Left;
        var top = border.Top;
        var right = border.Left + border.Width;
        var bottom = border.Top + border.Height;

        if (border.Edges.Top)
            AddBorderEdge(overlay, left, top, right, top);
        if (border.Edges.Bottom)
            AddBorderEdge(overlay, left, bottom, right, bottom);
        if (border.Edges.Left)
            AddBorderEdge(overlay, left, top, left, bottom);
        if (border.Edges.Right)
            AddBorderEdge(overlay, right, top, right, bottom);
    }

    private static void AddBorderEdge(Canvas overlay, double x1, double y1, double x2, double y2)
    {
        overlay.Children.Add(new AvaloniaControlShapesLine
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = PageBreakBorderBrush,
            StrokeThickness = 2,
        });
    }

    private static TextBlock PageSetupLabel(string text) =>
        new() { Text = StripDisplayMnemonic(text), VerticalAlignment = AvaloniaVerticalAlignment.Center, FontSize = 12, FontFamily = FormulaBarFontFamily };

    private static void ApplyPageLayoutButtonChrome(Button button, double minWidth, bool isDefault = false)
    {
        AvaloniaCompactDialogChrome.ApplyButton(button, PageLayoutDialogChromeStyle, minWidth, isDefault);
    }

    private static void ApplyPageLayoutTextBoxChrome(TextBox textBox)
    {
        AvaloniaCompactDialogChrome.ApplyTextBox(textBox, PageLayoutDialogChromeStyle);
    }

    private static void ApplyPageLayoutComboBoxChrome(ComboBox comboBox)
    {
        AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, PageLayoutDialogChromeStyle);
    }

    private static void ApplyPageLayoutCheckBoxChrome(CheckBox checkBox)
    {
        StripContentMnemonic(checkBox);
        checkBox.MinHeight = 20;
        checkBox.MaxHeight = 20;
        AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, PageLayoutDialogChromeStyle);
    }

    private static void ApplyPageLayoutRadioButtonChrome(RadioButton radioButton)
    {
        StripContentMnemonic(radioButton);
        radioButton.MinHeight = 20;
        AvaloniaCompactDialogChrome.ApplyRadioButton(radioButton, PageLayoutDialogChromeStyle);
    }

    private async Task<PageSetupDialogFields?> ShowPageSetupDialogCoreAsync(
        PageSetupDialogSurfacePlan surface,
        PageSetupDialogOpenPlan openPlan,
        bool openHeaderFooterTab = false)
    {
        PageSetupDialogFields? result = null;
        var initial = surface.Fields;
        var headerPictures = initial.HeaderPictures;
        var footerPictures = initial.FooterPictures;
        var dialog = new Window
        {
            Title = UiText.Get(PageSetupDialogPlanner.TitleResourceKey),
            Width = PageSetupDialogPlanner.WindowWidth,
            Height = PageSetupDialogPlanner.WindowHeight,
            MinWidth = PageSetupDialogPlanner.MinWindowWidth,
            MinHeight = PageSetupDialogPlanner.MinWindowHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, PageSetupDialogPlanner.DialogAutomationId);

        // --- Page tab ---
        var orientationChoices = PageSetupDialogPlanner.OrientationChoices;
        var orientationBox = new ComboBox
        {
            ItemsSource = PageSetupDialogPlanner.ResolveChoiceLabels(orientationChoices, UiText.Get),
            SelectedIndex = surface.ChoiceIndexes.Orientation,
            MinWidth = PageSetupDialogPlanner.FieldMinWidth,
        };
        ApplyPageLayoutComboBoxChrome(orientationBox);
        AutomationProperties.SetAutomationId(orientationBox, PageSetupDialogPlanner.OrientationBoxAutomationId);

        var paperSizeChoices = PageSetupDialogPlanner.PaperSizeChoices;
        var paperBox = new ComboBox
        {
            ItemsSource = PageSetupDialogPlanner.ResolveChoiceLabels(paperSizeChoices, UiText.Get),
            SelectedIndex = surface.ChoiceIndexes.PaperSize,
            MinWidth = PageSetupDialogPlanner.FieldMinWidth,
        };
        ApplyPageLayoutComboBoxChrome(paperBox);
        AutomationProperties.SetAutomationId(paperBox, PageSetupDialogPlanner.PaperSizeBoxAutomationId);

        var adjustRadio = new RadioButton
        {
            Content = StripDisplayMnemonic(UiText.Get("PageSetup_AdjustTo")),
            GroupName = "PageSetupScaling",
            IsChecked = initial.ScalingMode == PageSetupScalingMode.AdjustToPercent,
        };
        ApplyPageLayoutRadioButtonChrome(adjustRadio);
        AutomationProperties.SetAutomationId(adjustRadio, "PageSetupAdjustToRadio");
        var scalePercentBox = new TextBox { Text = surface.Scaling.ScalePercentText, MinWidth = 90 };
        ApplyPageLayoutTextBoxChrome(scalePercentBox);
        AutomationProperties.SetAutomationId(scalePercentBox, "PageSetupScalePercentBox");

        var fitRadio = new RadioButton
        {
            Content = StripDisplayMnemonic(UiText.Get("PageSetup_FitTo")),
            GroupName = "PageSetupScaling",
            IsChecked = initial.ScalingMode == PageSetupScalingMode.FitToPages,
        };
        ApplyPageLayoutRadioButtonChrome(fitRadio);
        AutomationProperties.SetAutomationId(fitRadio, "PageSetupFitToRadio");
        var fitWideBox = new TextBox { Text = surface.Scaling.FitToWideText, MinWidth = 70 };
        ApplyPageLayoutTextBoxChrome(fitWideBox);
        AutomationProperties.SetAutomationId(fitWideBox, "PageSetupFitWideBox");
        var fitTallBox = new TextBox { Text = surface.Scaling.FitToTallText, MinWidth = 70 };
        ApplyPageLayoutTextBoxChrome(fitTallBox);
        AutomationProperties.SetAutomationId(fitTallBox, "PageSetupFitTallBox");

        var firstPageNumberBox = new TextBox
        {
            Text = surface.FirstPageNumberText,
            MinWidth = 220,
            PlaceholderText = UiText.Get("PageSetup_Auto"),
        };
        ApplyPageLayoutTextBoxChrome(firstPageNumberBox);
        AutomationProperties.SetAutomationId(firstPageNumberBox, "PageSetupFirstPageNumberBox");

        var printQualityBox = new TextBox
        {
            Text = surface.PrintQualityDpiText,
            MinWidth = 220,
            PlaceholderText = UiText.Get("PageSetup_Auto"),
        };
        ApplyPageLayoutTextBoxChrome(printQualityBox);
        AutomationProperties.SetAutomationId(printQualityBox, "PageSetupPrintQualityBox");
        AutomationProperties.SetHelpText(printQualityBox, UiText.Get("PageSetup_PrintQualityHelp"));

        // --- Margins tab ---
        var marginsBox = new TextBox { Text = initial.MarginsText, MinWidth = 220 };
        ApplyPageLayoutTextBoxChrome(marginsBox);
        AutomationProperties.SetAutomationId(marginsBox, "PageSetupMarginsBox");
        AutomationProperties.SetHelpText(marginsBox, UiText.Get("PageSetup_MarginsHelp"));
        var headerMarginBox = new TextBox { Text = surface.HeaderMarginText, MinWidth = 220 };
        ApplyPageLayoutTextBoxChrome(headerMarginBox);
        AutomationProperties.SetAutomationId(headerMarginBox, "PageSetupHeaderMarginBox");
        var footerMarginBox = new TextBox { Text = surface.FooterMarginText, MinWidth = 220 };
        ApplyPageLayoutTextBoxChrome(footerMarginBox);
        AutomationProperties.SetAutomationId(footerMarginBox, "PageSetupFooterMarginBox");
        var centerHorizontallyCheck = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("PageSetup_CenterHorizontally")),
            IsChecked = initial.CenterHorizontally,
        };
        ApplyPageLayoutCheckBoxChrome(centerHorizontallyCheck);
        AutomationProperties.SetAutomationId(centerHorizontallyCheck, "PageSetupCenterHorizontallyCheck");
        var centerVerticallyCheck = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("PageSetup_CenterVertically")),
            IsChecked = initial.CenterVertically,
        };
        ApplyPageLayoutCheckBoxChrome(centerVerticallyCheck);
        AutomationProperties.SetAutomationId(centerVerticallyCheck, "PageSetupCenterVerticallyCheck");

        // --- Header/Footer tab ---
        var headerPresetChoices = PageSetupDialogModel.HeaderPresetChoices;
        var footerPresetChoices = PageSetupDialogModel.FooterPresetChoices;
        var headerPresetBox = new ComboBox
        {
            ItemsSource = PageSetupDialogPlanner.ResolveChoiceLabels(headerPresetChoices, UiText.Get),
            SelectedIndex = surface.ChoiceIndexes.HeaderPreset,
            MinWidth = PageSetupDialogPlanner.HeaderFooterPresetMinWidth,
        };
        ApplyPageLayoutComboBoxChrome(headerPresetBox);
        AutomationProperties.SetAutomationId(headerPresetBox, PageSetupDialogPlanner.HeaderPresetBoxAutomationId);
        var footerPresetBox = new ComboBox
        {
            ItemsSource = PageSetupDialogPlanner.ResolveChoiceLabels(footerPresetChoices, UiText.Get),
            SelectedIndex = surface.ChoiceIndexes.FooterPreset,
            MinWidth = PageSetupDialogPlanner.HeaderFooterPresetMinWidth,
        };
        ApplyPageLayoutComboBoxChrome(footerPresetBox);
        AutomationProperties.SetAutomationId(footerPresetBox, PageSetupDialogPlanner.FooterPresetBoxAutomationId);

        var headerLeftBox = new TextBox { Text = initial.Header.Left, MinWidth = 120 };
        ApplyPageLayoutTextBoxChrome(headerLeftBox);
        AutomationProperties.SetAutomationId(headerLeftBox, "PageSetupCustomHeaderLeftBox");
        var headerCenterBox = new TextBox { Text = initial.Header.Center, MinWidth = 120 };
        ApplyPageLayoutTextBoxChrome(headerCenterBox);
        AutomationProperties.SetAutomationId(headerCenterBox, "PageSetupCustomHeaderCenterBox");
        var headerRightBox = new TextBox { Text = initial.Header.Right, MinWidth = 120 };
        ApplyPageLayoutTextBoxChrome(headerRightBox);
        AutomationProperties.SetAutomationId(headerRightBox, "PageSetupCustomHeaderRightBox");
        var footerLeftBox = new TextBox { Text = initial.Footer.Left, MinWidth = 120 };
        ApplyPageLayoutTextBoxChrome(footerLeftBox);
        AutomationProperties.SetAutomationId(footerLeftBox, "PageSetupCustomFooterLeftBox");
        var footerCenterBox = new TextBox { Text = initial.Footer.Center, MinWidth = 120 };
        ApplyPageLayoutTextBoxChrome(footerCenterBox);
        AutomationProperties.SetAutomationId(footerCenterBox, "PageSetupCustomFooterCenterBox");
        var footerRightBox = new TextBox { Text = initial.Footer.Right, MinWidth = 120 };
        ApplyPageLayoutTextBoxChrome(footerRightBox);
        AutomationProperties.SetAutomationId(footerRightBox, "PageSetupCustomFooterRightBox");

        var formatHeaderPictureButton = new Button
        {
            Content = UiText.Get("FormatPicture_Title"),
            IsEnabled = headerPictures.Left is not null || headerPictures.Center is not null || headerPictures.Right is not null,
        };
        ApplyPageLayoutButtonChrome(formatHeaderPictureButton, 128);
        AutomationProperties.SetAutomationId(formatHeaderPictureButton, "PageSetupFormatHeaderPictureButton");
        formatHeaderPictureButton.Click += async (_, _) =>
        {
            var preferred = headerLeftBox.IsFocused
                ? HeaderFooterEditorSection.Left
                : headerRightBox.IsFocused
                    ? HeaderFooterEditorSection.Right
                    : HeaderFooterEditorSection.Center;
            if (await ShowHeaderFooterPictureSetFormatDialogAsync(headerPictures, preferred) is { } updated)
                headerPictures = updated;
        };

        var formatFooterPictureButton = new Button
        {
            Content = UiText.Get("FormatPicture_Title"),
            IsEnabled = footerPictures.Left is not null || footerPictures.Center is not null || footerPictures.Right is not null,
        };
        ApplyPageLayoutButtonChrome(formatFooterPictureButton, 128);
        AutomationProperties.SetAutomationId(formatFooterPictureButton, "PageSetupFormatFooterPictureButton");
        formatFooterPictureButton.Click += async (_, _) =>
        {
            var preferred = footerLeftBox.IsFocused
                ? HeaderFooterEditorSection.Left
                : footerRightBox.IsFocused
                    ? HeaderFooterEditorSection.Right
                    : HeaderFooterEditorSection.Center;
            if (await ShowHeaderFooterPictureSetFormatDialogAsync(footerPictures, preferred) is { } updated)
                footerPictures = updated;
        };

        // A preset selection fills the matching custom center box (mirrors the WPF preset combo).
        headerPresetBox.SelectionChanged += (_, _) =>
        {
            if (headerPresetBox.SelectedIndex < 0)
                return;

            var header = PageSetupDialogPlanner.ApplyHeaderPreset(
                new WorksheetHeaderFooter(
                    headerLeftBox.Text ?? "",
                    headerCenterBox.Text ?? "",
                    headerRightBox.Text ?? ""),
                headerPresetBox.SelectedIndex);
            headerCenterBox.Text = header.Center;
        };
        footerPresetBox.SelectionChanged += (_, _) =>
        {
            if (footerPresetBox.SelectedIndex < 0)
                return;

            var footer = PageSetupDialogPlanner.ApplyFooterPreset(
                new WorksheetHeaderFooter(
                    footerLeftBox.Text ?? "",
                    footerCenterBox.Text ?? "",
                    footerRightBox.Text ?? ""),
                footerPresetBox.SelectedIndex);
            footerCenterBox.Text = footer.Center;
        };

        var differentFirstPageCheck = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("PageSetup_DifferentFirstPage")),
            IsChecked = initial.DifferentFirstPage,
        };
        ApplyPageLayoutCheckBoxChrome(differentFirstPageCheck);
        AutomationProperties.SetAutomationId(differentFirstPageCheck, "PageSetupDifferentFirstPageCheck");
        var differentOddEvenCheck = new CheckBox
        {
            Content = UiText.Get("PageSetup_DifferentOddEven"),
            IsChecked = initial.DifferentOddEvenPages,
        };
        ApplyPageLayoutCheckBoxChrome(differentOddEvenCheck);
        AutomationProperties.SetAutomationId(differentOddEvenCheck, "PageSetupDifferentOddEvenCheck");
        var scaleWithDocumentCheck = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("PageSetup_ScaleWithDocument")),
            IsChecked = initial.ScaleHeaderFooterWithDocument,
        };
        ApplyPageLayoutCheckBoxChrome(scaleWithDocumentCheck);
        AutomationProperties.SetAutomationId(scaleWithDocumentCheck, "PageSetupScaleWithDocumentCheck");
        var alignWithMarginsCheck = new CheckBox
        {
            Content = UiText.Get("PageSetup_AlignWithMargins"),
            IsChecked = initial.AlignHeaderFooterWithMargins,
        };
        ApplyPageLayoutCheckBoxChrome(alignWithMarginsCheck);
        AutomationProperties.SetAutomationId(alignWithMarginsCheck, "PageSetupAlignWithMarginsCheck");

        // --- Sheet tab ---
        var printAreaBox = new TextBox { Text = surface.PrintAreaText, MinWidth = 220 };
        ApplyPageLayoutTextBoxChrome(printAreaBox);
        AutomationProperties.SetAutomationId(printAreaBox, "PageSetupPrintAreaBox");
        AutomationProperties.SetHelpText(printAreaBox, UiText.Get("PageSetup_PrintAreaHelp"));

        var repeatRowsBox = new TextBox { Text = surface.RepeatRowsText, MinWidth = 220 };
        ApplyPageLayoutTextBoxChrome(repeatRowsBox);
        AutomationProperties.SetAutomationId(repeatRowsBox, "PageSetupRepeatRowsBox");
        AutomationProperties.SetHelpText(repeatRowsBox, UiText.Get("PageSetup_RepeatRowsHelp"));

        var repeatColumnsBox = new TextBox { Text = surface.RepeatColumnsText, MinWidth = 220 };
        ApplyPageLayoutTextBoxChrome(repeatColumnsBox);
        AutomationProperties.SetAutomationId(repeatColumnsBox, "PageSetupRepeatColumnsBox");
        AutomationProperties.SetHelpText(repeatColumnsBox, UiText.Get("PageSetup_RepeatColumnsHelp"));

        var printAreaPicker = CreateDialogRangePickerButton(
            "PageSetupPrintAreaPickerButton",
            UiText.Get("PageSetup_SelectPrintArea"));
        AutomationProperties.SetHelpText(printAreaPicker, UiText.Get("PageSetup_SelectPrintAreaHelpText"));
        ToolTip.SetTip(printAreaPicker, UiText.Get("PageSetup_CollapseDialogAndSelectThePrintArea"));
        var repeatRowsPicker = CreateDialogRangePickerButton(
            "PageSetupRowsRepeatPickerButton",
            UiText.Get("PageSetup_SelectRowsToRepeat"));
        AutomationProperties.SetHelpText(repeatRowsPicker, UiText.Get("PageSetup_SelectRowsToRepeatHelpText"));
        ToolTip.SetTip(repeatRowsPicker, UiText.Get("PageSetup_CollapseDialogAndSelectRowsToRepeatAtTop"));
        var repeatColumnsPicker = CreateDialogRangePickerButton(
            "PageSetupColumnsRepeatPickerButton",
            UiText.Get("PageSetup_SelectColumnsToRepeat"));
        AutomationProperties.SetHelpText(repeatColumnsPicker, UiText.Get("PageSetup_SelectColumnsToRepeatHelpText"));
        ToolTip.SetTip(repeatColumnsPicker, UiText.Get("PageSetup_CollapseDialogAndSelectColumnsToRepeatAtLeft"));

        var gridlinesCheck = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("PageSetup_PrintGridlines")), IsChecked = initial.PrintGridlines };
        ApplyPageLayoutCheckBoxChrome(gridlinesCheck);
        AutomationProperties.SetAutomationId(gridlinesCheck, "PageSetupPrintGridlinesCheck");
        var headingsCheck = new CheckBox { Content = UiText.Get("PageSetup_PrintHeadings"), IsChecked = initial.PrintHeadings };
        ApplyPageLayoutCheckBoxChrome(headingsCheck);
        AutomationProperties.SetAutomationId(headingsCheck, "PageSetupPrintHeadingsCheck");
        var blackAndWhiteCheck = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("PageSetup_BlackAndWhite")), IsChecked = initial.PrintBlackAndWhite };
        ApplyPageLayoutCheckBoxChrome(blackAndWhiteCheck);
        AutomationProperties.SetAutomationId(blackAndWhiteCheck, "PageSetupBlackAndWhiteCheck");
        var draftQualityCheck = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("PageSetup_DraftQuality")), IsChecked = initial.PrintDraftQuality };
        ApplyPageLayoutCheckBoxChrome(draftQualityCheck);
        AutomationProperties.SetAutomationId(draftQualityCheck, "PageSetupDraftQualityCheck");

        var pageOrderChoices = PageSetupDialogPlanner.PageOrderChoices;
        var pageOrderBox = new ComboBox
        {
            ItemsSource = PageSetupDialogPlanner.ResolveChoiceLabels(pageOrderChoices, UiText.Get),
            SelectedIndex = surface.ChoiceIndexes.PageOrder,
            MinWidth = PageSetupDialogPlanner.FieldMinWidth,
        };
        ApplyPageLayoutComboBoxChrome(pageOrderBox);
        AutomationProperties.SetAutomationId(pageOrderBox, PageSetupDialogPlanner.PageOrderBoxAutomationId);

        var printErrorValueChoices = PageSetupDialogPlanner.PrintErrorValueChoices;
        var cellErrorsBox = new ComboBox
        {
            ItemsSource = PageSetupDialogPlanner.ResolveChoiceLabels(printErrorValueChoices, UiText.Get),
            SelectedIndex = surface.ChoiceIndexes.PrintErrorValue,
            MinWidth = PageSetupDialogPlanner.FieldMinWidth,
        };
        ApplyPageLayoutComboBoxChrome(cellErrorsBox);
        AutomationProperties.SetAutomationId(cellErrorsBox, PageSetupDialogPlanner.CellErrorsBoxAutomationId);

        var printCommentChoices = PageSetupDialogPlanner.PrintCommentChoices;
        var commentsBox = new ComboBox
        {
            ItemsSource = PageSetupDialogPlanner.ResolveChoiceLabels(printCommentChoices, UiText.Get),
            SelectedIndex = surface.ChoiceIndexes.PrintComments,
            MinWidth = PageSetupDialogPlanner.FieldMinWidth,
        };
        ApplyPageLayoutComboBoxChrome(commentsBox);
        AutomationProperties.SetAutomationId(commentsBox, PageSetupDialogPlanner.CommentsBoxAutomationId);

        var validationText = new TextBlock
        {
            Foreground = Brush(143, 74, 18),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(validationText, PageSetupDialogPlanner.ValidationTextAutomationId);

        var okButton = new Button { Content = UiText.Get("Common_Ok"), MinWidth = PageSetupDialogPlanner.FooterButtonMinWidth };
        ApplyPageLayoutButtonChrome(okButton, PageSetupDialogPlanner.FooterButtonMinWidth, isDefault: true);
        var cancelButton = new Button
        {
            Content = UiText.Get("Common_Cancel"),
            IsCancel = true,
            MinWidth = PageSetupDialogPlanner.FooterButtonMinWidth,
        };
        ApplyPageLayoutButtonChrome(cancelButton, PageSetupDialogPlanner.FooterButtonMinWidth);
        AutomationProperties.SetAutomationId(okButton, PageSetupDialogPlanner.OkButtonAutomationId);
        AutomationProperties.SetAutomationId(cancelButton, PageSetupDialogPlanner.CancelButtonAutomationId);

        // WPF has [Print...][Print Preview][Options...] on the bottom left
        var printButton = new Button { Content = UiText.Get("PageSetup_PrintButton"), MinWidth = PageSetupDialogPlanner.FooterButtonMinWidth };
        ApplyPageLayoutButtonChrome(printButton, PageSetupDialogPlanner.FooterButtonMinWidth);
        AutomationProperties.SetAutomationId(printButton, PageSetupDialogPlanner.PrintButtonAutomationId);
        var printPreviewButton = new Button { Content = UiText.Get("PageSetup_PrintPreviewButton"), MinWidth = PageSetupDialogPlanner.PrintPreviewButtonMinWidth };
        ApplyPageLayoutButtonChrome(printPreviewButton, PageSetupDialogPlanner.PrintPreviewButtonMinWidth);
        AutomationProperties.SetAutomationId(printPreviewButton, PageSetupDialogPlanner.PrintPreviewButtonAutomationId);
        var optionsButton = new Button { Content = UiText.Get("PageSetup_OptionsButton"), MinWidth = PageSetupDialogPlanner.FooterButtonMinWidth };
        ApplyPageLayoutButtonChrome(optionsButton, PageSetupDialogPlanner.FooterButtonMinWidth);
        AutomationProperties.SetAutomationId(optionsButton, PageSetupDialogPlanner.OptionsButtonAutomationId);
        // These are stub buttons (print/preview not yet wired in Avalonia shell)
        printButton.IsEnabled = false;
        printPreviewButton.IsEnabled = false;
        optionsButton.IsEnabled = false;

        PageSetupDialogFields ReadFields() => PageSetupDialogPlanner.BuildFields(initial, new PageSetupDialogSurfaceInput
        {
            OrientationIndex = orientationBox.SelectedIndex,
            PaperSizeIndex = paperBox.SelectedIndex,
            MarginsText = marginsBox.Text ?? "",
            HeaderMarginText = headerMarginBox.Text ?? "",
            FooterMarginText = footerMarginBox.Text ?? "",
            CenterHorizontally = centerHorizontallyCheck.IsChecked == true,
            CenterVertically = centerVerticallyCheck.IsChecked == true,
            ScalingMode = fitRadio.IsChecked == true
                ? PageSetupScalingMode.FitToPages
                : PageSetupScalingMode.AdjustToPercent,
            ScalePercentText = scalePercentBox.Text ?? "",
            FitToWideText = fitWideBox.Text ?? "",
            FitToTallText = fitTallBox.Text ?? "",
            FirstPageNumberText = firstPageNumberBox.Text ?? "",
            PrintQualityDpiText = printQualityBox.Text ?? "",
            PrintAreaText = printAreaBox.Text ?? "",
            RepeatRowsText = repeatRowsBox.Text ?? "",
            RepeatColumnsText = repeatColumnsBox.Text ?? "",
            PrintGridlines = gridlinesCheck.IsChecked == true,
            PrintHeadings = headingsCheck.IsChecked == true,
            PrintBlackAndWhite = blackAndWhiteCheck.IsChecked == true,
            PrintDraftQuality = draftQualityCheck.IsChecked == true,
            PrintErrorValueIndex = cellErrorsBox.SelectedIndex,
            PrintCommentsIndex = commentsBox.SelectedIndex,
            PageOrderIndex = pageOrderBox.SelectedIndex,
            Header = new WorksheetHeaderFooter(headerLeftBox.Text ?? "", headerCenterBox.Text ?? "", headerRightBox.Text ?? ""),
            Footer = new WorksheetHeaderFooter(footerLeftBox.Text ?? "", footerCenterBox.Text ?? "", footerRightBox.Text ?? ""),
            FirstPageHeader = initial.FirstPageHeader,
            FirstPageFooter = initial.FirstPageFooter,
            EvenPageHeader = initial.EvenPageHeader,
            EvenPageFooter = initial.EvenPageFooter,
            HeaderPictures = headerPictures,
            FooterPictures = footerPictures,
            FirstPageHeaderPictures = initial.FirstPageHeaderPictures,
            FirstPageFooterPictures = initial.FirstPageFooterPictures,
            EvenPageHeaderPictures = initial.EvenPageHeaderPictures,
            EvenPageFooterPictures = initial.EvenPageFooterPictures,
            DifferentFirstPage = differentFirstPageCheck.IsChecked == true,
            DifferentOddEvenPages = differentOddEvenCheck.IsChecked == true,
            ScaleHeaderFooterWithDocument = scaleWithDocumentCheck.IsChecked == true,
            AlignHeaderFooterWithMargins = alignWithMarginsCheck.IsChecked == true,
        });

        // WPF layout: [Print...][Print Preview][Options...]  ··fill··  [OK][Cancel]
        var leftButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { printButton, printPreviewButton, optionsButton },
        };
        var rightButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { okButton, cancelButton },
        };
        var buttonRow = new Grid
        {
            Margin = new Thickness(0, 12, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };
        Grid.SetColumn(leftButtons, 0);
        Grid.SetColumn(rightButtons, 2);
        buttonRow.Children.Add(leftButtons);
        buttonRow.Children.Add(rightButtons);

        var adjustRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                adjustRadio,
                scalePercentBox,
                PageSetupLabel("%"),
            },
        };

        var fitRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                fitRadio,
                fitWideBox,
                PageSetupLabel(UiText.Get("PageSetup_WideBy")),
                fitTallBox,
                PageSetupLabel(UiText.Get("PageSetup_Tall")),
            },
        };

        var pageTab = new TabItem
        {
            Header = StripDisplayMnemonic(UiText.Get("PageSetup_PageTab")),
            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Margin = new Thickness(14),
                    Spacing = 8,
                    Children =
                    {
                        PageSetupLabel(UiText.Get("PageSetup_Orientation")),
                        orientationBox,
                        PageSetupLabel(UiText.Get("PageSetup_PaperSize")),
                        paperBox,
                        new TextBlock { Text = UiText.Get("PageSetup_Scaling"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) },
                        adjustRow,
                        fitRow,
                        PageSetupLabel(UiText.Get("PageSetup_FirstPageNumber")),
                        firstPageNumberBox,
                        PageSetupLabel(UiText.Get("PageSetup_PrintQuality")),
                        printQualityBox,
                    },
                },
            },
        };

        var marginsTab = new TabItem
        {
            Header = StripDisplayMnemonic(UiText.Get("PageSetup_MarginsTab")),
            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Margin = new Thickness(14),
                    Spacing = 8,
                    Children =
                    {
                        PageSetupLabel(UiText.Get("PageSetup_MarginsLabel")),
                        marginsBox,
                        PageSetupLabel(UiText.Get("PageSetup_HeaderMargin")),
                        headerMarginBox,
                        PageSetupLabel(UiText.Get("PageSetup_FooterMargin")),
                        footerMarginBox,
                        new TextBlock { Text = UiText.Get("PageSetup_CenterOnPage"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) },
                        centerHorizontallyCheck,
                        centerVerticallyCheck,
                    },
                },
            },
        };

        var headerFooterTab = new TabItem
        {
            Header = StripDisplayMnemonic(UiText.Get("PageSetup_HeaderFooterTab")),
            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Margin = new Thickness(14),
                    Spacing = 8,
                    Children =
                    {
                        PageSetupLabel(UiText.Get("PageSetup_HeaderPreset")),
                        headerPresetBox,
                        new TextBlock { Text = StripDisplayMnemonic(UiText.Get("PageSetup_CustomHeader")), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 6,
                            Children = { headerLeftBox, headerCenterBox, headerRightBox },
                        },
                        formatHeaderPictureButton,
                        PageSetupLabel(UiText.Get("PageSetup_FooterPreset")),
                        footerPresetBox,
                        new TextBlock { Text = StripDisplayMnemonic(UiText.Get("PageSetup_CustomFooter")), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 6,
                            Children = { footerLeftBox, footerCenterBox, footerRightBox },
                        },
                        formatFooterPictureButton,
                        differentFirstPageCheck,
                        differentOddEvenCheck,
                        scaleWithDocumentCheck,
                        alignWithMarginsCheck,
                    },
                },
            },
        };

        var sheetTab = new TabItem
        {
            Header = StripDisplayMnemonic(UiText.Get("PageSetup_SheetTab")),
            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Margin = new Thickness(14),
                    Spacing = 8,
                    Children =
                    {
                        PageSetupLabel(UiText.Get("PageSetup_PrintArea")),
                        BuildDialogRangePickerRow(printAreaBox, printAreaPicker),
                        PageSetupLabel(UiText.Get("PageSetup_RepeatRows")),
                        BuildDialogRangePickerRow(repeatRowsBox, repeatRowsPicker),
                        PageSetupLabel(UiText.Get("PageSetup_RepeatColumns")),
                        BuildDialogRangePickerRow(repeatColumnsBox, repeatColumnsPicker),
                        gridlinesCheck,
                        headingsCheck,
                        blackAndWhiteCheck,
                        draftQualityCheck,
                        PageSetupLabel(UiText.Get("PageSetup_CellErrorsAs")),
                        cellErrorsBox,
                        PageSetupLabel(UiText.Get("PageSetup_Comments")),
                        commentsBox,
                        PageSetupLabel(UiText.Get("PageSetup_PageOrder")),
                        pageOrderBox,
                    },
                },
            },
        };

        var tabs = new TabControl
        {
            Items = { pageTab, marginsTab, headerFooterTab, sheetTab },
        };
        AutomationProperties.SetAutomationId(tabs, PageSetupDialogPlanner.TabsAutomationId);
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(tabs);

        void SelectValidationRoute(PageSetupValidationRoute route)
        {
            tabs.SelectedItem = route.Tab switch
            {
                PageSetupDialogTab.Margins => marginsTab,
                PageSetupDialogTab.Sheet => sheetTab,
                _ => pageTab,
            };
        }

        Control FocusControlFor(PageSetupDialogFocusTarget target) =>
            target switch
            {
                PageSetupDialogFocusTarget.PaperSize => paperBox,
                PageSetupDialogFocusTarget.Margins => marginsBox,
                PageSetupDialogFocusTarget.LeftMargin => marginsBox,
                PageSetupDialogFocusTarget.RightMargin => marginsBox,
                PageSetupDialogFocusTarget.TopMargin => marginsBox,
                PageSetupDialogFocusTarget.BottomMargin => marginsBox,
                PageSetupDialogFocusTarget.HeaderMargin => headerMarginBox,
                PageSetupDialogFocusTarget.FooterMargin => footerMarginBox,
                PageSetupDialogFocusTarget.ScalePercent => scalePercentBox,
                PageSetupDialogFocusTarget.FitPagesWide => fitWideBox,
                PageSetupDialogFocusTarget.FitPagesTall => fitTallBox,
                PageSetupDialogFocusTarget.FirstPageNumber => firstPageNumberBox,
                PageSetupDialogFocusTarget.PrintQuality => printQualityBox,
                PageSetupDialogFocusTarget.PrintArea => printAreaBox,
                PageSetupDialogFocusTarget.RepeatRows => repeatRowsBox,
                PageSetupDialogFocusTarget.RepeatColumns => repeatColumnsBox,
                PageSetupDialogFocusTarget.PageOrder => pageOrderBox,
                PageSetupDialogFocusTarget.PrintErrorValue => cellErrorsBox,
                PageSetupDialogFocusTarget.PrintComments => commentsBox,
                _ => orientationBox,
            };

        void FocusDialogTarget(PageSetupDialogFocusPlan plan)
        {
            SelectValidationRoute(plan.Route);
            var target = FocusControlFor(plan.Target);
            target.Focus();
            if (target is TextBox textBox)
                textBox.SelectAll();
        }

        PageSetupDialogValidationFocusState CreateValidationFocusState() =>
            new()
            {
                HasSeparateMarginFields = false,
                MarginsText = marginsBox.Text ?? "",
                HeaderMarginText = headerMarginBox.Text ?? "",
                FooterMarginText = footerMarginBox.Text ?? "",
                ScalingMode = fitRadio.IsChecked == true
                    ? PageSetupScalingMode.FitToPages
                    : PageSetupScalingMode.AdjustToPercent,
                FitToWideText = fitWideBox.Text ?? "",
                RepeatRowsText = repeatRowsBox.Text ?? "",
            };

        void FocusOpenPlan(PageSetupDialogOpenPlan plan)
        {
            FocusDialogTarget(
                PageSetupDialogPlanner.PlanInitialFocus(
                    plan,
                    fitRadio.IsChecked == true
                        ? PageSetupScalingMode.FitToPages
                        : PageSetupScalingMode.AdjustToPercent));
        }

        void Accept()
        {
            var fields = ReadFields();
            var submission = PageSetupSubmissionPlanner.TryBuild(_session.ActiveSheet, fields);
            if (!submission.Success)
            {
                var validation = submission.Validation!;
                FocusDialogTarget(
                    PageSetupDialogPlanner.PlanValidationFocus(
                        validation.Target,
                        CreateValidationFocusState()));
                validationText.Text = PageLayoutStatusPlanner.ResolvePageSetupValidationIssue(validation, UiText.Get);
                validationText.IsVisible = true;
                return;
            }

            result = fields;
            dialog.Close();
        }

        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };

        DockPanel.SetDock(buttonRow, AvaloniaDock.Bottom);
        DockPanel.SetDock(validationText, AvaloniaDock.Bottom);
        var root = new DockPanel
        {
            Margin = new Thickness(8),
            Children = { buttonRow, validationText, tabs },
        };
        ConfigurePageSetupTabCycle(dialog, root, cancelButton);
        dialog.Content = root;
        AttachDialogRangePicker(dialog, printAreaPicker, printAreaBox, "range.page-setup.print-area");
        AttachDialogRangePicker(dialog, repeatRowsPicker, repeatRowsBox, "range.page-setup.rows-to-repeat");
        AttachDialogRangePicker(dialog, repeatColumnsPicker, repeatColumnsBox, "range.page-setup.columns-to-repeat");
        dialog.Opened += (_, _) =>
        {
            if (openHeaderFooterTab)
            {
                tabs.SelectedItem = headerFooterTab;
                headerPresetBox.Focus();
            }
            else
            {
                FocusOpenPlan(openPlan);
            }
        };

        await dialog.ShowDialog(this);
        return result;
    }
}
