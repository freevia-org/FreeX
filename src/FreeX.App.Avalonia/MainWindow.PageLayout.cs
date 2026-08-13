using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

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

    private static FuncDataTemplate<HeaderFooterPresetChoice> HeaderFooterPresetTemplate() =>
        new((choice, _) => new TextBlock { Text = UiText.Get(choice.LabelResourceKey) }, supportsRecycling: true);

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
        var dialogResult = await ShowPageSetupDialogCoreAsync(
            PageSetupDialogPlanner.PlanSurface(sheet),
            PageSetupDialogPlanner.PlanOpen(source),
            openHeaderFooterTab);
        if (dialogResult is null)
            return;

        await ApplyPageSetupFieldsAsync(
            sheet,
            dialogResult.Value.Fields,
            dialogResult.Value.RequestedAction);
    }

    private async Task ShowHeaderFooterDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        var sheet = _session.ActiveSheet;
        var initial = PageSetupDialogModel.FromSheet(sheet);
        var edited = await ShowHeaderFooterEditorDialogAsync(
            HeaderFooterEditorState.FromPageSetupFields(initial),
            openFooterTab: false);
        if (edited is null)
            return;

        var plan = CreatePageLayoutCommandSession().PlanHeaderFooter(
            edited,
            _statusText.Text ?? UiText.Get("MainLoc_Ready"));
        var result = _session.ExecuteReviewCommand(plan.Command);
        var status = PageLayoutStatusPlanner.ResolveCommandStatus(
            plan,
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

    private async Task ApplyPageSetupFieldsAsync(
        Sheet sheet,
        PageSetupDialogFields fields,
        PageSetupDialogAction requestedAction)
    {
        // Page Setup currently targets the active sheet in this shell. Other Page Layout ribbon
        // actions use CreatePageLayoutCommandSession to preserve their existing grouped-sheet behavior.
        var build = new PageLayoutCommandSession([sheet.Id]).TryPlanPageSetup(
            sheet,
            fields,
            requestedAction);
        if (!build.Success)
        {
            ShowEditIssue(PageLayoutStatusPlanner.ResolvePageSetupValidationIssue(build.Validation!, UiText.Get));
            return;
        }

        var plan = build.Plan!;
        var result = _session.ExecuteReviewCommand(plan.Execution.Command);
        var status = PageLayoutStatusPlanner.ResolveCommandStatus(
            plan.Execution,
            result.Success,
            result.ErrorMessage,
            UiText.Get);
        if (!result.Success)
        {
            ShowEditIssue(status);
            return;
        }

        RefreshShell(status);

        switch (plan.FollowUpAction)
        {
            case PageSetupDialogFollowUpAction.ShowPrinterOptions:
                // Avalonia has no separate printer-properties API. Its print dialog is the
                // portable equivalent of WPF's printer-options surface and exposes the
                // available printer, copies, collation, and page-range choices.
                await ShowPrintDialogAsync();
                break;
            case PageSetupDialogFollowUpAction.Print:
                await ShowPrintDialogAsync();
                break;
            case PageSetupDialogFollowUpAction.PrintPreview:
                await ShowPrintPreviewDialogAsync();
                break;
        }
    }

    private void TogglePageBreakPreview()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        var plan = PageLayoutStatusPlanner.PlanPageBreakPreviewToggle(_session.ViewMode);
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
        if (!PageBreakPreviewInstructionBuilder.TryResolvePrintRanges(sheet, out var printRanges))
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
            printRanges,
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

    private async Task<(PageSetupDialogFields Fields, PageSetupDialogAction RequestedAction)?> ShowPageSetupDialogCoreAsync(
        PageSetupDialogSurfacePlan surface,
        PageSetupDialogOpenPlan openPlan,
        bool openHeaderFooterTab = false)
    {
        (PageSetupDialogFields Fields, PageSetupDialogAction RequestedAction)? result = null;
        var initial = surface.Fields;
        var initialHeaderFooter = initial.HeaderFooter.DeepClone();
        var headerPictures = initialHeaderFooter.HeaderPictures;
        var footerPictures = initialHeaderFooter.FooterPictures;
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
        var leftMarginBox = new TextBox { Text = surface.Margins.Left, MinWidth = PageSetupDialogPlanner.FieldMinWidth };
        ApplyPageLayoutTextBoxChrome(leftMarginBox);
        AutomationProperties.SetAutomationId(leftMarginBox, PageSetupDialogPlanner.LeftMarginBoxAutomationId);
        var rightMarginBox = new TextBox { Text = surface.Margins.Right, MinWidth = PageSetupDialogPlanner.FieldMinWidth };
        ApplyPageLayoutTextBoxChrome(rightMarginBox);
        AutomationProperties.SetAutomationId(rightMarginBox, PageSetupDialogPlanner.RightMarginBoxAutomationId);
        var topMarginBox = new TextBox { Text = surface.Margins.Top, MinWidth = PageSetupDialogPlanner.FieldMinWidth };
        ApplyPageLayoutTextBoxChrome(topMarginBox);
        AutomationProperties.SetAutomationId(topMarginBox, PageSetupDialogPlanner.TopMarginBoxAutomationId);
        var bottomMarginBox = new TextBox { Text = surface.Margins.Bottom, MinWidth = PageSetupDialogPlanner.FieldMinWidth };
        ApplyPageLayoutTextBoxChrome(bottomMarginBox);
        AutomationProperties.SetAutomationId(bottomMarginBox, PageSetupDialogPlanner.BottomMarginBoxAutomationId);
        var headerMarginBox = new TextBox { Text = surface.HeaderMarginText, MinWidth = 220 };
        ApplyPageLayoutTextBoxChrome(headerMarginBox);
        AutomationProperties.SetAutomationId(headerMarginBox, PageSetupDialogPlanner.HeaderMarginBoxAutomationId);
        var footerMarginBox = new TextBox { Text = surface.FooterMarginText, MinWidth = 220 };
        ApplyPageLayoutTextBoxChrome(footerMarginBox);
        AutomationProperties.SetAutomationId(footerMarginBox, PageSetupDialogPlanner.FooterMarginBoxAutomationId);
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
        var header = initialHeaderFooter.Header;
        var footer = initialHeaderFooter.Footer;
        var firstPageHeader = initialHeaderFooter.FirstPageHeader;
        var firstPageFooter = initialHeaderFooter.FirstPageFooter;
        var evenPageHeader = initialHeaderFooter.EvenPageHeader;
        var evenPageFooter = initialHeaderFooter.EvenPageFooter;
        var firstPageHeaderPictures = initialHeaderFooter.FirstPageHeaderPictures;
        var firstPageFooterPictures = initialHeaderFooter.FirstPageFooterPictures;
        var evenPageHeaderPictures = initialHeaderFooter.EvenPageHeaderPictures;
        var evenPageFooterPictures = initialHeaderFooter.EvenPageFooterPictures;
        var headerPresetChoices = PageSetupDialogModel.HeaderPresetChoices;
        var footerPresetChoices = PageSetupDialogModel.FooterPresetChoices;
        var headerPresetBox = new ComboBox
        {
            ItemsSource = headerPresetChoices,
            SelectedIndex = surface.ChoiceIndexes.HeaderPreset,
            MinWidth = PageSetupDialogPlanner.HeaderFooterPresetMinWidth,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        headerPresetBox.ItemTemplate = HeaderFooterPresetTemplate();
        ApplyPageLayoutComboBoxChrome(headerPresetBox);
        AutomationProperties.SetAutomationId(headerPresetBox, PageSetupDialogPlanner.HeaderPresetBoxAutomationId);
        var footerPresetBox = new ComboBox
        {
            ItemsSource = footerPresetChoices,
            SelectedIndex = surface.ChoiceIndexes.FooterPreset,
            MinWidth = PageSetupDialogPlanner.HeaderFooterPresetMinWidth,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        footerPresetBox.ItemTemplate = HeaderFooterPresetTemplate();
        ApplyPageLayoutComboBoxChrome(footerPresetBox);
        AutomationProperties.SetAutomationId(footerPresetBox, PageSetupDialogPlanner.FooterPresetBoxAutomationId);

        var headerPreviewText = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        AutomationProperties.SetAutomationId(headerPreviewText, "PageSetupHeaderPreviewText");
        var footerPreviewText = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        AutomationProperties.SetAutomationId(footerPreviewText, "PageSetupFooterPreviewText");

        void UpdateHeaderFooterPreview()
        {
            headerPreviewText.Text = PageSetupDialogModel.BuildHeaderFooterPreview(header, UiText.Get("PageSetup_None"));
            footerPreviewText.Text = PageSetupDialogModel.BuildHeaderFooterPreview(footer, UiText.Get("PageSetup_None"));
        }

        var customHeaderButton = new Button
        {
            Content = UiText.Get("PageSetup_CustomHeader"),
            Height = 22,
            MinHeight = 22,
            MaxHeight = 22,
        };
        ApplyPageLayoutButtonChrome(customHeaderButton, 128);
        AutomationProperties.SetAutomationId(customHeaderButton, "PageSetupCustomHeaderButton");
        var customFooterButton = new Button
        {
            Content = UiText.Get("PageSetup_CustomFooter"),
            Height = 22,
            MinHeight = 22,
            MaxHeight = 22,
        };
        ApplyPageLayoutButtonChrome(customFooterButton, 128);
        AutomationProperties.SetAutomationId(customFooterButton, "PageSetupCustomFooterButton");

        headerPresetBox.SelectionChanged += (_, _) =>
        {
            if (headerPresetBox.SelectedItem is not HeaderFooterPresetChoice choice)
                return;

            header = PageSetupDialogPlanner.ApplyHeaderPreset(header, choice);
            UpdateHeaderFooterPreview();
        };
        footerPresetBox.SelectionChanged += (_, _) =>
        {
            if (footerPresetBox.SelectedItem is not HeaderFooterPresetChoice choice)
                return;

            footer = PageSetupDialogPlanner.ApplyFooterPreset(footer, choice);
            UpdateHeaderFooterPreview();
        };

        var differentFirstPageCheck = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("PageSetup_DifferentFirstPage")),
            IsChecked = initialHeaderFooter.DifferentFirstPage,
        };
        ApplyPageLayoutCheckBoxChrome(differentFirstPageCheck);
        AutomationProperties.SetAutomationId(differentFirstPageCheck, "PageSetupDifferentFirstPageCheck");
        var differentOddEvenCheck = new CheckBox
        {
            Content = UiText.Get("PageSetup_DifferentOddEven"),
            IsChecked = initialHeaderFooter.DifferentOddEvenPages,
        };
        ApplyPageLayoutCheckBoxChrome(differentOddEvenCheck);
        AutomationProperties.SetAutomationId(differentOddEvenCheck, "PageSetupDifferentOddEvenCheck");
        var scaleWithDocumentCheck = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("PageSetup_ScaleWithDocument")),
            IsChecked = initialHeaderFooter.ScaleWithDocument,
        };
        ApplyPageLayoutCheckBoxChrome(scaleWithDocumentCheck);
        AutomationProperties.SetAutomationId(scaleWithDocumentCheck, "PageSetupScaleWithDocumentCheck");
        var alignWithMarginsCheck = new CheckBox
        {
            Content = UiText.Get("PageSetup_AlignWithMargins"),
            IsChecked = initialHeaderFooter.AlignWithMargins,
        };
        ApplyPageLayoutCheckBoxChrome(alignWithMarginsCheck);
        AutomationProperties.SetAutomationId(alignWithMarginsCheck, "PageSetupAlignWithMarginsCheck");

        HeaderFooterEditorState CaptureHeaderFooterEditorState() => new(
            header, footer, firstPageHeader, firstPageFooter, evenPageHeader, evenPageFooter,
            headerPictures, footerPictures, firstPageHeaderPictures, firstPageFooterPictures,
            evenPageHeaderPictures, evenPageFooterPictures,
            differentFirstPageCheck.IsChecked == true, differentOddEvenCheck.IsChecked == true,
            scaleWithDocumentCheck.IsChecked == true, alignWithMarginsCheck.IsChecked == true);

        void ApplyHeaderFooterEditorState(HeaderFooterEditorState edited)
        {
            header = edited.Header;
            footer = edited.Footer;
            firstPageHeader = edited.FirstPageHeader;
            firstPageFooter = edited.FirstPageFooter;
            evenPageHeader = edited.EvenPageHeader;
            evenPageFooter = edited.EvenPageFooter;
            headerPictures = edited.HeaderPictures;
            footerPictures = edited.FooterPictures;
            firstPageHeaderPictures = edited.FirstPageHeaderPictures;
            firstPageFooterPictures = edited.FirstPageFooterPictures;
            evenPageHeaderPictures = edited.EvenPageHeaderPictures;
            evenPageFooterPictures = edited.EvenPageFooterPictures;
            differentFirstPageCheck.IsChecked = edited.DifferentFirstPage;
            differentOddEvenCheck.IsChecked = edited.DifferentOddEvenPages;
            scaleWithDocumentCheck.IsChecked = edited.ScaleWithDocument;
            alignWithMarginsCheck.IsChecked = edited.AlignWithMargins;
            headerPresetBox.SelectedIndex = PageSetupDialogPlanner.ResolveHeaderPresetIndex(header);
            footerPresetBox.SelectedIndex = PageSetupDialogPlanner.ResolveFooterPresetIndex(footer);
            UpdateHeaderFooterPreview();
        }

        async Task EditHeaderFooterAsync(bool openFooterTab)
        {
            var edited = await ShowHeaderFooterEditorDialogAsync(
                CaptureHeaderFooterEditorState(),
                openFooterTab);
            if (edited is null)
                return;

            ApplyHeaderFooterEditorState(edited);
        }

        customHeaderButton.Click += async (_, _) => await EditHeaderFooterAsync(openFooterTab: false);
        customFooterButton.Click += async (_, _) => await EditHeaderFooterAsync(openFooterTab: true);
        UpdateHeaderFooterPreview();

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
        PageSetupDialogFields ReadFields() => PageSetupDialogPlanner.BuildFields(initial, new PageSetupDialogSurfaceInput
        {
            OrientationIndex = orientationBox.SelectedIndex,
            PaperSizeIndex = paperBox.SelectedIndex,
            LeftMarginText = leftMarginBox.Text ?? "",
            RightMarginText = rightMarginBox.Text ?? "",
            TopMarginText = topMarginBox.Text ?? "",
            BottomMarginText = bottomMarginBox.Text ?? "",
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
            HeaderFooter = CaptureHeaderFooterEditorState(),
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

        var marginsGrid = new Grid
        {
            Margin = new Thickness(10),
            ColumnDefinitions = new ColumnDefinitions("120,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
        };

        void AddMarginField(int row, string label, TextBox value)
        {
            var labelControl = PageSetupLabel(label);
            labelControl.Margin = new Thickness(0, 0, 8, 8);
            value.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(labelControl, row);
            Grid.SetColumn(labelControl, 0);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            marginsGrid.Children.Add(labelControl);
            marginsGrid.Children.Add(value);
        }

        AddMarginField(0, UiText.Get("PageSetup_Left"), leftMarginBox);
        AddMarginField(1, UiText.Get("PageSetup_Right"), rightMarginBox);
        AddMarginField(2, UiText.Get("PageSetup_Top"), topMarginBox);
        AddMarginField(3, UiText.Get("PageSetup_Bottom"), bottomMarginBox);
        AddMarginField(4, UiText.Get("PageSetup_Header"), headerMarginBox);
        AddMarginField(5, UiText.Get("PageSetup_Footer"), footerMarginBox);

        Grid.SetRow(centerHorizontallyCheck, 6);
        Grid.SetColumn(centerHorizontallyCheck, 1);
        centerHorizontallyCheck.Margin = new Thickness(0, 4, 0, 8);
        Grid.SetRow(centerVerticallyCheck, 7);
        Grid.SetColumn(centerVerticallyCheck, 1);
        marginsGrid.Children.Add(centerHorizontallyCheck);
        marginsGrid.Children.Add(centerVerticallyCheck);

        var marginsTab = new TabItem
        {
            Header = StripDisplayMnemonic(UiText.Get("PageSetup_MarginsTab")),
            Content = new ScrollViewer
            {
                Content = marginsGrid,
            },
        };

        var previewGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("72,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(4),
        };
        var previewHeaderLabel = new TextBlock
        {
            Text = UiText.Get("PageSetup_Header2"),
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 8, 6),
        };
        var previewFooterLabel = new TextBlock
        {
            Text = UiText.Get("PageSetup_Footer2"),
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(previewHeaderLabel, 0);
        Grid.SetRow(previewHeaderLabel, 0);
        Grid.SetColumn(headerPreviewText, 1);
        Grid.SetRow(headerPreviewText, 0);
        Grid.SetColumn(previewFooterLabel, 0);
        Grid.SetRow(previewFooterLabel, 1);
        Grid.SetColumn(footerPreviewText, 1);
        Grid.SetRow(footerPreviewText, 1);
        previewGrid.Children.Add(previewHeaderLabel);
        previewGrid.Children.Add(headerPreviewText);
        previewGrid.Children.Add(previewFooterLabel);
        previewGrid.Children.Add(footerPreviewText);
        var scaleAlignOptions = new StackPanel
        {
            Spacing = 8,
            Children = { scaleWithDocumentCheck, alignWithMarginsCheck },
        };

        var headerPresetLabel = PageSetupLabel(UiText.Get("PageSetup_HeaderPreset"));
        headerPresetLabel.Margin = new Thickness(0, 0, 8, 8);
        var footerPresetLabel = PageSetupLabel(UiText.Get("PageSetup_FooterPreset"));
        footerPresetLabel.Margin = new Thickness(0, 0, 8, 8);
        headerPresetBox.Margin = new Thickness(0, 0, 0, 8);
        footerPresetBox.Margin = new Thickness(0, 0, 0, 8);
        var previewGroup = new GroupBox
        {
            Header = UiText.Get("PageSetup_Preview"),
            Height = 68,
            MinHeight = 68,
            MaxHeight = 68,
            Margin = new Thickness(0, 2, 0, 10),
            Content = previewGrid,
        };
        var customButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 12),
            Children = { customHeaderButton, customFooterButton },
        };
        foreach (var check in new[]
        {
            differentFirstPageCheck,
            differentOddEvenCheck,
            scaleWithDocumentCheck,
            alignWithMarginsCheck,
        })
        {
            check.MinHeight = 18;
            check.MaxHeight = 18;
        }
        differentFirstPageCheck.Margin = new Thickness(0, 0, 0, 5);
        differentOddEvenCheck.Margin = new Thickness(0, 0, 0, 5);
        scaleWithDocumentCheck.Margin = new Thickness(0);
        alignWithMarginsCheck.Margin = new Thickness(0);
        scaleAlignOptions.Spacing = 5;

        var headerFooterGrid = new Grid
        {
            Margin = new Thickness(14, 4, 14, 14),
            ColumnDefinitions = new ColumnDefinitions("120,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
        };
        Grid.SetRow(headerPresetLabel, 0); Grid.SetColumn(headerPresetLabel, 0);
        Grid.SetRow(headerPresetBox, 0); Grid.SetColumn(headerPresetBox, 1);
        Grid.SetRow(footerPresetLabel, 1); Grid.SetColumn(footerPresetLabel, 0);
        Grid.SetRow(footerPresetBox, 1); Grid.SetColumn(footerPresetBox, 1);
        Grid.SetRow(previewGroup, 2); Grid.SetColumn(previewGroup, 0); Grid.SetColumnSpan(previewGroup, 2);
        Grid.SetRow(customButtons, 3); Grid.SetColumn(customButtons, 1);
        Grid.SetRow(differentFirstPageCheck, 4); Grid.SetColumn(differentFirstPageCheck, 1);
        Grid.SetRow(differentOddEvenCheck, 5); Grid.SetColumn(differentOddEvenCheck, 1);
        Grid.SetRow(scaleAlignOptions, 6); Grid.SetColumn(scaleAlignOptions, 1);
        headerFooterGrid.Children.Add(headerPresetLabel);
        headerFooterGrid.Children.Add(headerPresetBox);
        headerFooterGrid.Children.Add(footerPresetLabel);
        headerFooterGrid.Children.Add(footerPresetBox);
        headerFooterGrid.Children.Add(previewGroup);
        headerFooterGrid.Children.Add(customButtons);
        headerFooterGrid.Children.Add(differentFirstPageCheck);
        headerFooterGrid.Children.Add(differentOddEvenCheck);
        headerFooterGrid.Children.Add(scaleAlignOptions);

        var headerFooterTab = new TabItem
        {
            Header = StripDisplayMnemonic(UiText.Get("PageSetup_HeaderFooterTab")),
            Content = new ScrollViewer { Content = headerFooterGrid },
        };

        // Keep the Sheet tab's three-column grid in lockstep with the WPF XAML.  The stacked
        // Avalonia form made the 600x560 dialog scroll, hid the range-picker buttons, and changed
        // the visual order of the page-order/print-option controls.
        var sheetGrid = new Grid
        {
            Margin = new Thickness(10),
            ColumnDefinitions = new ColumnDefinitions("150,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
        };

        void AddSheetLabel(int row, string text, Thickness margin)
        {
            var label = PageSetupLabel(text);
            label.Margin = margin;
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            sheetGrid.Children.Add(label);
        }

        void AddSheetValue(int row, Control value, Button? picker = null, Thickness? margin = null)
        {
            value.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;
            value.Margin = margin ?? new Thickness(0, 0, picker is null ? 0 : 6, 8);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            sheetGrid.Children.Add(value);

            if (picker is null)
                return;

            picker.Width = 24;
            picker.MinWidth = 24;
            picker.MaxWidth = 24;
            picker.Height = 24;
            picker.MinHeight = 24;
            picker.MaxHeight = 24;
            picker.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(picker, row);
            Grid.SetColumn(picker, 2);
            sheetGrid.Children.Add(picker);
        }

        AddSheetLabel(0, UiText.Get("PageSetup_PrintArea"), new Thickness(0, 0, 8, 8));
        AddSheetValue(0, printAreaBox, printAreaPicker);
        AddSheetLabel(1, UiText.Get("PageSetup_RepeatRows"), new Thickness(0, 0, 8, 8));
        AddSheetValue(1, repeatRowsBox, repeatRowsPicker);
        AddSheetLabel(2, UiText.Get("PageSetup_RepeatColumns"), new Thickness(0, 0, 8, 8));
        AddSheetValue(2, repeatColumnsBox, repeatColumnsPicker);

        AddSheetValue(3, gridlinesCheck, margin: new Thickness(0, 0, 0, 8));
        AddSheetValue(4, headingsCheck, margin: new Thickness(0, 0, 0, 8));
        AddSheetLabel(5, UiText.Get("PageSetup_PageOrder"), new Thickness(0, 0, 8, 0));
        AddSheetValue(5, pageOrderBox, margin: new Thickness(0));
        AddSheetValue(6, blackAndWhiteCheck, margin: new Thickness(0, 8, 0, 8));
        AddSheetValue(7, draftQualityCheck, margin: new Thickness(0));
        AddSheetLabel(8, UiText.Get("PageSetup_CellErrorsAs"), new Thickness(0, 8, 8, 0));
        AddSheetValue(8, cellErrorsBox, margin: new Thickness(0, 8, 0, 0));
        AddSheetLabel(9, UiText.Get("PageSetup_Comments"), new Thickness(0, 8, 8, 0));
        AddSheetValue(9, commentsBox, margin: new Thickness(0, 8, 0, 0));

        var sheetTab = new TabItem
        {
            Header = StripDisplayMnemonic(UiText.Get("PageSetup_SheetTab")),
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                Content = sheetGrid,
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
                PageSetupDialogFocusTarget.Margins => leftMarginBox,
                PageSetupDialogFocusTarget.LeftMargin => leftMarginBox,
                PageSetupDialogFocusTarget.RightMargin => rightMarginBox,
                PageSetupDialogFocusTarget.TopMargin => topMarginBox,
                PageSetupDialogFocusTarget.BottomMargin => bottomMarginBox,
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
                HasSeparateMarginFields = true,
                LeftMarginText = leftMarginBox.Text ?? "",
                RightMarginText = rightMarginBox.Text ?? "",
                TopMarginText = topMarginBox.Text ?? "",
                BottomMarginText = bottomMarginBox.Text ?? "",
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

        void Accept(PageSetupDialogAction requestedAction)
        {
            var fields = ReadFields();
            var submission = PageSetupSubmissionPlanner.TryBuild(_session.ActiveSheet, fields, requestedAction);
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

            result = (fields, submission.Submission!.RequestedAction);
            dialog.Close();
        }

        okButton.Click += (_, _) => Accept(PageSetupDialogAction.Ok);
        cancelButton.Click += (_, _) => dialog.Close();
        printButton.Click += (_, _) => Accept(PageSetupDialogAction.Print);
        printPreviewButton.Click += (_, _) => Accept(PageSetupDialogAction.PrintPreview);
        optionsButton.Click += (_, _) => Accept(PageSetupDialogAction.Options);

        DockPanel.SetDock(buttonRow, AvaloniaDock.Bottom);
        DockPanel.SetDock(validationText, AvaloniaDock.Bottom);
        var root = new DockPanel
        {
            Margin = new Thickness(12),
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

    private async Task<HeaderFooterEditorState?> ShowHeaderFooterEditorDialogAsync(
        HeaderFooterEditorState initial,
        bool openFooterTab)
    {
        var dialog = new Window
        {
            Title = UiText.Get("HeaderFooter_HeaderAndFooter"),
            Width = 760,
            Height = 600,
            MinWidth = 700,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var editors = new Dictionary<HeaderFooterEditorTarget, TextBox>();
        TextBox? activeEditor = null;
        HeaderFooterEditorTarget? activeTarget = null;
        TabControl? tabs = null;
        var editedState = initial;

        HeaderFooterEditorTarget ActiveTarget() => activeTarget ??
            new(openFooterTab ? HeaderFooterEditorScope.Footer : HeaderFooterEditorScope.Header, HeaderFooterEditorSection.Center);

        Button? formatPictureButton = null;
        TextBlock? pictureTargetStatus = null;

        TextBox CreateEditor(HeaderFooterEditorTarget target, string text)
        {
            var box = new TextBox { Text = text, MinHeight = 24 };
            ApplyPageLayoutTextBoxChrome(box);
            AutomationProperties.SetAutomationId(box, $"HeaderFooter{target.Scope}{target.Section}Box");
            box.GotFocus += (_, _) =>
            {
                activeEditor = box;
                activeTarget = target;
                RefreshPictureTargetState();
            };
            editors[target] = box;
            return box;
        }

        GroupBox CreateScopeGroup(
            HeaderFooterEditorScope scope,
            string titleKey,
            WorksheetHeaderFooter value,
            bool isVisible)
        {
            var left = CreateEditor(new HeaderFooterEditorTarget(scope, HeaderFooterEditorSection.Left), value.Left);
            var center = CreateEditor(new HeaderFooterEditorTarget(scope, HeaderFooterEditorSection.Center), value.Center);
            var right = CreateEditor(new HeaderFooterEditorTarget(scope, HeaderFooterEditorSection.Right), value.Right);
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("128,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
                Margin = new Thickness(8),
                RowSpacing = 6,
            };
            var rows = new[]
            {
                (UiText.Get(HeaderFooterEditorPlanner.EditorFieldLabelResourceKey(
                    new HeaderFooterEditorTarget(scope, HeaderFooterEditorSection.Left))), left),
                (UiText.Get(HeaderFooterEditorPlanner.EditorFieldLabelResourceKey(
                    new HeaderFooterEditorTarget(scope, HeaderFooterEditorSection.Center))), center),
                (UiText.Get(HeaderFooterEditorPlanner.EditorFieldLabelResourceKey(
                    new HeaderFooterEditorTarget(scope, HeaderFooterEditorSection.Right))), right),
            };
            for (var row = 0; row < rows.Length; row++)
            {
                var label = PageSetupLabel(rows[row].Item1);
                Grid.SetRow(label, row);
                Grid.SetColumn(label, 0);
                Grid.SetRow(rows[row].Item2, row);
                Grid.SetColumn(rows[row].Item2, 1);
                grid.Children.Add(label);
                grid.Children.Add(rows[row].Item2);
            }

            return new GroupBox
            {
                Header = UiText.Get(titleKey),
                Content = grid,
                IsVisible = isVisible,
                Margin = new Thickness(0, 0, 0, 8),
            };
        }

        var firstPageCheck = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("HeaderFooter_DifferentFirstPage")),
            IsChecked = initial.DifferentFirstPage,
            Margin = new Thickness(0, 0, 18, 6),
        };
        AutomationProperties.SetAutomationId(firstPageCheck, "HeaderFooterDifferentFirstPageCheck");
        var oddEvenCheck = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("HeaderFooter_DifferentOddAndEvenPages")),
            IsChecked = initial.DifferentOddEvenPages,
            Margin = new Thickness(0, 0, 18, 6),
        };
        AutomationProperties.SetAutomationId(oddEvenCheck, "HeaderFooterDifferentOddEvenCheck");
        var scaleCheck = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("HeaderFooter_ScaleWithDocument")),
            IsChecked = initial.ScaleWithDocument,
            Margin = new Thickness(0, 0, 18, 6),
        };
        var alignCheck = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("HeaderFooter_AlignWithPageMargins")),
            IsChecked = initial.AlignWithMargins,
            Margin = new Thickness(0, 0, 0, 6),
        };
        foreach (var check in new[] { firstPageCheck, oddEvenCheck, scaleCheck, alignCheck })
            ApplyPageLayoutCheckBoxChrome(check);

        var headerPreset = new ComboBox
        {
            ItemsSource = HeaderFooterPresetCatalog.HeaderChoices,
            SelectedIndex = PageSetupDialogPlanner.ResolveHeaderPresetIndex(initial.Header),
            MinWidth = 320,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        headerPreset.ItemTemplate = HeaderFooterPresetTemplate();
        var footerPreset = new ComboBox
        {
            ItemsSource = HeaderFooterPresetCatalog.FooterChoices,
            SelectedIndex = PageSetupDialogPlanner.ResolveFooterPresetIndex(initial.Footer),
            MinWidth = 320,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        footerPreset.ItemTemplate = HeaderFooterPresetTemplate();
        ApplyPageLayoutComboBoxChrome(headerPreset);
        ApplyPageLayoutComboBoxChrome(footerPreset);
        AutomationProperties.SetAutomationId(headerPreset, "HeaderFooterHeaderPresetBox");
        AutomationProperties.SetAutomationId(footerPreset, "HeaderFooterFooterPresetBox");

        var headerGroup = CreateScopeGroup(HeaderFooterEditorScope.Header, "HeaderFooter_Header", initial.Header, true);
        var firstHeaderGroup = CreateScopeGroup(HeaderFooterEditorScope.FirstPageHeader, "HeaderFooter_FirstPageHeader", initial.FirstPageHeader, initial.DifferentFirstPage);
        var evenHeaderGroup = CreateScopeGroup(HeaderFooterEditorScope.EvenPageHeader, "HeaderFooter_EvenPageHeader", initial.EvenPageHeader, initial.DifferentOddEvenPages);
        var footerGroup = CreateScopeGroup(HeaderFooterEditorScope.Footer, "HeaderFooter_Footer", initial.Footer, true);
        var firstFooterGroup = CreateScopeGroup(HeaderFooterEditorScope.FirstPageFooter, "HeaderFooter_FirstPageFooter", initial.FirstPageFooter, initial.DifferentFirstPage);
        var evenFooterGroup = CreateScopeGroup(HeaderFooterEditorScope.EvenPageFooter, "HeaderFooter_EvenPageFooter", initial.EvenPageFooter, initial.DifferentOddEvenPages);

        WorksheetHeaderFooter ReadEditorScope(HeaderFooterEditorScope scope)
        {
            var left = editors[new HeaderFooterEditorTarget(scope, HeaderFooterEditorSection.Left)].Text ?? "";
            var center = editors[new HeaderFooterEditorTarget(scope, HeaderFooterEditorSection.Center)].Text ?? "";
            var right = editors[new HeaderFooterEditorTarget(scope, HeaderFooterEditorSection.Right)].Text ?? "";
            return new WorksheetHeaderFooter(left, center, right);
        }

        var headerPresetLabel = PageSetupLabel(UiText.Get("HeaderFooter_HeaderPreset"));
        var headerPresetRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("116,*"),
            Margin = new Thickness(0, 0, 0, 12),
            Children = { headerPresetLabel, headerPreset },
        };
        Grid.SetColumn(headerPresetLabel, 0);
        Grid.SetColumn(headerPreset, 1);
        var headerScroll = new ScrollViewer
        {
            Content = new StackPanel
            {
                Children = { headerGroup, firstHeaderGroup, evenHeaderGroup },
            },
        };
        var headerContent = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { headerPresetRow, headerScroll },
        };
        Grid.SetRow(headerPresetRow, 0);
        Grid.SetRow(headerScroll, 1);
        var headerTab = new TabItem
        {
            Header = StripDisplayMnemonic(UiText.Get("HeaderFooter_Header")),
            Content = headerContent,
        };
        var footerPresetLabel = PageSetupLabel(UiText.Get("HeaderFooter_FooterPreset"));
        var footerPresetRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("116,*"),
            Margin = new Thickness(0, 0, 0, 12),
            Children = { footerPresetLabel, footerPreset },
        };
        Grid.SetColumn(footerPresetLabel, 0);
        Grid.SetColumn(footerPreset, 1);
        var footerScroll = new ScrollViewer
        {
            Content = new StackPanel
            {
                Children = { footerGroup, firstFooterGroup, evenFooterGroup },
            },
        };
        var footerContent = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { footerPresetRow, footerScroll },
        };
        Grid.SetRow(footerPresetRow, 0);
        Grid.SetRow(footerScroll, 1);
        var footerTab = new TabItem
        {
            Header = StripDisplayMnemonic(UiText.Get("HeaderFooter_Footer")),
            Content = footerContent,
        };
        tabs = new TabControl { Items = { headerTab, footerTab }, SelectedIndex = openFooterTab ? 1 : 0 };
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(tabs);
        AutomationProperties.SetAutomationId(tabs, "HeaderFooterTabs");

        HeaderFooterEditorTarget SelectedTabCenter() => new(
            tabs!.SelectedIndex == 1 ? HeaderFooterEditorScope.Footer : HeaderFooterEditorScope.Header,
            HeaderFooterEditorSection.Center);

        HeaderFooterEditorTarget CoerceTargetToVisibleTab(HeaderFooterEditorTarget target)
        {
            var selectedScope = tabs!.SelectedIndex == 1 ? HeaderFooterEditorScope.Footer : HeaderFooterEditorScope.Header;
            return HeaderFooterEditorPlanner.CoerceToEnabledTargetForTab(
                target,
                selectedScope,
                firstPageCheck.IsChecked == true,
                oddEvenCheck.IsChecked == true);
        }

        void CoerceActiveEditor()
        {
            var target = CoerceTargetToVisibleTab(activeTarget ?? SelectedTabCenter());
            activeTarget = target;
            activeEditor = editors[target];
        }

        void RefreshOptionalSections()
        {
            firstHeaderGroup.IsVisible = firstPageCheck.IsChecked == true;
            firstFooterGroup.IsVisible = firstPageCheck.IsChecked == true;
            evenHeaderGroup.IsVisible = oddEvenCheck.IsChecked == true;
            evenFooterGroup.IsVisible = oddEvenCheck.IsChecked == true;
            CoerceActiveEditor();
            RefreshPictureTargetState();
        }

        void RefreshPictureTargetState()
        {
            if (formatPictureButton is null || pictureTargetStatus is null)
                return;

            var target = CoerceTargetToVisibleTab(ActiveTarget());
            activeTarget = target;
            activeEditor = editors[target];
            var hasPicture = HeaderFooterEditorPlanner.GetPicture(
                editedState.GetPictures(target.Scope),
                target.Section) is not null;
            formatPictureButton.IsEnabled = hasPicture;
            var label = HeaderFooterEditorPlanner.ComposeTargetLabel(
                UiText.Get(HeaderFooterEditorPlanner.ScopeLabelResourceKey(target.Scope)),
                UiText.Get(HeaderFooterEditorPlanner.SectionLabelResourceKey(target.Section)),
                UiText.Format);
            ToolTip.SetTip(formatPictureButton, hasPicture
                ? UiText.Format("HeaderFooterPicture_FormatPictureToolTip", label)
                : UiText.Format("HeaderFooterPicture_InsertBeforeFormattingToolTip", label));
            pictureTargetStatus.Text = hasPicture
                ? UiText.Format("HeaderFooterPicture_TargetHasPictureStatus", label)
                : UiText.Format("HeaderFooterPicture_TargetHasNoPictureStatus", label);
        }

        tabs.SelectionChanged += (_, _) =>
        {
            CoerceActiveEditor();
            RefreshPictureTargetState();
        };
        firstPageCheck.IsCheckedChanged += (_, _) => RefreshOptionalSections();
        oddEvenCheck.IsCheckedChanged += (_, _) => RefreshOptionalSections();
        headerPreset.SelectionChanged += (_, _) =>
        {
            if (headerPreset.SelectedItem is HeaderFooterPresetChoice choice)
            {
                var current = ReadEditorScope(HeaderFooterEditorScope.Header);
                var value = PageSetupDialogPlanner.ApplyHeaderPreset(current, choice);
                editors[new HeaderFooterEditorTarget(HeaderFooterEditorScope.Header, HeaderFooterEditorSection.Center)].Text = value.Center;
            }
        };
        footerPreset.SelectionChanged += (_, _) =>
        {
            if (footerPreset.SelectedItem is HeaderFooterPresetChoice choice)
            {
                var current = ReadEditorScope(HeaderFooterEditorScope.Footer);
                var value = PageSetupDialogPlanner.ApplyFooterPreset(current, choice);
                editors[new HeaderFooterEditorTarget(HeaderFooterEditorScope.Footer, HeaderFooterEditorSection.Center)].Text = value.Center;
            }
        };

        var tokenButtons = new WrapPanel { Orientation = Orientation.Horizontal };
        void ApplyTokenButtonChrome(Button button, double minWidth)
        {
            ApplyPageLayoutButtonChrome(button, minWidth);
            button.Height = 22;
            button.MinHeight = 22;
            button.MaxHeight = 22;
        }
        foreach (var token in new[]
        {
            ("HeaderFooter_PageNumber", "&[Page]", 96d),
            ("HeaderFooter_NumberOfPages", "&[Pages]", 112d),
            ("HeaderFooter_Date2", "&[Date]", 72d),
            ("HeaderFooter_Time2", "&[Time]", 72d),
            ("HeaderFooter_FilePath2", "&[Path]&[File]", 88d),
            ("HeaderFooter_FileName2", "&[File]", 88d),
            ("HeaderFooter_SheetName2", "&[Tab]", 92d),
        })
        {
            var button = new Button
            {
                Content = UiText.Get(token.Item1),
                Tag = token.Item2,
                Margin = new Thickness(0, 0, 6, 6),
            };
            ApplyTokenButtonChrome(button, token.Item3);
            AutomationProperties.SetAutomationId(button, $"HeaderFooterToken{token.Item1}");
            button.Click += (_, _) =>
            {
                if (activeEditor is null)
                    activeEditor = editors[new HeaderFooterEditorTarget(
                        tabs.SelectedIndex == 1 ? HeaderFooterEditorScope.Footer : HeaderFooterEditorScope.Header,
                        HeaderFooterEditorSection.Center)];
                var caret = activeEditor.CaretIndex;
                var value = HeaderFooterEditorPlanner.InsertToken(activeEditor.Text, caret, token.Item2);
                activeEditor.Text = value;
                activeEditor.CaretIndex = Math.Min(caret + token.Item2.Length, value.Length);
                activeEditor.Focus();
            };
            tokenButtons.Children.Add(button);
        }

        var pictureButton = new Button
        {
            Content = UiText.Get("HeaderFooter_Picture"),
            Margin = new Thickness(0, 0, 6, 6),
        };
        ApplyTokenButtonChrome(pictureButton, 80);
        AutomationProperties.SetAutomationId(pictureButton, "HeaderFooterPictureButton");
        pictureButton.Click += async (_, _) =>
        {
            var target = ActiveTarget();
            var editor = editors[target];
            var caret = editor.CaretIndex;
            var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
                StorageProvider,
                AvaloniaFilePickerOpenRequest.FromFileTypes(
                    UiText.Get("HeaderFooter_InsertPictureTitle"),
                    [PictureFileType]));
            if (file is null)
                return;

            byte[] bytes;
            await using (var stream = await file.OpenReadAsync())
            {
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                bytes = memory.ToArray();
            }
            var contentType = InsertPictureCommandFactory.ContentTypeForPath(file.Name) ?? "image/png";
            var picture = new WorksheetHeaderFooterPicture(bytes, contentType, file.Name, 160, 80);
            if (!HeaderFooterEditorPlanner.ContainsPictureToken(editor.Text))
            {
                var value = HeaderFooterEditorPlanner.InsertToken(editor.Text, caret, HeaderFooterEditorPlanner.PictureToken);
                editor.Text = value;
                editor.CaretIndex = Math.Min(caret + HeaderFooterEditorPlanner.PictureToken.Length, value.Length);
            }
            editedState = editedState.WithPictures(
                target.Scope,
                HeaderFooterEditorPlanner.SetPicture(
                    editedState.GetPictures(target.Scope),
                    target.Section,
                    picture));
            RefreshPictureTargetState();
            editor.Focus();
        };
        tokenButtons.Children.Add(pictureButton);

        formatPictureButton = new Button
        {
            Content = UiText.Get("HeaderFooter_FormatPicture"),
            Margin = new Thickness(0, 0, 6, 6),
        };
        ApplyTokenButtonChrome(formatPictureButton, 104);
        AutomationProperties.SetAutomationId(formatPictureButton, "HeaderFooterFormatPictureButton");
        formatPictureButton!.Click += async (_, _) =>
        {
            var target = ActiveTarget();
            if (editedState.GetPictures(target.Scope) is { } pictures &&
                HeaderFooterEditorPlanner.GetPicture(pictures, target.Section) is not null)
            {
                if (await ShowHeaderFooterPictureSetFormatDialogAsync(pictures, target.Section) is { } updated)
                {
                    editedState = editedState.WithPictures(target.Scope, updated);
                    RefreshPictureTargetState();
                }
            }
        };
        tokenButtons.Children.Add(formatPictureButton);

        pictureTargetStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(128, 128, 128),
        };
        RefreshPictureTargetState();

        var okButton = new Button { Content = UiText.Get("Common_Ok") };
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPageLayoutButtonChrome(okButton, 72, isDefault: true);
        ApplyPageLayoutButtonChrome(cancelButton, 72);
        okButton.Height = 22;
        okButton.MinHeight = 22;
        okButton.MaxHeight = 22;
        cancelButton.Height = 22;
        cancelButton.MinHeight = 22;
        cancelButton.MaxHeight = 22;
        AutomationProperties.SetAutomationId(okButton, "HeaderFooterEditorOkButton");
        AutomationProperties.SetAutomationId(cancelButton, "HeaderFooterEditorCancelButton");

        HeaderFooterEditorState? result = null;
        okButton.Click += (_, _) =>
        {
            result = HeaderFooterEditorPlanner.BuildResult(
                editedState,
                ReadEditorScope,
                firstPageCheck.IsChecked == true,
                oddEvenCheck.IsChecked == true,
                scaleCheck.IsChecked == true,
                alignCheck.IsChecked == true);
            dialog.Close();
        };
        cancelButton.Click += (_, _) => Dispatcher.UIThread.Post(
            () => dialog.Close(),
            DispatcherPriority.Input);

        var optionRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { firstPageCheck, oddEvenCheck, scaleCheck, alignCheck },
        };
        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Children = { okButton, cancelButton },
        };
        var tokenToolbar = new Border
        {
            BorderBrush = Brush(166, 166, 166),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new StackPanel
            {
                Children = { tokenButtons, pictureTargetStatus },
            },
        };
        var root = new Grid
        {
            Margin = new Thickness(12, 12, 28, 48),
            RowDefinitions = new RowDefinitions("*,Auto,Auto,Auto"),
        };
        Grid.SetRow(tabs, 0);
        Grid.SetRow(tokenToolbar, 1);
        Grid.SetRow(optionRow, 2);
        Grid.SetRow(actionRow, 3);
        root.Children.Add(tabs);
        root.Children.Add(tokenToolbar);
        root.Children.Add(optionRow);
        root.Children.Add(actionRow);
        dialog.Content = root;
        ConfigureDialogTabCycle(dialog, root);
        ConfigureDialogCancelOnEscape(dialog, cancelButton);
        dialog.Opened += (_, _) =>
        {
            activeTarget = SelectedTabCenter();
            activeEditor = editors[activeTarget.Value];
            activeEditor.Focus();
            activeEditor.SelectAll();
            RefreshPictureTargetState();
        };

        await dialog.ShowDialog(this);
        return result;
    }
}
