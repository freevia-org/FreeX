using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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

    // A custom header/footer matches no preset, so the combo's selection box is templated with a null
    // choice; dereferencing it threw during layout, blanking the Page Setup Header/Footer tab and
    // crashing the Header & Footer editor for every sheet with custom header/footer text.
    private static FuncDataTemplate<HeaderFooterPresetChoice> HeaderFooterPresetTemplate() =>
        new((choice, _) => new TextBlock
        {
            Text = choice is null ? string.Empty : UiText.Get(choice.LabelResourceKey),
        }, supportsRecycling: true);

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

        AttachPageBreakLinePointerHandlers();
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

    // -------------------------------------------------------------------------------------------------------
    // Page Break Preview: dragging a manual break line to move or remove it (parity with the WPF shell's
    // GridView.Input.cs / GridView.HitTesting.cs HitTestPageBreakLine + CalculatePageBreakLineDragTarget,
    // wired through MainWindow.Host's OnPageBreakLineMoved -> PlanMovePageBreak). Mirrors the Tunnel-routed
    // capture pattern MainWindow.SplitPanePointer.cs uses for split-pane dividers: handlers are attached once
    // on _sheetGridHost so they see the pointer before the underlying cell-selection handlers do, and only
    // act while Page Break Preview is the active view mode.
    // -------------------------------------------------------------------------------------------------------

    /// <summary>Pixel tolerance for grabbing a manual page-break line, matching the WPF shell's
    /// <c>PageBreakLineHitZone</c>.</summary>
    private const double PageBreakLinePointerHitZone = 4;

    private bool _pageBreakLinePointerHandlersAttached;
    private PageBreakAxis? _pageBreakLineDragAxis;
    private uint _pageBreakLineDragIndex;
    private IPointer? _pageBreakLineDragPointer;

    private readonly record struct PageBreakLinePointerLayout(
        ViewportModel DisplayViewport,
        double RowHeaderWidth,
        double ColumnHeaderHeight,
        double Width,
        double Height);

    private void AttachPageBreakLinePointerHandlers()
    {
        if (_pageBreakLinePointerHandlersAttached)
            return;

        _pageBreakLinePointerHandlersAttached = true;
        _sheetGridHost.AddHandler(
            InputElement.PointerPressedEvent,
            PageBreakLinePointerPressed,
            RoutingStrategies.Tunnel);
        _sheetGridHost.AddHandler(
            InputElement.PointerMovedEvent,
            PageBreakLinePointerMoved,
            RoutingStrategies.Tunnel);
        _sheetGridHost.AddHandler(
            InputElement.PointerReleasedEvent,
            PageBreakLinePointerReleased,
            RoutingStrategies.Tunnel);
        _sheetGridHost.PointerCaptureLost += PageBreakLinePointerCaptureLost;
    }

    private void PageBreakLinePointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(_sheetGridHost).Properties.IsLeftButtonPressed ||
            !TryGetPageBreakLinePointerLayout(out var layout))
        {
            return;
        }

        var sheet = _session.ActiveSheet;
        var position = args.GetPosition(_sheetGridHost);
        if (HitTestPageBreakLinePointer(
                layout.DisplayViewport,
                sheet.RowPageBreaks,
                sheet.ColumnPageBreaks,
                position,
                layout.RowHeaderWidth,
                layout.ColumnHeaderHeight,
                layout.Width,
                layout.Height) is not { } hit)
        {
            return;
        }

        _pageBreakLineDragAxis = hit.Axis;
        _pageBreakLineDragIndex = hit.Index;
        _pageBreakLineDragPointer = args.Pointer;
        args.Pointer.Capture(_sheetGridHost);
        _sheetGridHost.Cursor = PageBreakLineDragCursor(hit.Axis);
        args.Handled = true;
    }

    private void PageBreakLinePointerMoved(object? sender, PointerEventArgs args)
    {
        if (_pageBreakLineDragAxis is not { } axis)
            return;

        _sheetGridHost.Cursor = PageBreakLineDragCursor(axis);
        args.Handled = true;
    }

    private void PageBreakLinePointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_pageBreakLineDragAxis is not { } axis)
            return;

        var originalIndex = _pageBreakLineDragIndex;
        if (TryGetPageBreakLinePointerLayout(out var layout))
        {
            var position = args.GetPosition(_sheetGridHost);
            var newIndex = CalculatePageBreakLineDragTarget(
                layout.DisplayViewport,
                axis,
                position,
                layout.RowHeaderWidth,
                layout.ColumnHeaderHeight,
                layout.Width,
                layout.Height);
            if (newIndex != originalIndex)
            {
                var sheet = _session.ActiveSheet;
                ExecutePageLayoutCommandWithShellRefresh(
                    CreatePageLayoutCommandSession().PlanMovePageBreak(
                        axis,
                        originalIndex,
                        newIndex,
                        sheet.RowPageBreaks,
                        sheet.ColumnPageBreaks));
            }
        }

        ClearPageBreakLinePointerCapture();
        args.Handled = true;
    }

    private void PageBreakLinePointerCaptureLost(object? sender, PointerCaptureLostEventArgs args) =>
        ClearPageBreakLinePointerCapture();

    private void ClearPageBreakLinePointerCapture()
    {
        _pageBreakLineDragAxis = null;
        _pageBreakLineDragIndex = 0;
        _pageBreakLineDragPointer = null;
        _sheetGridHost.Cursor = Cursor.Default;
    }

    private static Cursor PageBreakLineDragCursor(PageBreakAxis axis) =>
        axis == PageBreakAxis.Row
            ? new Cursor(StandardCursorType.SizeNorthSouth)
            : new Cursor(StandardCursorType.SizeWestEast);

    /// <summary>
    /// Resolves the display-space viewport + header offsets a page-break-line pointer gesture should hit
    /// test against, matching what <see cref="BuildPageBreakPreviewOverlay"/> renders (same zoom + minimum
    /// row/column size projection). Returns false outside Page Break Preview or when the grid has no
    /// measured extent yet.
    /// </summary>
    private bool TryGetPageBreakLinePointerLayout(out PageBreakLinePointerLayout layout)
    {
        if (_session.ViewMode != WorksheetViewMode.PageBreakPreview)
        {
            layout = default;
            return false;
        }

        var viewport = _session.Viewport;
        var zoomFactor = GetActiveZoomFactor();
        var showHeadings = _session.IsShowingHeadings;
        var rowHeaderWidth = showHeadings ? GetRowHeaderWidth(viewport, zoomFactor) : 0;
        var columnHeaderHeight = showHeadings ? GetColumnHeaderHeight(viewport, zoomFactor) : 0;
        var width = CalculateDisplayedGridWidth(viewport, showHeadings, zoomFactor);
        var height = CalculateDisplayedGridHeight(viewport, showHeadings, zoomFactor);
        if (width <= 0 || height <= 0)
        {
            layout = default;
            return false;
        }

        var displayViewport = PageBreakPreviewInstructionBuilder.ProjectToDisplaySpace(
            viewport,
            zoomFactor,
            MinimumDisplayedColumnWidth,
            MinimumDisplayedRowHeight);
        layout = new PageBreakLinePointerLayout(displayViewport, rowHeaderWidth, columnHeaderHeight, width, height);
        return true;
    }

    /// <summary>
    /// Hit-tests a manual (row or column) page-break line in Page Break Preview, mirroring the WPF shell's
    /// <c>GridView.HitTesting.cs HitTestPageBreakLine</c>: returns the axis and row/column index of the
    /// nearest manual break line within <see cref="PageBreakLinePointerHitZone"/> pixels of
    /// <paramref name="pos"/>, or null when the pointer is outside the grid/print area or not close enough
    /// to a line.
    /// </summary>
    internal static (PageBreakAxis Axis, uint Index)? HitTestPageBreakLinePointer(
        ViewportModel displayViewport,
        IReadOnlyCollection<uint>? rowPageBreaks,
        IReadOnlyCollection<uint>? columnPageBreaks,
        Point pos,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double width,
        double height)
    {
        if (pos.X < rowHeaderWidth || pos.Y < columnHeaderHeight || pos.X > width || pos.Y > height)
            return null;

        if (rowPageBreaks is { Count: > 0 })
        {
            var rowBreakLookup = rowPageBreaks as IReadOnlySet<uint> ?? new HashSet<uint>(rowPageBreaks);
            foreach (var metric in displayViewport.RowMetrics)
            {
                if (!rowBreakLookup.Contains(metric.Row))
                    continue;

                var y = metric.TopOffset + columnHeaderHeight;
                if (Math.Abs(pos.Y - y) <= PageBreakLinePointerHitZone)
                    return (PageBreakAxis.Row, metric.Row);
            }
        }

        if (columnPageBreaks is { Count: > 0 })
        {
            var columnBreakLookup = columnPageBreaks as IReadOnlySet<uint> ?? new HashSet<uint>(columnPageBreaks);
            foreach (var metric in displayViewport.ColMetrics)
            {
                if (!columnBreakLookup.Contains(metric.Col))
                    continue;

                var x = metric.LeftOffset + rowHeaderWidth;
                if (Math.Abs(pos.X - x) <= PageBreakLinePointerHitZone)
                    return (PageBreakAxis.Column, metric.Col);
            }
        }

        return null;
    }

    /// <summary>
    /// Given a page-break line drag on <paramref name="axis"/>, computes where dropping the pointer at
    /// <paramref name="pos"/> should place the break: the row/column of the nearest gridline under the
    /// pointer, or null when the pointer is outside the grid/print area (matching
    /// <see cref="HitTestPageBreakLinePointer"/>'s bounds check) -- Excel removes a page break dragged off
    /// the print area the same way. Mirrors the WPF shell's
    /// <c>GridView.HitTesting.cs CalculatePageBreakLineDragTarget</c>.
    /// </summary>
    internal static uint? CalculatePageBreakLineDragTarget(
        ViewportModel displayViewport,
        PageBreakAxis axis,
        Point pos,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double width,
        double height)
    {
        if (pos.X < rowHeaderWidth || pos.Y < columnHeaderHeight || pos.X > width || pos.Y > height)
            return null;

        if (axis == PageBreakAxis.Row)
        {
            uint? closestRow = null;
            var closestDistance = double.MaxValue;
            foreach (var metric in displayViewport.RowMetrics)
            {
                var y = metric.TopOffset + columnHeaderHeight;
                var distance = Math.Abs(pos.Y - y);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestRow = metric.Row;
                }
            }
            return closestRow;
        }

        uint? closestCol = null;
        var closestColDistance = double.MaxValue;
        foreach (var metric in displayViewport.ColMetrics)
        {
            var x = metric.LeftOffset + rowHeaderWidth;
            var distance = Math.Abs(pos.X - x);
            if (distance < closestColDistance)
            {
                closestColDistance = distance;
                closestCol = metric.Col;
            }
        }
        return closestCol;
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

    // PageSetupDialog.xaml leaves its fields at the WPF defaults: an 18px single-line TextBox and a
    // 22px ComboBox, so its rows sit 26px (margins) and 30px (combo rows) apart.
    private static AvaloniaCompactDialogChromeStyle PageSetupFieldChromeStyle => new(FormulaBarFontFamily)
    {
        TextBoxHeight = 18,
        TextBoxPadding = new Thickness(3, 0),
        ComboBoxHeight = 22,
    };

    private static void ApplyPageSetupTextBoxChrome(TextBox textBox)
    {
        AvaloniaCompactDialogChrome.ApplyTextBox(textBox, PageSetupFieldChromeStyle);
    }

    private static void ApplyPageSetupComboBoxChrome(ComboBox comboBox)
    {
        AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, PageSetupFieldChromeStyle);
    }

    // A WPF CheckBox in these forms is 15px tall (PageSetupDialog.xaml check boxes sit 23px apart
    // with their 8px bottom margin); the shared 20px row made the stacked options drift.
    private const double PageSetupCheckBoxHeight = 15;
    // PageSetupFieldChromeStyle's text-box height (Margins tab) and the 22px picker / combo rows
    // of the Sheet tab: the tallest control a page-setup label may share its row with.
    private const double PageSetupMarginBoxHeight = 18;
    private const double PageSetupFieldRowHeight = 22;

    private static void ApplyPageSetupCheckBoxRowHeight(CheckBox checkBox)
    {
        checkBox.Height = PageSetupCheckBoxHeight;
        checkBox.MinHeight = PageSetupCheckBoxHeight;
        checkBox.MaxHeight = PageSetupCheckBoxHeight;
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
            // WPF PageSetupDialog.xaml is a 600x560 NoResize window; Width/Height are OUTER sizes there,
            // so the client area drops the caption/frame on both axes.
            Width = PageSetupDialogPlanner.WindowWidth - WpfNoResizeFrameWidth,
            Height = PageSetupDialogPlanner.WindowHeight - WpfNoResizeClientFrameHeight,
            MinWidth = PageSetupDialogPlanner.MinWindowWidth,
            MinHeight = PageSetupDialogPlanner.MinWindowHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, PageSetupDialogPlanner.DialogAutomationId);

        // --- Page tab ---
        // Orientation and page order are radio buttons, as in Excel; index 0 is the first choice.
        RadioButton CreateChoiceRadio(string labelKey, string groupName, bool isChecked, string automationId)
        {
            var radio = new RadioButton
            {
                Content = StripDisplayMnemonic(UiText.Get(labelKey)),
                GroupName = groupName,
                IsChecked = isChecked,
            };
            ApplyPageLayoutRadioButtonChrome(radio);
            AutomationProperties.SetAutomationId(radio, automationId);
            return radio;
        }

        var portraitRadio = CreateChoiceRadio(
            "PageSetup_Portrait", "PageSetupOrientation", surface.ChoiceIndexes.Orientation != 1,
            PageSetupDialogPlanner.PortraitRadioAutomationId);
        var landscapeRadio = CreateChoiceRadio(
            "PageSetup_Landscape", "PageSetupOrientation", surface.ChoiceIndexes.Orientation == 1,
            PageSetupDialogPlanner.LandscapeRadioAutomationId);
        landscapeRadio.Margin = new Thickness(24, 0, 0, 0);
        var orientationPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { portraitRadio, landscapeRadio },
        };
        AutomationProperties.SetAutomationId(orientationPanel, PageSetupDialogPlanner.OrientationBoxAutomationId);

        var paperSizeChoices = PageSetupDialogPlanner.PaperSizeChoices;
        var paperBox = new ComboBox
        {
            ItemsSource = PageSetupDialogPlanner.ResolveChoiceLabels(paperSizeChoices, UiText.Get),
            SelectedIndex = surface.ChoiceIndexes.PaperSize,
            MinWidth = PageSetupDialogPlanner.FieldMinWidth,
        };
        ApplyPageSetupComboBoxChrome(paperBox);
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
        ApplyPageSetupTextBoxChrome(scalePercentBox);
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
        ApplyPageSetupTextBoxChrome(fitWideBox);
        AutomationProperties.SetAutomationId(fitWideBox, "PageSetupFitWideBox");
        var fitTallBox = new TextBox { Text = surface.Scaling.FitToTallText, MinWidth = 70 };
        ApplyPageSetupTextBoxChrome(fitTallBox);
        AutomationProperties.SetAutomationId(fitTallBox, "PageSetupFitTallBox");

        var firstPageNumberBox = new TextBox
        {
            Text = surface.FirstPageNumberText,
            MinWidth = 220,
        };
        ApplyPageSetupTextBoxChrome(firstPageNumberBox);
        AutomationProperties.SetAutomationId(firstPageNumberBox, "PageSetupFirstPageNumberBox");

        var printQualityBox = new TextBox
        {
            Text = surface.PrintQualityDpiText,
            MinWidth = 220,
        };
        ApplyPageSetupTextBoxChrome(printQualityBox);
        AutomationProperties.SetAutomationId(printQualityBox, "PageSetupPrintQualityBox");
        AutomationProperties.SetHelpText(printQualityBox, UiText.Get("PageSetup_PrintQualityHelp"));

        // --- Margins tab ---
        var leftMarginBox = new TextBox { Text = surface.Margins.Left, MinWidth = PageSetupDialogPlanner.FieldMinWidth };
        ApplyPageSetupTextBoxChrome(leftMarginBox);
        AutomationProperties.SetAutomationId(leftMarginBox, PageSetupDialogPlanner.LeftMarginBoxAutomationId);
        var rightMarginBox = new TextBox { Text = surface.Margins.Right, MinWidth = PageSetupDialogPlanner.FieldMinWidth };
        ApplyPageSetupTextBoxChrome(rightMarginBox);
        AutomationProperties.SetAutomationId(rightMarginBox, PageSetupDialogPlanner.RightMarginBoxAutomationId);
        var topMarginBox = new TextBox { Text = surface.Margins.Top, MinWidth = PageSetupDialogPlanner.FieldMinWidth };
        ApplyPageSetupTextBoxChrome(topMarginBox);
        AutomationProperties.SetAutomationId(topMarginBox, PageSetupDialogPlanner.TopMarginBoxAutomationId);
        var bottomMarginBox = new TextBox { Text = surface.Margins.Bottom, MinWidth = PageSetupDialogPlanner.FieldMinWidth };
        ApplyPageSetupTextBoxChrome(bottomMarginBox);
        AutomationProperties.SetAutomationId(bottomMarginBox, PageSetupDialogPlanner.BottomMarginBoxAutomationId);
        var headerMarginBox = new TextBox { Text = surface.HeaderMarginText, MinWidth = 220 };
        ApplyPageSetupTextBoxChrome(headerMarginBox);
        AutomationProperties.SetAutomationId(headerMarginBox, PageSetupDialogPlanner.HeaderMarginBoxAutomationId);
        var footerMarginBox = new TextBox { Text = surface.FooterMarginText, MinWidth = 220 };
        ApplyPageSetupTextBoxChrome(footerMarginBox);
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
        ApplyPageSetupComboBoxChrome(headerPresetBox);
        AutomationProperties.SetAutomationId(headerPresetBox, PageSetupDialogPlanner.HeaderPresetBoxAutomationId);
        var footerPresetBox = new ComboBox
        {
            ItemsSource = footerPresetChoices,
            SelectedIndex = surface.ChoiceIndexes.FooterPreset,
            MinWidth = PageSetupDialogPlanner.HeaderFooterPresetMinWidth,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        footerPresetBox.ItemTemplate = HeaderFooterPresetTemplate();
        ApplyPageSetupComboBoxChrome(footerPresetBox);
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

        customHeaderButton.Click += (_, _) => RunGuarded(() => EditHeaderFooterAsync(openFooterTab: false));
        customFooterButton.Click += (_, _) => RunGuarded(() => EditHeaderFooterAsync(openFooterTab: true));
        UpdateHeaderFooterPreview();

        // --- Sheet tab ---
        var printAreaBox = new TextBox { Text = surface.PrintAreaText, MinWidth = 220 };
        ApplyPageSetupTextBoxChrome(printAreaBox);
        AutomationProperties.SetAutomationId(printAreaBox, "PageSetupPrintAreaBox");
        AutomationProperties.SetHelpText(printAreaBox, UiText.Get("PageSetup_PrintAreaHelp"));

        var repeatRowsBox = new TextBox { Text = surface.RepeatRowsText, MinWidth = 220 };
        ApplyPageSetupTextBoxChrome(repeatRowsBox);
        AutomationProperties.SetAutomationId(repeatRowsBox, "PageSetupRepeatRowsBox");
        AutomationProperties.SetHelpText(repeatRowsBox, UiText.Get("PageSetup_RepeatRowsHelp"));

        var repeatColumnsBox = new TextBox { Text = surface.RepeatColumnsText, MinWidth = 220 };
        ApplyPageSetupTextBoxChrome(repeatColumnsBox);
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

        var downThenOverRadio = CreateChoiceRadio(
            "PageSetup_DownThenOver", "PageSetupPageOrder", surface.ChoiceIndexes.PageOrder != 1,
            PageSetupDialogPlanner.DownThenOverRadioAutomationId);
        var overThenDownRadio = CreateChoiceRadio(
            "PageSetup_OverThenDown", "PageSetupPageOrder", surface.ChoiceIndexes.PageOrder == 1,
            PageSetupDialogPlanner.OverThenDownRadioAutomationId);
        overThenDownRadio.Margin = new Thickness(0, 4, 0, 0);
        var pageOrderPanel = new StackPanel { Children = { downThenOverRadio, overThenDownRadio } };
        AutomationProperties.SetAutomationId(pageOrderPanel, PageSetupDialogPlanner.PageOrderBoxAutomationId);

        var printErrorValueChoices = PageSetupDialogPlanner.PrintErrorValueChoices;
        var cellErrorsBox = new ComboBox
        {
            ItemsSource = PageSetupDialogPlanner.ResolveChoiceLabels(printErrorValueChoices, UiText.Get),
            SelectedIndex = surface.ChoiceIndexes.PrintErrorValue,
            MinWidth = PageSetupDialogPlanner.FieldMinWidth,
        };
        ApplyPageSetupComboBoxChrome(cellErrorsBox);
        AutomationProperties.SetAutomationId(cellErrorsBox, PageSetupDialogPlanner.CellErrorsBoxAutomationId);

        var printCommentChoices = PageSetupDialogPlanner.PrintCommentChoices;
        var commentsBox = new ComboBox
        {
            ItemsSource = PageSetupDialogPlanner.ResolveChoiceLabels(printCommentChoices, UiText.Get),
            SelectedIndex = surface.ChoiceIndexes.PrintComments,
            MinWidth = PageSetupDialogPlanner.FieldMinWidth,
        };
        ApplyPageSetupComboBoxChrome(commentsBox);
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
            OrientationIndex = landscapeRadio.IsChecked == true ? 1 : 0,
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
            PageOrderIndex = overThenDownRadio.IsChecked == true ? 1 : 0,
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

        // Page tab, as PageSetupDialog.xaml lays it out: a two-column form (120px label column, 8px
        // label gap, 8px row gap) inside a 10px page margin, with the "Scaling" group spanning both
        // columns. The group holds an (Auto, 72, Auto, 72, Auto) grid: radio | box | caption | box | caption.
        var pageGrid = new Grid
        {
            Margin = new Thickness(10),
            ColumnDefinitions = new ColumnDefinitions("120,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
        };

        void AddPageField(int row, string label, Control value)
        {
            var labelControl = PageSetupLabel(label);
            labelControl.Margin = new Thickness(0, 0, 8, 8);
            value.Margin = new Thickness(0, 0, 0, 8);
            value.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;
            Grid.SetRow(labelControl, row);
            Grid.SetColumn(labelControl, 0);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            pageGrid.Children.Add(labelControl);
            pageGrid.Children.Add(value);
        }

        var scalingGrid = new Grid
        {
            Margin = new Thickness(8),
            ColumnDefinitions = new ColumnDefinitions("Auto,72,Auto,72,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
        };

        void AddScalingCell(Control control, int row, int column, Thickness margin)
        {
            control.Margin = margin;
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            scalingGrid.Children.Add(control);
        }

        adjustRadio.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        fitRadio.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        scalePercentBox.MinWidth = 0;
        fitWideBox.MinWidth = 0;
        fitTallBox.MinWidth = 0;
        AddScalingCell(adjustRadio, 0, 0, new Thickness(0, 0, 8, 8));
        AddScalingCell(scalePercentBox, 0, 1, new Thickness(0, 0, 6, 8));
        AddScalingCell(PageSetupLabel(UiText.Get("PageSetup_NormalSize")), 0, 2, new Thickness(0, 0, 0, 8));
        AddScalingCell(fitRadio, 1, 0, new Thickness(0, 0, 8, 0));
        AddScalingCell(fitWideBox, 1, 1, new Thickness(0, 0, 6, 0));
        AddScalingCell(PageSetupLabel(UiText.Get("PageSetup_WideBy")), 1, 2, new Thickness(0, 0, 6, 0));
        AddScalingCell(fitTallBox, 1, 3, new Thickness(0, 0, 6, 0));
        AddScalingCell(PageSetupLabel(UiText.Get("PageSetup_Tall")), 1, 4, new Thickness(0));

        // WPF UpdateScalingInputState: only the boxes of the selected scaling mode are enabled.
        void UpdateScalingInputState()
        {
            var fitTo = fitRadio.IsChecked == true;
            scalePercentBox.IsEnabled = !fitTo;
            fitWideBox.IsEnabled = fitTo;
            fitTallBox.IsEnabled = fitTo;
        }

        adjustRadio.IsCheckedChanged += (_, _) => UpdateScalingInputState();
        fitRadio.IsCheckedChanged += (_, _) => UpdateScalingInputState();
        UpdateScalingInputState();

        var scalingGroup = new GroupBox
        {
            Header = UiText.Get("PageSetup_Scaling"),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 2, 0, 8),
            Content = scalingGrid,
        };
        ApplyPlainWindowGroupBoxChrome(scalingGroup, PageLayoutDialogChromeStyle);
        Grid.SetRow(scalingGroup, 2);
        Grid.SetColumnSpan(scalingGroup, 2);

        AddPageField(0, UiText.Get("PageSetup_Orientation"), orientationPanel);
        AddPageField(1, UiText.Get("PageSetup_PaperSize"), paperBox);
        pageGrid.Children.Add(scalingGroup);
        AddPageField(3, UiText.Get("PageSetup_FirstPageNumber"), firstPageNumberBox);
        AddPageField(4, UiText.Get("PageSetup_PrintQuality"), printQualityBox);

        var pageTab = new TabItem
        {
            Header = StripDisplayMnemonic(UiText.Get("PageSetup_PageTab")),
            Content = new ScrollViewer
            {
                Content = pageGrid,
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
            // PageSetupDialog.xaml: WPF resolves each Auto row to the 18px box plus its 8px margin
            // (26px pitch). The label's taller Avalonia line box made alternate rows 27-28px, so
            // it may not exceed the box it labels.
            labelControl.MaxHeight = PageSetupMarginBoxHeight;
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
        ApplyPageSetupCheckBoxRowHeight(centerHorizontallyCheck);
        ApplyPageSetupCheckBoxRowHeight(centerVerticallyCheck);
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
            Spacing = 8,
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

        // PageSetupDialog.xaml: the Header/Footer tab grid has the same 10px margin as the other tabs.
        var headerFooterGrid = new Grid
        {
            Margin = new Thickness(10),
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
            // WPF resolves these Auto rows from the 22px pickers and combo boxes; the label's
            // (taller) Avalonia line box must not stretch a row past them.
            label.MaxHeight = PageSetupFieldRowHeight;
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

            // WPF: the range TextBox stretches to the 22px picker row it shares.
            if (value is TextBox rangeBox)
            {
                rangeBox.Height = PageSetupFieldRowHeight;
                rangeBox.MinHeight = PageSetupFieldRowHeight;
                rangeBox.MaxHeight = PageSetupFieldRowHeight;
            }

            picker.Width = 24;
            picker.MinWidth = 24;
            picker.MaxWidth = 24;
            // PageSetupDialog.xaml range pickers are 24x22.
            picker.Height = 22;
            picker.MinHeight = 22;
            picker.MaxHeight = 22;
            picker.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(picker, row);
            Grid.SetColumn(picker, 2);
            sheetGrid.Children.Add(picker);
        }

        AddSheetLabel(0, UiText.Get("PageSetup_PrintArea"), new Thickness(0, 0, 8, 8));
        AddSheetValue(0, printAreaBox, printAreaPicker);
        AddSheetLabel(1, UiText.Get("PageSetup_RowsToRepeatAtTop"), new Thickness(0, 0, 8, 8));
        AddSheetValue(1, repeatRowsBox, repeatRowsPicker);
        AddSheetLabel(2, UiText.Get("PageSetup_ColumnsToRepeatAtLeft"), new Thickness(0, 0, 8, 8));
        AddSheetValue(2, repeatColumnsBox, repeatColumnsPicker);

        AddSheetValue(3, gridlinesCheck, margin: new Thickness(0, 0, 0, 8));
        AddSheetValue(4, headingsCheck, margin: new Thickness(0, 0, 0, 8));
        AddSheetLabel(5, UiText.Get("PageSetup_PageOrder"), new Thickness(0, 0, 8, 0));
        AddSheetValue(5, pageOrderPanel, margin: new Thickness(0));
        AddSheetValue(6, blackAndWhiteCheck, margin: new Thickness(0, 8, 0, 8));
        AddSheetValue(7, draftQualityCheck, margin: new Thickness(0));
        AddSheetLabel(8, UiText.Get("PageSetup_CellErrorsAs"), new Thickness(0, 8, 8, 0));
        AddSheetValue(8, cellErrorsBox, margin: new Thickness(0, 8, 0, 0));
        AddSheetLabel(9, UiText.Get("PageSetup_Comments"), new Thickness(0, 8, 8, 0));
        AddSheetValue(9, commentsBox, margin: new Thickness(0, 8, 0, 0));
        foreach (var check in new[] { gridlinesCheck, headingsCheck, blackAndWhiteCheck, draftQualityCheck })
            ApplyPageSetupCheckBoxRowHeight(check);

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
                PageSetupDialogFocusTarget.PageOrder => overThenDownRadio.IsChecked == true ? overThenDownRadio : downThenOverRadio,
                PageSetupDialogFocusTarget.PrintErrorValue => cellErrorsBox,
                PageSetupDialogFocusTarget.PrintComments => commentsBox,
                _ => landscapeRadio.IsChecked == true ? landscapeRadio : portraitRadio,
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
            // WPF HeaderFooterDialog.xaml is a resizable 760x600 window (min 700x520). Those are OUTER
            // sizes; the client area the 12px-margin root grid is laid out in drops the frame.
            Width = 760 - WpfResizableFrameWidth,
            Height = 600 - WpfResizableFrameHeight,
            MinWidth = 700 - WpfResizableFrameWidth,
            MinHeight = 520 - WpfResizableFrameHeight,
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
            // HeaderFooterDialog.xaml leaves these TextBoxes at the WPF default height (18px at the
            // 12px dialog font), like the Page Setup fields.
            var box = new TextBox { Text = text };
            ApplyPageSetupTextBoxChrome(box);
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
        // WPF: HeaderFooterTabs has Margin="0,0,0,10" above the token toolbar.
        tabs.Margin = new Thickness(0, 0, 0, 10);
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
                editors[new HeaderFooterEditorTarget(HeaderFooterEditorScope.Header, HeaderFooterEditorSection.Left)].Text = value.Left;
                editors[new HeaderFooterEditorTarget(HeaderFooterEditorScope.Header, HeaderFooterEditorSection.Center)].Text = value.Center;
                editors[new HeaderFooterEditorTarget(HeaderFooterEditorScope.Header, HeaderFooterEditorSection.Right)].Text = value.Right;
            }
        };
        footerPreset.SelectionChanged += (_, _) =>
        {
            if (footerPreset.SelectedItem is HeaderFooterPresetChoice choice)
            {
                var current = ReadEditorScope(HeaderFooterEditorScope.Footer);
                var value = PageSetupDialogPlanner.ApplyFooterPreset(current, choice);
                editors[new HeaderFooterEditorTarget(HeaderFooterEditorScope.Footer, HeaderFooterEditorSection.Left)].Text = value.Left;
                editors[new HeaderFooterEditorTarget(HeaderFooterEditorScope.Footer, HeaderFooterEditorSection.Center)].Text = value.Center;
                editors[new HeaderFooterEditorTarget(HeaderFooterEditorScope.Footer, HeaderFooterEditorSection.Right)].Text = value.Right;
            }
        };

        var tokenButtons = new WrapPanel { Orientation = Orientation.Horizontal };
        // HeaderFooterDialog.xaml gives its buttons no Height: the WPF default at the 12px dialog
        // font is 20px.
        void ApplyTokenButtonChrome(Button button, double minWidth)
        {
            ApplyPageLayoutButtonChrome(button, minWidth);
            button.Height = 20;
            button.MinHeight = 20;
            button.MaxHeight = 20;
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
        pictureButton.Click += (_, _) => RunGuarded(async () =>
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

            // Round 172 (freep-media F1 follow-up): reject an unsupported picture rather than falling
            // back to image/png, matching InsertPictureFromFileAsync and the WPF header/footer dialog.
            // XlsxHeaderFooterPicturePackageWriter names the media part from this stored content type,
            // so the old fallback wrote e.g. metafile bytes into a .png part declared image/png.
            var contentType = InsertPictureCommandFactory.ContentTypeForPath(file.Name);
            if (contentType is null)
            {
                ShowEditIssue(UiText.Get("InsertLoc_UnsupportedImageFormat"));
                return;
            }

            var readResult = await FileByteReadWorkflow.ReadStreamAsync(file.OpenReadAsync);
            if (readResult.Outcome == FileByteReadOutcome.Canceled)
                return;
            if (!readResult.IsReadable)
            {
                ShowEditIssue(UiText.Format("InsertLoc_CouldNotReadImage", readResult.FailureMessage));
                return;
            }
            if (readResult.Outcome == FileByteReadOutcome.Empty)
            {
                ShowEditIssue(UiText.Get("InsertLoc_SelectedImageEmpty"));
                return;
            }

            var (pictureWidth, pictureHeight) = DecodeHeaderFooterPictureSize(readResult.Bytes);
            var picture = new WorksheetHeaderFooterPicture(readResult.Bytes, contentType, file.Name, pictureWidth, pictureHeight);
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
        });
        tokenButtons.Children.Add(pictureButton);

        formatPictureButton = new Button
        {
            Content = UiText.Get("HeaderFooter_FormatPicture"),
            Margin = new Thickness(0, 0, 6, 6),
        };
        ApplyTokenButtonChrome(formatPictureButton, 104);
        AutomationProperties.SetAutomationId(formatPictureButton, "HeaderFooterFormatPictureButton");
        formatPictureButton!.Click += (_, _) => RunGuarded(async () =>
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
        });
        tokenButtons.Children.Add(formatPictureButton);

        pictureTargetStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(128, 128, 128),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        RefreshPictureTargetState();

        var okButton = new Button { Content = UiText.Get("Common_Ok") };
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPageLayoutButtonChrome(okButton, 72, isDefault: true);
        ApplyPageLayoutButtonChrome(cancelButton, 72);
        okButton.Height = 20;
        okButton.MinHeight = 20;
        okButton.MaxHeight = 20;
        cancelButton.Height = 20;
        cancelButton.MinHeight = 20;
        cancelButton.MaxHeight = 20;
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
            Margin = new Thickness(12),
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

    private const double HeaderFooterPictureDefaultWidth = 160;
    private const double HeaderFooterPictureDefaultHeight = 80;

    /// <summary>
    /// Decodes a header/footer picture's native size via Avalonia's <see cref="Bitmap"/> decoder and
    /// converts pixels to this app's 96-DPI device-independent unit convention using the same
    /// <see cref="PictureDecodePixelsToDip"/> helper (defined in MainWindow.InsertObjects.cs) that the
    /// ordinary Insert &gt; Picture path's <c>DecodePictureSize</c> already uses -- so an image inserted
    /// through either the header/footer "Picture" button or the ordinary Insert &gt; Picture command lands
    /// at the same physical size instead of every header/footer picture being force-fit into a fixed box
    /// and having its aspect ratio distorted. Falls back to the historical 160x80 default only when the
    /// bytes cannot be decoded as an image at all, matching how <c>DecodePictureSize</c> degrades on failure.
    /// </summary>
    private static (double Width, double Height) DecodeHeaderFooterPictureSize(byte[] imageBytes)
    {
        try
        {
            using var stream = new MemoryStream(imageBytes);
            using var bitmap = new Bitmap(stream);
            return ResolveHeaderFooterPictureSize(bitmap.PixelSize.Width, bitmap.PixelSize.Height, bitmap.Dpi.X, bitmap.Dpi.Y);
        }
        catch (Exception)
        {
            // Corrupt, truncated, or unsupported bytes: fall through to the historical default box size
            // below, same as the ordinary Insert > Picture path's DecodePictureSize degrades on failure.
            return (HeaderFooterPictureDefaultWidth, HeaderFooterPictureDefaultHeight);
        }
    }

    /// <summary>
    /// Converts decoded pixel dimensions to DIP units via <see cref="PictureDecodePixelsToDip"/> and falls
    /// back to the historical 160x80 default when either dimension decodes as non-positive (extracted from
    /// <see cref="DecodeHeaderFooterPictureSize"/> so the fallback threshold is directly testable without
    /// needing a real -- or headless-stubbed -- <see cref="Bitmap"/> decode).
    /// </summary>
    private static (double Width, double Height) ResolveHeaderFooterPictureSize(
        int pixelWidth, int pixelHeight, double dpiX, double dpiY)
    {
        var width = PictureDecodePixelsToDip(pixelWidth, dpiX);
        var height = PictureDecodePixelsToDip(pixelHeight, dpiY);
        return width > 0 && height > 0
            ? (width, height)
            : (HeaderFooterPictureDefaultWidth, HeaderFooterPictureDefaultHeight);
    }
}
