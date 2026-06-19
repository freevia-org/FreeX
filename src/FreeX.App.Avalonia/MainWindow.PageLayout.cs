using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

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

    private bool _isPageBreakPreviewActive;

    private async Task ShowPageSetupDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var sheet = _session.ActiveSheet;
        var fields = await ShowPageSetupDialogCoreAsync(PageSetupDialogModel.FromSheet(sheet));
        if (fields is null)
            return;

        ApplyPageSetupFields(sheet, fields);
    }

    private void ApplyPageSetupFields(Sheet sheet, PageSetupDialogFields fields)
    {
        var build = PageSetupDialogModel.TryBuildCommand(sheet, fields);
        if (!build.Success)
        {
            ShowEditIssue(build.Error ?? "Page setup is invalid.");
            return;
        }

        if (!PageSetupDialogModel.TryParsePrintArea(fields.PrintAreaText, sheet.Id, out var printArea))
        {
            ShowEditIssue("Print area must be a cell range like A1:D20.");
            return;
        }

        var pageSetupResult = _session.ExecuteReviewCommand(build.Command!);
        if (!pageSetupResult.Success)
        {
            ShowEditIssue(pageSetupResult.ErrorMessage ?? "Page setup failed.");
            return;
        }

        var headerFooterCommand = PageSetupDialogModel.BuildHeaderFooterCommand(sheet, fields);
        var headerFooterResult = _session.ExecuteReviewCommand(headerFooterCommand);
        if (!headerFooterResult.Success)
        {
            ShowEditIssue(headerFooterResult.ErrorMessage ?? "Header and footer update failed.");
            return;
        }

        if (!ApplyPrintArea(sheet, printArea))
            return;

        RefreshShell("Page setup updated");
    }

    private bool ApplyPrintArea(Sheet sheet, GridRange? printArea)
    {
        var current = sheet.PrintArea is { } existing && existing.Start.Sheet == sheet.Id
            ? existing
            : (GridRange?)null;

        if (Equals(current, printArea))
            return true;

        var command = printArea is { } range
            ? (IWorkbookCommand)new SetPrintAreaCommand(sheet.Id, range)
            : new ClearPrintAreaCommand(sheet.Id);

        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Print area failed.");
            return false;
        }

        return true;
    }

    private void TogglePageBreakPreview()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        _isPageBreakPreviewActive = !_isPageBreakPreviewActive;
        RefreshShell(_isPageBreakPreviewActive ? "Page Break Preview on" : "Page Break Preview off");
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

        var rowHeaderWidth = showHeadings ? HeaderColumnWidth * zoomFactor : 0;
        var columnHeaderHeight = showHeadings ? HeaderRowHeight * zoomFactor : 0;
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
            actualHeight);

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
        new() { Text = text, VerticalAlignment = AvaloniaVerticalAlignment.Center };

    private static int PageSetupPresetIndex(string center)
    {
        var presets = PageSetupDialogModel.HeaderFooterPresets;
        for (var i = 0; i < presets.Count; i++)
        {
            if (string.Equals(presets[i], center, StringComparison.Ordinal))
                return i;
        }

        return 0;
    }

    private async Task<PageSetupDialogFields?> ShowPageSetupDialogCoreAsync(PageSetupDialogFields initial)
    {
        PageSetupDialogFields? result = null;
        var dialog = new Window
        {
            Title = UiText.Get("PageSetup_Title"),
            Width = 460,
            Height = 560,
            MinWidth = 440,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PageSetupDialog");

        // --- Page tab ---
        var orientationBox = new ComboBox
        {
            ItemsSource = new[] { UiText.Get("PageSetup_Portrait"), UiText.Get("PageSetup_Landscape") },
            SelectedIndex = initial.Orientation == WorksheetPageOrientation.Landscape ? 1 : 0,
            MinWidth = 220,
        };
        AutomationProperties.SetAutomationId(orientationBox, "PageSetupOrientationBox");

        var paperSizes = PageSetupDialogModel.PaperSizes;
        var paperBox = new ComboBox
        {
            ItemsSource = paperSizes.Select(PageSetupDialogModel.DescribePaperSize).ToList(),
            SelectedIndex = Math.Max(0, paperSizes.ToList().IndexOf(initial.PaperSize)),
            MinWidth = 220,
        };
        AutomationProperties.SetAutomationId(paperBox, "PageSetupPaperSizeBox");

        var adjustRadio = new RadioButton
        {
            Content = UiText.Get("PageSetup_AdjustTo"),
            GroupName = "PageSetupScaling",
            IsChecked = initial.ScalingMode == PageSetupScalingMode.AdjustToPercent,
        };
        AutomationProperties.SetAutomationId(adjustRadio, "PageSetupAdjustToRadio");
        var scalePercentBox = new TextBox { Text = initial.ScalePercentText, MinWidth = 90 };
        AutomationProperties.SetAutomationId(scalePercentBox, "PageSetupScalePercentBox");

        var fitRadio = new RadioButton
        {
            Content = UiText.Get("PageSetup_FitTo"),
            GroupName = "PageSetupScaling",
            IsChecked = initial.ScalingMode == PageSetupScalingMode.FitToPages,
        };
        AutomationProperties.SetAutomationId(fitRadio, "PageSetupFitToRadio");
        var fitWideBox = new TextBox { Text = initial.FitToWideText, MinWidth = 70 };
        AutomationProperties.SetAutomationId(fitWideBox, "PageSetupFitWideBox");
        var fitTallBox = new TextBox { Text = initial.FitToTallText, MinWidth = 70 };
        AutomationProperties.SetAutomationId(fitTallBox, "PageSetupFitTallBox");

        var firstPageNumberBox = new TextBox
        {
            Text = initial.FirstPageNumberText,
            MinWidth = 220,
            PlaceholderText = UiText.Get("PageSetup_Auto"),
        };
        AutomationProperties.SetAutomationId(firstPageNumberBox, "PageSetupFirstPageNumberBox");

        var printQualityBox = new TextBox
        {
            Text = initial.PrintQualityDpiText,
            MinWidth = 220,
            PlaceholderText = UiText.Get("PageSetup_Auto"),
        };
        AutomationProperties.SetAutomationId(printQualityBox, "PageSetupPrintQualityBox");
        AutomationProperties.SetHelpText(printQualityBox, UiText.Get("PageSetup_PrintQualityHelp"));

        // --- Margins tab ---
        var marginsBox = new TextBox { Text = initial.MarginsText, MinWidth = 220 };
        AutomationProperties.SetAutomationId(marginsBox, "PageSetupMarginsBox");
        AutomationProperties.SetHelpText(marginsBox, UiText.Get("PageSetup_MarginsHelp"));
        var headerMarginBox = new TextBox { Text = initial.HeaderMarginText, MinWidth = 220 };
        AutomationProperties.SetAutomationId(headerMarginBox, "PageSetupHeaderMarginBox");
        var footerMarginBox = new TextBox { Text = initial.FooterMarginText, MinWidth = 220 };
        AutomationProperties.SetAutomationId(footerMarginBox, "PageSetupFooterMarginBox");
        var centerHorizontallyCheck = new CheckBox
        {
            Content = UiText.Get("PageSetup_CenterHorizontally"),
            IsChecked = initial.CenterHorizontally,
        };
        AutomationProperties.SetAutomationId(centerHorizontallyCheck, "PageSetupCenterHorizontallyCheck");
        var centerVerticallyCheck = new CheckBox
        {
            Content = UiText.Get("PageSetup_CenterVertically"),
            IsChecked = initial.CenterVertically,
        };
        AutomationProperties.SetAutomationId(centerVerticallyCheck, "PageSetupCenterVerticallyCheck");

        // --- Header/Footer tab ---
        var presetLabels = new[]
        {
            UiText.Get("PageSetup_None"),
            UiText.Get("PageSetup_PresetPage"),
            UiText.Get("PageSetup_PresetPageOf"),
            UiText.Get("PageSetup_PresetSheetName"),
            UiText.Get("PageSetup_PresetFileName"),
            UiText.Get("PageSetup_PresetFileSheet"),
            UiText.Get("PageSetup_PresetDate"),
            UiText.Get("PageSetup_PresetTime"),
            UiText.Get("PageSetup_PresetDatePage"),
            UiText.Get("PageSetup_PresetConfidential"),
            UiText.Get("PageSetup_PresetFilePath"),
        };
        var headerPresetBox = new ComboBox
        {
            ItemsSource = presetLabels,
            SelectedIndex = PageSetupPresetIndex(initial.Header.Center),
            MinWidth = 260,
        };
        AutomationProperties.SetAutomationId(headerPresetBox, "PageSetupHeaderPresetBox");
        var footerPresetBox = new ComboBox
        {
            ItemsSource = presetLabels,
            SelectedIndex = PageSetupPresetIndex(initial.Footer.Center),
            MinWidth = 260,
        };
        AutomationProperties.SetAutomationId(footerPresetBox, "PageSetupFooterPresetBox");

        var headerLeftBox = new TextBox { Text = initial.Header.Left, MinWidth = 120 };
        AutomationProperties.SetAutomationId(headerLeftBox, "PageSetupCustomHeaderLeftBox");
        var headerCenterBox = new TextBox { Text = initial.Header.Center, MinWidth = 120 };
        AutomationProperties.SetAutomationId(headerCenterBox, "PageSetupCustomHeaderCenterBox");
        var headerRightBox = new TextBox { Text = initial.Header.Right, MinWidth = 120 };
        AutomationProperties.SetAutomationId(headerRightBox, "PageSetupCustomHeaderRightBox");
        var footerLeftBox = new TextBox { Text = initial.Footer.Left, MinWidth = 120 };
        AutomationProperties.SetAutomationId(footerLeftBox, "PageSetupCustomFooterLeftBox");
        var footerCenterBox = new TextBox { Text = initial.Footer.Center, MinWidth = 120 };
        AutomationProperties.SetAutomationId(footerCenterBox, "PageSetupCustomFooterCenterBox");
        var footerRightBox = new TextBox { Text = initial.Footer.Right, MinWidth = 120 };
        AutomationProperties.SetAutomationId(footerRightBox, "PageSetupCustomFooterRightBox");

        // A preset selection fills the matching custom center box (mirrors the WPF preset combo).
        headerPresetBox.SelectionChanged += (_, _) =>
        {
            var idx = headerPresetBox.SelectedIndex;
            if (idx >= 0 && idx < PageSetupDialogModel.HeaderFooterPresets.Count)
                headerCenterBox.Text = PageSetupDialogModel.HeaderFooterPresets[idx];
        };
        footerPresetBox.SelectionChanged += (_, _) =>
        {
            var idx = footerPresetBox.SelectedIndex;
            if (idx >= 0 && idx < PageSetupDialogModel.HeaderFooterPresets.Count)
                footerCenterBox.Text = PageSetupDialogModel.HeaderFooterPresets[idx];
        };

        var differentFirstPageCheck = new CheckBox
        {
            Content = UiText.Get("PageSetup_DifferentFirstPage"),
            IsChecked = initial.DifferentFirstPage,
        };
        AutomationProperties.SetAutomationId(differentFirstPageCheck, "PageSetupDifferentFirstPageCheck");
        var differentOddEvenCheck = new CheckBox
        {
            Content = UiText.Get("PageSetup_DifferentOddEven"),
            IsChecked = initial.DifferentOddEvenPages,
        };
        AutomationProperties.SetAutomationId(differentOddEvenCheck, "PageSetupDifferentOddEvenCheck");
        var scaleWithDocumentCheck = new CheckBox
        {
            Content = UiText.Get("PageSetup_ScaleWithDocument"),
            IsChecked = initial.ScaleHeaderFooterWithDocument,
        };
        AutomationProperties.SetAutomationId(scaleWithDocumentCheck, "PageSetupScaleWithDocumentCheck");
        var alignWithMarginsCheck = new CheckBox
        {
            Content = UiText.Get("PageSetup_AlignWithMargins"),
            IsChecked = initial.AlignHeaderFooterWithMargins,
        };
        AutomationProperties.SetAutomationId(alignWithMarginsCheck, "PageSetupAlignWithMarginsCheck");

        // --- Sheet tab ---
        var printAreaBox = new TextBox { Text = initial.PrintAreaText, MinWidth = 220 };
        AutomationProperties.SetAutomationId(printAreaBox, "PageSetupPrintAreaBox");
        AutomationProperties.SetHelpText(printAreaBox, UiText.Get("PageSetup_PrintAreaHelp"));

        var repeatRowsBox = new TextBox { Text = initial.RepeatRowsText, MinWidth = 220 };
        AutomationProperties.SetAutomationId(repeatRowsBox, "PageSetupRepeatRowsBox");
        AutomationProperties.SetHelpText(repeatRowsBox, UiText.Get("PageSetup_RepeatRowsHelp"));

        var repeatColumnsBox = new TextBox { Text = initial.RepeatColumnsText, MinWidth = 220 };
        AutomationProperties.SetAutomationId(repeatColumnsBox, "PageSetupRepeatColumnsBox");
        AutomationProperties.SetHelpText(repeatColumnsBox, UiText.Get("PageSetup_RepeatColumnsHelp"));

        var gridlinesCheck = new CheckBox { Content = UiText.Get("PageSetup_PrintGridlines"), IsChecked = initial.PrintGridlines };
        AutomationProperties.SetAutomationId(gridlinesCheck, "PageSetupPrintGridlinesCheck");
        var headingsCheck = new CheckBox { Content = UiText.Get("PageSetup_PrintHeadings"), IsChecked = initial.PrintHeadings };
        AutomationProperties.SetAutomationId(headingsCheck, "PageSetupPrintHeadingsCheck");
        var blackAndWhiteCheck = new CheckBox { Content = UiText.Get("PageSetup_BlackAndWhite"), IsChecked = initial.PrintBlackAndWhite };
        AutomationProperties.SetAutomationId(blackAndWhiteCheck, "PageSetupBlackAndWhiteCheck");
        var draftQualityCheck = new CheckBox { Content = UiText.Get("PageSetup_DraftQuality"), IsChecked = initial.PrintDraftQuality };
        AutomationProperties.SetAutomationId(draftQualityCheck, "PageSetupDraftQualityCheck");

        var pageOrderBox = new ComboBox
        {
            ItemsSource = new[] { UiText.Get("PageSetup_DownThenOver"), UiText.Get("PageSetup_OverThenDown") },
            SelectedIndex = initial.PageOrder == WorksheetPageOrder.OverThenDown ? 1 : 0,
            MinWidth = 220,
        };
        AutomationProperties.SetAutomationId(pageOrderBox, "PageSetupPageOrderBox");

        var cellErrorsBox = new ComboBox
        {
            ItemsSource = new[]
            {
                UiText.Get("PageSetup_ErrorsDisplayed"),
                UiText.Get("PageSetup_ErrorsBlank"),
                UiText.Get("PageSetup_ErrorsDash"),
                UiText.Get("PageSetup_ErrorsNotAvailable"),
            },
            SelectedIndex = initial.PrintErrorValue switch
            {
                WorksheetPrintErrorValue.Blank => 1,
                WorksheetPrintErrorValue.Dash => 2,
                WorksheetPrintErrorValue.NotAvailable => 3,
                _ => 0,
            },
            MinWidth = 220,
        };
        AutomationProperties.SetAutomationId(cellErrorsBox, "PageSetupCellErrorsBox");

        var commentsBox = new ComboBox
        {
            ItemsSource = new[]
            {
                UiText.Get("PageSetup_CommentsNone"),
                UiText.Get("PageSetup_CommentsAtEnd"),
                UiText.Get("PageSetup_CommentsAsDisplayed"),
            },
            SelectedIndex = initial.PrintComments switch
            {
                WorksheetPrintComments.AtEnd => 1,
                WorksheetPrintComments.AsDisplayed => 2,
                _ => 0,
            },
            MinWidth = 220,
        };
        AutomationProperties.SetAutomationId(commentsBox, "PageSetupCommentsBox");

        var validationText = new TextBlock
        {
            Foreground = Brush(143, 74, 18),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(validationText, "PageSetupValidationText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), MinWidth = 84, Padding = new Thickness(10, 4) };
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), MinWidth = 84, Padding = new Thickness(10, 4) };
        AutomationProperties.SetAutomationId(okButton, "PageSetupOkButton");
        AutomationProperties.SetAutomationId(cancelButton, "PageSetupCancelButton");

        WorksheetPrintErrorValue ReadErrorValue() => cellErrorsBox.SelectedIndex switch
        {
            1 => WorksheetPrintErrorValue.Blank,
            2 => WorksheetPrintErrorValue.Dash,
            3 => WorksheetPrintErrorValue.NotAvailable,
            _ => WorksheetPrintErrorValue.Displayed,
        };

        WorksheetPrintComments ReadComments() => commentsBox.SelectedIndex switch
        {
            1 => WorksheetPrintComments.AtEnd,
            2 => WorksheetPrintComments.AsDisplayed,
            _ => WorksheetPrintComments.None,
        };

        PageSetupDialogFields ReadFields() => new()
        {
            Orientation = orientationBox.SelectedIndex == 1
                ? WorksheetPageOrientation.Landscape
                : WorksheetPageOrientation.Portrait,
            PaperSize = paperSizes[Math.Clamp(paperBox.SelectedIndex, 0, paperSizes.Count - 1)],
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
            PrintErrorValue = ReadErrorValue(),
            PrintComments = ReadComments(),
            PageOrder = pageOrderBox.SelectedIndex == 1
                ? WorksheetPageOrder.OverThenDown
                : WorksheetPageOrder.DownThenOver,
            Header = new WorksheetHeaderFooter(headerLeftBox.Text ?? "", headerCenterBox.Text ?? "", headerRightBox.Text ?? ""),
            Footer = new WorksheetHeaderFooter(footerLeftBox.Text ?? "", footerCenterBox.Text ?? "", footerRightBox.Text ?? ""),
            DifferentFirstPage = differentFirstPageCheck.IsChecked == true,
            DifferentOddEvenPages = differentOddEvenCheck.IsChecked == true,
            ScaleHeaderFooterWithDocument = scaleWithDocumentCheck.IsChecked == true,
            AlignHeaderFooterWithMargins = alignWithMarginsCheck.IsChecked == true,
        };

        void Accept()
        {
            var fields = ReadFields();
            var build = PageSetupDialogModel.TryBuildCommand(_session.ActiveSheet, fields);
            if (!build.Success)
            {
                validationText.Text = build.Error;
                validationText.IsVisible = true;
                return;
            }

            if (!PageSetupDialogModel.TryParsePrintArea(fields.PrintAreaText, _session.ActiveSheet.Id, out _))
            {
                validationText.Text = "Print area must be a cell range like A1:D20.";
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

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { cancelButton, okButton },
        };

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
            Header = UiText.Get("PageSetup_PageTab"),
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
            Header = UiText.Get("PageSetup_MarginsTab"),
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
            Header = UiText.Get("PageSetup_HeaderFooterTab"),
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
                        new TextBlock { Text = UiText.Get("PageSetup_CustomHeader"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 6,
                            Children = { headerLeftBox, headerCenterBox, headerRightBox },
                        },
                        PageSetupLabel(UiText.Get("PageSetup_FooterPreset")),
                        footerPresetBox,
                        new TextBlock { Text = UiText.Get("PageSetup_CustomFooter"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 6,
                            Children = { footerLeftBox, footerCenterBox, footerRightBox },
                        },
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
            Header = UiText.Get("PageSetup_SheetTab"),
            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Margin = new Thickness(14),
                    Spacing = 8,
                    Children =
                    {
                        PageSetupLabel(UiText.Get("PageSetup_PrintArea")),
                        printAreaBox,
                        PageSetupLabel(UiText.Get("PageSetup_RepeatRows")),
                        repeatRowsBox,
                        PageSetupLabel(UiText.Get("PageSetup_RepeatColumns")),
                        repeatColumnsBox,
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
        AutomationProperties.SetAutomationId(tabs, "PageSetupTabs");

        DockPanel.SetDock(buttonRow, AvaloniaDock.Bottom);
        DockPanel.SetDock(validationText, AvaloniaDock.Bottom);
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(8),
            Children = { buttonRow, validationText, tabs },
        };
        dialog.Opened += (_, _) => orientationBox.Focus();

        await dialog.ShowDialog(this);
        return result;
    }
}
