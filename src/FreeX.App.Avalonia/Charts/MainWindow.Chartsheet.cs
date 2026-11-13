using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;

namespace FreeX.App.Avalonia;

/// <summary>
/// Chartsheet presentation, mirroring the WPF host's <c>MainWindow.Chartsheet.cs</c>: when the active
/// sheet is a chartsheet the cell grid (and its headers) is replaced by a single chart that fills the
/// whole sheet area. The chart is laid out by the portable <see cref="ChartLayoutEngine"/> and painted
/// by the existing <see cref="AvaloniaChartRenderer"/> (the same path embedded charts use); only the
/// bounds differ (the viewport, not <see cref="ChartModel.Width"/>/<see cref="ChartModel.Height"/>).
/// </summary>
public sealed partial class MainWindow
{
    private const string ChartsheetViewAutomationId = "ChartsheetView";
    private const double ChartsheetFallbackWidth = 800;
    private const double ChartsheetFallbackHeight = 500;

    private static bool IsChartsheet(Sheet? sheet) => sheet is { IsChartsheet: true };

    /// <summary>True while the worksheet viewport is showing the active chartsheet's chart.</summary>
    internal bool IsChartsheetViewShowing => _sheetGridHost.Content is ChartsheetViewHost;

    /// <summary>
    /// Builds the full-window chart view that replaces <see cref="BuildSheetGrid"/> output for a
    /// chartsheet. The chart is implicitly selected (Excel shows Chart Design/Format for a chartsheet).
    /// </summary>
    private Control BuildChartsheetView()
    {
        // The grid-only overlays/affordances do not exist on a chartsheet.
        _formulaReferenceGridOverlay = null;
        _cellAffordanceOverlay = null;
        _formulaReferenceGridHighlightVisuals.Clear();
        _activeDataValidationDropdown = null;
        _activeCellBorder = null;
        _activeSelectionOutlineVisual = null;
        _autofillHandleVisual = null;

        var sheet = _session.ActiveSheet;
        var chart = sheet.ChartsheetChart;
        if (chart is not null && !IsSelectedChart(chart))
        {
            _selectedDrawingObjectKind = SelectionPaneObjectKind.Chart;
            _selectedDrawingObjectId = chart.Id;
            _ribbonContextSource.OnDrawingObjectSelected(SelectionPaneObjectKind.Chart);
        }

        var bounds = _sheetScrollViewer.Bounds;
        var host = new ChartsheetViewHost(
            this,
            chart,
            bounds.Width >= 1 ? bounds.Width : ChartsheetFallbackWidth,
            bounds.Height >= 1 ? bounds.Height : ChartsheetFallbackHeight);
        AutomationProperties.SetAutomationId(host, ChartsheetViewAutomationId);
        AutomationProperties.SetName(host, sheet.Name);
        return host;
    }

    /// <summary>Paints <paramref name="chart"/> to fill <paramref name="width"/> x <paramref name="height"/>.</summary>
    private Control? RenderChartsheetChart(ChartModel chart, double width, double height)
    {
        if (!ChartLayoutEngine.IsSupported(chart.Type))
            return null;

        // The chart's data usually lives on a worksheet; the session viewport carries the active
        // sheet's chart data cells (ChartDataCells span other sheets), keyed by data sheet id.
        var dataSheetId = chart.DataRange.Start.Sheet;
        var accessor = ChartViewportCellAccessorBuilder.BuildValueAccessor(_session.Viewport, dataSheetId);

        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var inset = Math.Min(28, Math.Min(width, height) / 4);
        var plotArea = new PlotRect(inset, inset, Math.Max(1, width - (2 * inset)), Math.Max(1, height - (2 * inset)));
        var request = ChartLayoutRequestBuilder.TryBuild(chart, plotArea, accessor, ChartTextMeasurer);
        if (request is null)
            return null;

        try
        {
            var layout = ChartLayoutEngine.Layout(request);
            var visual = new AvaloniaChartRenderer(chart, _session.Workbook.Theme).Render(layout, width, height);
            var container = new AvaloniaGrid
            {
                Width = width,
                Height = height,
                Background = Brushes.Transparent,
            };
            container.Children.Add(visual);
            if (BuildPivotChartFieldButtonOverlay(chart) is { } fieldButtons)
                container.Children.Add(fieldButtons);
            return container;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private void SelectChartsheetChart(ChartModel chart, PointerPressedEventArgs args, Control anchor)
    {
        var point = args.GetCurrentPoint(anchor);
        if (point.Properties.IsRightButtonPressed)
        {
            HandleChartPointerContext(chart, anchor, args);
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            SelectChart(chart);
            args.Handled = true;
        }
    }

    /// <summary>
    /// Host for the chartsheet chart. Re-renders at the available size whenever the viewport resizes
    /// (the equivalent of WPF's <c>ChartsheetView_SizeChanged</c>).
    /// </summary>
    private sealed class ChartsheetViewHost : AvaloniaGrid
    {
        private readonly MainWindow _owner;
        private readonly ChartModel? _chart;
        private Size _renderedSize;

        public ChartsheetViewHost(MainWindow owner, ChartModel? chart, double width, double height)
        {
            _owner = owner;
            _chart = chart;
            Background = Brushes.White;
            Focusable = true;
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch;
            Width = width;
            Height = height;
            Render(width, height);

            if (chart is not null)
            {
                PointerPressed += (_, args) => _owner.SelectChartsheetChart(chart, args, this);
                owner._sheetScrollViewer.SizeChanged += OnViewportSizeChanged;
                DetachedFromVisualTree += (_, _) => owner._sheetScrollViewer.SizeChanged -= OnViewportSizeChanged;
            }
        }

        private void OnViewportSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width < 1 || e.NewSize.Height < 1)
                return;
            Width = e.NewSize.Width;
            Height = e.NewSize.Height;
            Render(e.NewSize.Width, e.NewSize.Height);
        }

        private void Render(double width, double height)
        {
            if (_chart is null || _renderedSize == new Size(width, height))
                return;

            _renderedSize = new Size(width, height);
            Children.Clear();
            if (_owner.RenderChartsheetChart(_chart, width, height) is { } visual)
                Children.Add(visual);
        }
    }
}
