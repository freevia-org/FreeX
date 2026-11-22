using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Free.Shared.Drawing;
using Free.Shared.Localization;

using AvaloniaCanvas = Avalonia.Controls.Canvas;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;

namespace FreeX.App.Avalonia;

/// <summary>
/// In-chart element selection: clicking a chart's title, legend, axes, axis titles, gridlines, plot area,
/// data labels or a data series selects just that element (highlighted in place), Delete removes it,
/// and double-click / Format Selection opens the matching existing format dialog. The geometry and
/// the Delete / Format policy come from the shared <see cref="ChartElementMapPlanner"/> and
/// <see cref="ChartElementCommandPlanner"/>, so WPF and Avalonia behave identically.
/// </summary>
public sealed partial class MainWindow
{
    private const double ChartElementHighlightInflate = 2;
    private const double ChartElementHandleSize = 6;

    // Valid only while that chart is the selected drawing object (see GetSelectedChartElement).
    private ChartElementRef? _selectedChartElement;
    private Guid _selectedChartElementChartId;
    private string? _selectedChartElementSeriesName;

    private ChartElementRef? GetSelectedChartElement(ChartModel chart) =>
        IsSelectedChart(chart) && _selectedChartElementChartId == chart.Id ? _selectedChartElement : null;

    private void SelectChartElement(ChartModel chart, ChartElementRef element, ChartElementMap map)
    {
        if (element.IsChartArea)
        {
            _selectedChartElementSeriesName = null;
            SelectChart(chart);
            return;
        }

        var seriesName = map.GetDisplayName(element);
        _selectedChartElementSeriesName = seriesName;

        var name = ChartElementCommandPlanner.Describe(element, seriesName).Resolve(UiText.Get, UiText.Format);
        SelectChartWithElement(chart, element, UiText.Format(ChartElementCommandPlanner.SelectedStatusResourceKey, name));
    }

    /// <summary>
    /// Outline (plus corner grips for single-rectangle elements) drawn over the selected element,
    /// replacing the whole-chart adorner while an inner element is selected.
    /// </summary>
    private Control CreateChartElementHighlight(ChartElementMap map, ChartElementRef element)
    {
        var layer = new AvaloniaCanvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };

        var bounds = map.GetHighlightBounds(element);
        foreach (var rect in bounds)
        {
            var outline = new AvaloniaRectangle
            {
                Width = Math.Max(1, rect.Width + (2 * ChartElementHighlightInflate)),
                Height = Math.Max(1, rect.Height + (2 * ChartElementHighlightInflate)),
                Stroke = SelectionBorder,
                StrokeThickness = 1.5,
                StrokeDashArray = [4, 2],
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            AvaloniaCanvas.SetLeft(outline, rect.Left - ChartElementHighlightInflate);
            AvaloniaCanvas.SetTop(outline, rect.Top - ChartElementHighlightInflate);
            layer.Children.Add(outline);
        }

        if (bounds.Count == 1)
        {
            var r = bounds[0];
            foreach (var corner in new[]
                     {
                         new LayoutPoint(r.Left, r.Top), new LayoutPoint(r.Right, r.Top),
                         new LayoutPoint(r.Left, r.Bottom), new LayoutPoint(r.Right, r.Bottom),
                     })
            {
                var grip = new AvaloniaRectangle
                {
                    Width = ChartElementHandleSize,
                    Height = ChartElementHandleSize,
                    Fill = Brushes.White,
                    Stroke = SelectionBorder,
                    StrokeThickness = 1,
                    IsHitTestVisible = false,
                };
                AvaloniaCanvas.SetLeft(grip, corner.X - (ChartElementHandleSize / 2));
                AvaloniaCanvas.SetTop(grip, corner.Y - (ChartElementHandleSize / 2));
                layer.Children.Add(grip);
            }
        }

        return layer;
    }

