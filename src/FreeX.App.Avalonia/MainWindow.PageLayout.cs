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

        if (!PageSetupRangeParser.TryParsePrintArea(fields.PrintAreaText, sheet.Id, out var printArea))
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

    private async Task<PageSetupDialogFields?> ShowPageSetupDialogCoreAsync(PageSetupDialogFields initial)
    {
        PageSetupDialogFields? result = null;
        var dialog = new Window
        {
            Title = "Page Setup",
            Width = 440,
            Height = 540,
            MinWidth = 420,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var orientationBox = new ComboBox
        {
            ItemsSource = new[] { "Portrait", "Landscape" },
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

        var marginsBox = new TextBox { Text = initial.MarginsText, MinWidth = 220 };
        AutomationProperties.SetAutomationId(marginsBox, "PageSetupMarginsBox");
        AutomationProperties.SetHelpText(marginsBox, "Left, right, top, bottom in inches.");

        var adjustRadio = new RadioButton
        {
            Content = "Adjust to (% of normal size)",
            GroupName = "PageSetupScaling",
            IsChecked = initial.ScalingMode == PageSetupScalingMode.AdjustToPercent,
        };
        AutomationProperties.SetAutomationId(adjustRadio, "PageSetupAdjustToRadio");
        var scalePercentBox = new TextBox { Text = initial.ScalePercentText, MinWidth = 90 };
        AutomationProperties.SetAutomationId(scalePercentBox, "PageSetupScalePercentBox");

        var fitRadio = new RadioButton
        {
            Content = "Fit to (pages wide x tall)",
            GroupName = "PageSetupScaling",
            IsChecked = initial.ScalingMode == PageSetupScalingMode.FitToPages,
        };
        AutomationProperties.SetAutomationId(fitRadio, "PageSetupFitToRadio");
        var fitWideBox = new TextBox { Text = initial.FitToWideText, MinWidth = 70 };
        AutomationProperties.SetAutomationId(fitWideBox, "PageSetupFitWideBox");
        var fitTallBox = new TextBox { Text = initial.FitToTallText, MinWidth = 70 };
        AutomationProperties.SetAutomationId(fitTallBox, "PageSetupFitTallBox");

        var printAreaBox = new TextBox { Text = initial.PrintAreaText, MinWidth = 220 };
        AutomationProperties.SetAutomationId(printAreaBox, "PageSetupPrintAreaBox");
        AutomationProperties.SetHelpText(printAreaBox, "Cell range like A1:D20, or blank to clear.");

        var repeatRowsBox = new TextBox { Text = initial.RepeatRowsText, MinWidth = 220 };
        AutomationProperties.SetAutomationId(repeatRowsBox, "PageSetupRepeatRowsBox");
        AutomationProperties.SetHelpText(repeatRowsBox, "Row range like 1:2, or blank for none.");

        var repeatColumnsBox = new TextBox { Text = initial.RepeatColumnsText, MinWidth = 220 };
        AutomationProperties.SetAutomationId(repeatColumnsBox, "PageSetupRepeatColumnsBox");
        AutomationProperties.SetHelpText(repeatColumnsBox, "Column range like A:B, or blank for none.");

        var gridlinesCheck = new CheckBox { Content = "Print gridlines", IsChecked = initial.PrintGridlines };
        AutomationProperties.SetAutomationId(gridlinesCheck, "PageSetupPrintGridlinesCheck");
        var headingsCheck = new CheckBox { Content = "Print row and column headings", IsChecked = initial.PrintHeadings };
        AutomationProperties.SetAutomationId(headingsCheck, "PageSetupPrintHeadingsCheck");

        var pageOrderBox = new ComboBox
        {
            ItemsSource = new[] { "Down, then over", "Over, then down" },
            SelectedIndex = initial.PageOrder == WorksheetPageOrder.OverThenDown ? 1 : 0,
            MinWidth = 220,
        };
        AutomationProperties.SetAutomationId(pageOrderBox, "PageSetupPageOrderBox");

        var validationText = new TextBlock
        {
            Foreground = Brush(143, 74, 18),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(validationText, "PageSetupValidationText");

        var okButton = new Button { Content = "OK", MinWidth = 84, Padding = new Thickness(10, 4) };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 84, Padding = new Thickness(10, 4) };
        AutomationProperties.SetAutomationId(okButton, "PageSetupOkButton");
        AutomationProperties.SetAutomationId(cancelButton, "PageSetupCancelButton");

        PageSetupDialogFields ReadFields() => new()
        {
            Orientation = orientationBox.SelectedIndex == 1
                ? WorksheetPageOrientation.Landscape
                : WorksheetPageOrientation.Portrait,
            PaperSize = paperSizes[Math.Clamp(paperBox.SelectedIndex, 0, paperSizes.Count - 1)],
            MarginsText = marginsBox.Text ?? "",
            ScalingMode = fitRadio.IsChecked == true
                ? PageSetupScalingMode.FitToPages
                : PageSetupScalingMode.AdjustToPercent,
            ScalePercentText = scalePercentBox.Text ?? "",
            FitToWideText = fitWideBox.Text ?? "",
            FitToTallText = fitTallBox.Text ?? "",
            PrintAreaText = printAreaBox.Text ?? "",
            RepeatRowsText = repeatRowsBox.Text ?? "",
            RepeatColumnsText = repeatColumnsBox.Text ?? "",
            PrintGridlines = gridlinesCheck.IsChecked == true,
            PrintHeadings = headingsCheck.IsChecked == true,
            PageOrder = pageOrderBox.SelectedIndex == 1
                ? WorksheetPageOrder.OverThenDown
                : WorksheetPageOrder.DownThenOver,
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

            if (!PageSetupRangeParser.TryParsePrintArea(fields.PrintAreaText, _session.ActiveSheet.Id, out _))
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
            Children = { cancelButton, okButton },
        };

        var fitRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                fitRadio,
                fitWideBox,
                new TextBlock { Text = "wide x", VerticalAlignment = AvaloniaVerticalAlignment.Center },
                fitTallBox,
                new TextBlock { Text = "tall", VerticalAlignment = AvaloniaVerticalAlignment.Center },
            },
        };

        var adjustRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                adjustRadio,
                scalePercentBox,
                new TextBlock { Text = "%", VerticalAlignment = AvaloniaVerticalAlignment.Center },
            },
        };

        var content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Orientation" },
                orientationBox,
                new TextBlock { Text = "Paper size" },
                paperBox,
                new TextBlock { Text = "Margins (left, right, top, bottom inches)" },
                marginsBox,
                new TextBlock { Text = "Scaling", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) },
                adjustRow,
                fitRow,
                new TextBlock { Text = "Print area" },
                printAreaBox,
                new TextBlock { Text = "Rows to repeat at top" },
                repeatRowsBox,
                new TextBlock { Text = "Columns to repeat at left" },
                repeatColumnsBox,
                gridlinesCheck,
                headingsCheck,
                new TextBlock { Text = "Page order" },
                pageOrderBox,
                validationText,
                buttonRow,
            },
        };

        dialog.Content = new ScrollViewer { Content = content };
        dialog.Opened += (_, _) => orientationBox.Focus();

        await dialog.ShowDialog(this);
        return result;
    }
}
