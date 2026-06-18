using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const double EmusPerPixel = 9525.0;

    private static readonly IBrush SlicerBodyBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
    private static readonly IBrush SlicerBorderBrush = new SolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xBF));
    private static readonly IBrush SlicerHeaderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));
    private static readonly IBrush SlicerTileBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly IBrush SlicerSelectedTileBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xE2, 0xF3));
    private static readonly IBrush SlicerMutedTextBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
    private static readonly IBrush SlicerTrackBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
    private static readonly IBrush SlicerSelectionBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));

    /// <summary>
    /// Adds slicer and timeline overlay visuals for the active sheet's connected
    /// <see cref="SlicerModel"/>s / <see cref="TimelineModel"/>s. The on-sheet drawing anchor is
    /// resolved to a viewport pixel rectangle, the portable <see cref="SlicerLayoutBuilder"/> /
    /// <see cref="TimelineLayoutBuilder"/> produce the geometry, and the visuals are wired so tile
    /// clicks and track clicks commit the matching filter command through the session.
    /// </summary>
    private void AddSlicerTimelineOverlays(Canvas overlay, ViewportModel viewport)
    {
        var showHeadings = _session.ActiveSheet.ShowHeadings;
        var zoomFactor = GetActiveZoomFactor();
        var workbook = _session.Workbook;

        foreach (var slicer in workbook.Slicers)
        {
            if (slicer.DrawingAnchor is not { } anchor ||
                !TryResolveAnchorBounds(viewport, anchor, showHeadings, zoomFactor, out var bounds))
            {
                continue;
            }

            var availableItems = ReadSlicerSourceItems(slicer);
            var layout = SlicerLayoutBuilder.Build(slicer, availableItems, ToModelBounds(bounds, zoomFactor));
            var visual = CreateSlicerVisual(slicer, layout, availableItems, bounds.Width, bounds.Height, zoomFactor);
            Canvas.SetLeft(visual, bounds.Left);
            Canvas.SetTop(visual, bounds.Top);
            overlay.Children.Add(visual);
        }

        foreach (var timeline in workbook.Timelines)
        {
            if (timeline.DrawingAnchor is not { } anchor ||
                !TryResolveAnchorBounds(viewport, anchor, showHeadings, zoomFactor, out var bounds))
            {
                continue;
            }

            var granularity = SlicerTimelineGranularity.Resolve(timeline);
            var layout = TimelineLayoutBuilder.Build(timeline, ToModelBounds(bounds, zoomFactor), granularity);
            var visual = CreateTimelineVisual(timeline, layout, bounds.Width, bounds.Height, zoomFactor);
            Canvas.SetLeft(visual, bounds.Left);
            Canvas.SetTop(visual, bounds.Top);
            overlay.Children.Add(visual);
        }
    }

    private Control CreateSlicerVisual(
        SlicerModel slicer,
        SlicerLayoutModel layout,
        IReadOnlyList<string> availableItems,
        double width,
        double height,
        double zoomFactor)
    {
        var canvas = new Canvas
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Background = SlicerBodyBrush,
            ClipToBounds = true,
        };

        AddFramedBackground(canvas, width, height, layout.HeaderRect.Height * zoomFactor);
        AddHeaderCaption(canvas, layout.Caption, width, layout.HeaderRect.Height * zoomFactor);

        foreach (var tile in layout.Tiles)
        {
            var tileControl = CreateTileControl(tile, zoomFactor);
            canvas.Children.Add(tileControl);
        }

        AutomationProperties.SetAutomationId(canvas, $"Slicer{slicer.Name}");
        AutomationProperties.SetName(canvas, $"Slicer {layout.Caption}");

        canvas.PointerPressed += (_, args) =>
        {
            var point = args.GetCurrentPoint(canvas).Position;
            HandleSlicerPointer(slicer, availableItems, layout, new LayoutPoint(point.X / zoomFactor, point.Y / zoomFactor));
            args.Handled = true;
        };

        return canvas;
    }

    private Border CreateTileControl(SlicerTileLayout tile, double zoomFactor)
    {
        return new Border
        {
            Width = Math.Max(1, tile.Rect.Width * zoomFactor),
            Height = Math.Max(1, tile.Rect.Height * zoomFactor),
            Background = tile.IsSelected ? SlicerSelectedTileBrush : SlicerTileBrush,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(tile.Rect.Left * zoomFactor, tile.Rect.Top * zoomFactor, 0, 0),
            Child = new TextBlock
            {
                Text = tile.Caption,
                FontSize = Math.Max(1, 10 * zoomFactor),
                Foreground = SlicerMutedTextBrush,
                Margin = new Thickness(4 * zoomFactor, 0, 4 * zoomFactor, 0),
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
    }

    private Control CreateTimelineVisual(
        TimelineModel timeline,
        TimelineLayoutModel layout,
        double width,
        double height,
        double zoomFactor)
    {
        var canvas = new Canvas
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Background = SlicerBodyBrush,
            ClipToBounds = true,
        };

        AddFramedBackground(canvas, width, height, layout.HeaderRect.Height * zoomFactor);
        AddHeaderCaption(canvas, layout.Caption, width, layout.HeaderRect.Height * zoomFactor);

        canvas.Children.Add(new TextBlock
        {
            Text = layout.DateLabel,
            FontSize = Math.Max(1, 9 * zoomFactor),
            Foreground = SlicerMutedTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(layout.DateLabelRect.Left * zoomFactor, layout.DateLabelRect.Top * zoomFactor, 0, 0),
        });

        canvas.Children.Add(CreateTrackRect(layout.TrackRect, SlicerTrackBrush, zoomFactor));
        canvas.Children.Add(CreateTrackRect(layout.SelectionRect, SlicerSelectionBrush, zoomFactor));

        AutomationProperties.SetAutomationId(canvas, $"Timeline{timeline.Name}");
        AutomationProperties.SetName(canvas, $"Timeline {layout.Caption}");

        canvas.PointerPressed += (_, args) =>
        {
            var point = args.GetCurrentPoint(canvas).Position;
            HandleTimelinePointer(timeline, layout, new LayoutPoint(point.X / zoomFactor, point.Y / zoomFactor));
            args.Handled = true;
        };

        return canvas;
    }

    private static Border CreateTrackRect(LayoutRect rect, IBrush fill, double zoomFactor) =>
        new()
        {
            Width = Math.Max(1, rect.Width * zoomFactor),
            Height = Math.Max(1, rect.Height * zoomFactor),
            Background = fill,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(rect.Left * zoomFactor, rect.Top * zoomFactor, 0, 0),
            IsHitTestVisible = false,
        };

    private static void AddFramedBackground(Canvas canvas, double width, double height, double headerHeight)
    {
        canvas.Children.Add(new Border
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            BorderBrush = SlicerBorderBrush,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        });
        canvas.Children.Add(new Border
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, headerHeight),
            Background = SlicerHeaderBrush,
            IsHitTestVisible = false,
        });
    }

    private static void AddHeaderCaption(Canvas canvas, string caption, double width, double headerHeight)
    {
        canvas.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 11,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Width = Math.Max(1, width - 10),
            Height = Math.Max(1, headerHeight),
            Margin = new Thickness(5, 0, 0, 0),
        });
    }

    private void HandleSlicerPointer(
        SlicerModel slicer,
        IReadOnlyList<string> availableItems,
        SlicerLayoutModel layout,
        LayoutPoint point)
    {
        var command = SlicerTimelineInteractionPlanner.BuildSlicerToggleCommand(slicer, availableItems, layout, point);
        if (command is null)
            return;

        CommitFilterCommand(command, $"Slicer: {layout.Caption}");
    }

    private void HandleTimelinePointer(TimelineModel timeline, TimelineLayoutModel layout, LayoutPoint point)
    {
        var command = SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(timeline, layout, point);
        if (command is null)
            return;

        CommitFilterCommand(command, $"Timeline: {layout.Caption}");
    }

    private void CommitFilterCommand(FreeX.Core.Commands.IWorkbookCommand command, string status)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Filter update failed.");
            return;
        }

        RefreshShell(status);
    }

    /// <summary>
    /// Resolves a drawing anchor's From/To corners to a viewport pixel rectangle, mirroring the
    /// Windows planner: the column/row indices are 0-based in the anchor and 1-based in the metrics,
    /// and the EMU corner offsets are added on top of the resolved cell edge. Returns false when
    /// either corner is off the laid-out viewport.
    /// </summary>
    private static bool TryResolveAnchorBounds(
        ViewportModel viewport,
        DrawingAnchorRange anchor,
        bool showHeadings,
        double zoomFactor,
        out (double Left, double Top, double Width, double Height) bounds)
    {
        bounds = default;
        if (anchor.From.Column == uint.MaxValue || anchor.To.Column == uint.MaxValue ||
            anchor.From.Row == uint.MaxValue || anchor.To.Row == uint.MaxValue)
        {
            return false;
        }

        if (!TryGetDisplayedColumnLeft(viewport.ColMetrics, anchor.From.Column + 1, zoomFactor, out var fromLeft) ||
            !TryGetDisplayedColumnLeft(viewport.ColMetrics, anchor.To.Column + 1, zoomFactor, out var toLeft) ||
            !TryGetDisplayedRowTop(viewport.RowMetrics, anchor.From.Row + 1, zoomFactor, out var fromTop) ||
            !TryGetDisplayedRowTop(viewport.RowMetrics, anchor.To.Row + 1, zoomFactor, out var toTop))
        {
            return false;
        }

        var headerLeft = showHeadings ? HeaderColumnWidth * zoomFactor : 0;
        var headerTop = showHeadings ? HeaderRowHeight * zoomFactor : 0;
        var left = headerLeft + fromLeft + (EmusToPixels(anchor.From.ColumnOffsetEmu) * zoomFactor);
        var top = headerTop + fromTop + (EmusToPixels(anchor.From.RowOffsetEmu) * zoomFactor);
        var right = headerLeft + toLeft + (EmusToPixels(anchor.To.ColumnOffsetEmu) * zoomFactor);
        var bottom = headerTop + toTop + (EmusToPixels(anchor.To.RowOffsetEmu) * zoomFactor);

        var width = Math.Max(80 * zoomFactor, right - left);
        var height = Math.Max(44 * zoomFactor, bottom - top);
        if (width <= 0 || height <= 0)
            return false;

        bounds = (left, top, width, height);
        return true;
    }

    private static LayoutRect ToModelBounds((double Left, double Top, double Width, double Height) bounds, double zoomFactor) =>
        new(0, 0, bounds.Width / zoomFactor, bounds.Height / zoomFactor);

    private static double EmusToPixels(long emus) => emus / EmusPerPixel;

    /// <summary>
    /// Reads the available source items for a slicer from its connected PivotTable field, mirroring
    /// the Windows host's <c>ReadSlicerSourceItems</c>. Returns an empty list when the slicer is not
    /// connected or the field cannot be resolved.
    /// </summary>
    private IReadOnlyList<string> ReadSlicerSourceItems(SlicerModel slicer)
    {
        // Table slicers (and pivot slicers whose items live in the slicer cache) resolve through the
        // shared SlicerItemResolver — table-column distinct values or pivot cache shared items.
        var resolved = FreeX.Core.Commands.SlicerItemResolver.ResolveAvailableItems(slicer, _session.Workbook);
        if (resolved.Count > 0)
            return resolved;

        if (string.IsNullOrWhiteSpace(slicer.SourcePivotTableName) ||
            string.IsNullOrWhiteSpace(slicer.SourceFieldName))
        {
            return [];
        }

        foreach (var sheet in _session.Workbook.Sheets)
        {
            PivotTableModel? pivotTable = null;
            foreach (var pivot in sheet.PivotTables)
            {
                if (string.Equals(pivot.Name, slicer.SourcePivotTableName, StringComparison.OrdinalIgnoreCase))
                {
                    pivotTable = pivot;
                    break;
                }
            }

            if (pivotTable is null)
                continue;

            return SlicerTimelineSourceReader.ReadFieldItems(sheet, pivotTable, slicer.SourceFieldName);
        }

        return [];
    }
}