    // The element map of the chart as last painted, kept so keyboard navigation uses the same geometry.
    private ChartElementMap? _lastChartElementMap;
    private Guid _lastChartElementMapChartId;

    /// <summary>
    /// Arrow-key navigation between chart elements (Excel behaviour). Returns false when no inner
    /// element is selected so the arrow keys keep nudging the chart object.
    /// </summary>
    private bool TryNavigateSelectedChartElement(Key key)
    {
        if (_selectedDrawingObjectKind != SelectionPaneObjectKind.Chart ||
            _selectedDrawingObjectId is not { } chartId ||
            _selectedChartElement is not { IsChartArea: false } current ||
            _selectedChartElementChartId != chartId ||
            _lastChartElementMap is not { } map ||
            _lastChartElementMapChartId != chartId)
        {
            return false;
        }

        var chart = _session.ActiveSheet.Charts.FirstOrDefault(c => c.Id == chartId);
        if (chart is null)
            return false;

        var next = map.Navigate(current, key switch
        {
            Key.Up => ChartNavigationKey.Up,
            Key.Down => ChartNavigationKey.Down,
            Key.Left => ChartNavigationKey.Left,
            _ => ChartNavigationKey.Right,
        });
        if (next != current)
            SelectChartElement(chart, next, map);
        return true;
    }

    /// <summary>
    /// Delete on a selected inner chart element. Returns false when no inner element is selected so the
    /// caller falls through to deleting the chart object itself.
    /// </summary>
    private bool TryDeleteSelectedChartElement()
    {
        if (_selectedDrawingObjectKind != SelectionPaneObjectKind.Chart ||
            _selectedDrawingObjectId is not { } chartId ||
            _selectedChartElement is not { IsChartArea: false } element ||
            _selectedChartElementChartId != chartId)
        {
            return false;
        }

        var sheet = _session.ActiveSheet;
        var chart = sheet.Charts.FirstOrDefault(c => c.Id == chartId);
        if (chart is null)
            return false;

        var name = ChartElementCommandPlanner.Describe(element, _selectedChartElementSeriesName).Resolve(UiText.Get, UiText.Format);
        var plan = ChartElementCommandPlanner.PlanDelete(chart, element);
        var command = ChartElementCommandPlanner.BuildDeleteCommand(sheet.Id, chart, plan);

        if (command is null)
        {
            RefreshShell(UiText.Format(ChartElementCommandPlanner.CannotDeleteStatusResourceKey, name));
            return true;
        }

        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Format(ChartElementCommandPlanner.CannotDeleteStatusResourceKey, name));
            return true;
        }

        // Like Excel, deleting an element leaves the (whole) chart selected.
        _selectedChartElement = null;
        _selectedChartElementSeriesName = null;
        RefreshShell(UiText.Format(ChartElementCommandPlanner.DeletedStatusResourceKey, name));
        return true;
    }

    /// <summary>"Format Selection": opens the existing dialog that owns the selected chart element.</summary>
    private async Task OpenFormatSelectedChartElementAsync(ChartModel chart, ChartElementRef element)
    {
        switch (ChartElementCommandPlanner.GetFormatTarget(chart, element))
        {
            case ChartElementFormatTarget.ChartTitles:
                await ShowChartTitlesDialog();
                break;
            case ChartElementFormatTarget.XAxis:
                await ShowChartXAxisFormatDialog();
                break;
            case ChartElementFormatTarget.YAxis:
                await ShowChartYAxisFormatDialog();
                break;
            case ChartElementFormatTarget.DataSeries:
                await ShowChartSeriesFormatDialog();
                break;
            case ChartElementFormatTarget.DataLabels:
                await ShowChartDataLabelsDialog();
                break;
            case ChartElementFormatTarget.Trendline:
                await ShowChartTrendlineDialog();
                break;
            case ChartElementFormatTarget.ErrorBars:
                await ShowChartErrorBarsDialog();
                break;
            default:
                await ShowFormatChartAreaDialog();
                break;
        }
    }
}
