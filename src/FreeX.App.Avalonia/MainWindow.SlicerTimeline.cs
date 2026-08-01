using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const double EmusPerPixel = 9525.0;

    private static readonly IBrush SlicerBodyBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
    private static readonly IBrush SlicerBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xBF));
    private static readonly IBrush SlicerHeaderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));
    private static readonly IBrush SlicerTileBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly IBrush SlicerSelectedTileBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xD9, 0xE2, 0xF3));
    private static readonly IBrush SlicerMutedTextBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
    private static readonly IBrush SlicerTrackBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
    private static readonly IBrush SlicerSelectionBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));

    /// <summary>
    /// Adds slicer and timeline overlay visuals for the active sheet's connected
    /// <see cref="SlicerModel"/>s / <see cref="TimelineModel"/>s. The on-sheet drawing anchor is
    /// resolved to a viewport pixel rectangle, the portable <see cref="SlicerLayoutBuilder"/> /
    /// <see cref="TimelineLayoutBuilder"/> produce the geometry, and the visuals are wired so tile
    /// clicks and track clicks commit the matching filter command through the session.
    /// </summary>
    private void AddSlicerTimelineOverlays(Canvas overlay, ViewportModel viewport)
    {
        var showHeadings = _session.IsShowingHeadings;
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
            // Full multi-column item rendering (every available item, honoring columnCount + showCaption),
            // matching the WPF/headless renderer — not the single-column four-tile preview.
            var layout = SlicerLayoutBuilder.BuildFull(slicer, availableItems, ToModelBounds(bounds, zoomFactor));
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
        // Theme the slicer from its built-in style (SlicerStyleLight1..6) against the workbook theme.
        var style = SlicerStyleColors.Resolve(slicer.StyleName, _session.Workbook.Theme);
        var bodyBrush = ToBrush(style.Body);
        var borderBrush = ToBrush(style.Border);
        var headerBrush = ToBrush(style.Header);
        var headerTextBrush = ToBrush(style.HeaderText);
        var tileBrush = ToBrush(style.Tile);
        var selectedTileBrush = ToBrush(style.SelectedTile);
        var itemTextBrush = ToBrush(style.ItemText);

        var canvas = new Canvas
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Background = bodyBrush,
            ClipToBounds = true,
        };

        var headerHeight = layout.HeaderRect.Height * zoomFactor;
        AddFramedBackground(canvas, width, height, headerHeight, borderBrush, headerBrush);
        // showCaption="0" => no caption band (HeaderRect collapses to zero height in BuildFull).
        if (slicer.ShowCaption)
            AddHeaderCaption(canvas, layout.Caption, width, headerHeight, headerTextBrush);

        foreach (var tile in layout.Tiles)
        {
            var tileControl = CreateTileControl(tile, zoomFactor, tileBrush, selectedTileBrush, itemTextBrush);
            canvas.Children.Add(tileControl);
        }

        // Header chrome: multi-select (☰) and clear-filter (×) icons in the top-right of the header.
        // Rects come from the shared layout builder so WPF and Avalonia use identical geometry.
        if (slicer.ShowCaption && layout.MultiSelectIconRect.Width > 0)
        {
            var multiSelectIcon = new TextBlock
            {
                Text = "☰",
                FontSize = Math.Max(1, 8 * zoomFactor),
                Foreground = headerTextBrush,
                Width = Math.Max(1, layout.MultiSelectIconRect.Width * zoomFactor),
                Height = Math.Max(1, layout.MultiSelectIconRect.Height * zoomFactor),
                Margin = new Thickness(layout.MultiSelectIconRect.Left * zoomFactor, layout.MultiSelectIconRect.Top * zoomFactor, 0, 0),
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
            };
            canvas.Children.Add(multiSelectIcon);
        }

        if (slicer.ShowCaption && layout.ClearFilterIconRect.Width > 0)
        {
            // Clear-filter icon: full opacity when filter is active, semi-transparent otherwise.
            var hasFilter = layout.HasActiveFilter;
            var clearBrush = hasFilter ? headerTextBrush : new SolidColorBrush(Color.FromArgb(128, 255, 255, 255));
            var clearIcon = new TextBlock
            {
                Text = "×",
                FontSize = Math.Max(1, 8 * zoomFactor),
                Foreground = clearBrush,
                Width = Math.Max(1, layout.ClearFilterIconRect.Width * zoomFactor),
                Height = Math.Max(1, layout.ClearFilterIconRect.Height * zoomFactor),
                Margin = new Thickness(layout.ClearFilterIconRect.Left * zoomFactor, layout.ClearFilterIconRect.Top * zoomFactor, 0, 0),
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
            };
            canvas.Children.Add(clearIcon);
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

    private Border CreateTileControl(
        SlicerTileLayout tile,
        double zoomFactor,
        IBrush tileBrush,
        IBrush selectedTileBrush,
        IBrush itemTextBrush)
    {
        return new Border
        {
            Width = Math.Max(1, tile.Rect.Width * zoomFactor),
            Height = Math.Max(1, tile.Rect.Height * zoomFactor),
            Background = tile.IsSelected ? selectedTileBrush : tileBrush,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(tile.Rect.Left * zoomFactor, tile.Rect.Top * zoomFactor, 0, 0),
            Child = new TextBlock
            {
                Text = tile.Caption,
                FontSize = Math.Max(1, 10 * zoomFactor),
                Foreground = itemTextBrush,
                Margin = new Thickness(4 * zoomFactor, 0, 4 * zoomFactor, 0),
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
    }

    private static IBrush ToBrush(CellColor color) =>
        new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));

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

        AddFramedBackground(canvas, width, height, layout.HeaderRect.Height * zoomFactor, SlicerBorderBrush, SlicerHeaderBrush);
        AddHeaderCaption(canvas, layout.Caption, width, layout.HeaderRect.Height * zoomFactor, Brushes.White);

        canvas.Children.Add(new TextBlock
        {
            Text = layout.DateLabel,
            FontSize = Math.Max(1, 9 * zoomFactor),
            Foreground = SlicerMutedTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(layout.DateLabelRect.Left * zoomFactor, layout.DateLabelRect.Top * zoomFactor, 0, 0),
        });

        // Granularity dropdown label ("MONTHS ▾" etc.) — the pointer route below cycles the
        // shared timeline granularity command, matching the WPF/native timeline behavior.
        if (layout.GranularityDropdownRect.Width > 0)
        {
            var granLabel = layout.Granularity switch
            {
                TimelineGranularity.Year => "YEARS ▾",
                TimelineGranularity.Quarter => "QUARTERS ▾",
                TimelineGranularity.Month => "MONTHS ▾",
                _ => "DAYS ▾"
            };
            canvas.Children.Add(new TextBlock
            {
                Text = granLabel,
                FontSize = Math.Max(1, 7.5 * zoomFactor),
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Width = Math.Max(1, layout.GranularityDropdownRect.Width * zoomFactor),
                Height = Math.Max(1, layout.GranularityDropdownRect.Height * zoomFactor),
                Margin = new Thickness(layout.GranularityDropdownRect.Left * zoomFactor, layout.GranularityDropdownRect.Top * zoomFactor, 0, 0),
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
                IsHitTestVisible = false,
            });
        }

        // Clear-filter (×) icon — shown when a date range filter is active.
        if (layout.HasActiveFilter && layout.ClearFilterIconRect.Width > 0)
        {
            canvas.Children.Add(new TextBlock
            {
                Text = "×",
                FontSize = Math.Max(1, 9 * zoomFactor),
                Foreground = Brushes.White,
                Width = Math.Max(1, layout.ClearFilterIconRect.Width * zoomFactor),
                Height = Math.Max(1, layout.ClearFilterIconRect.Height * zoomFactor),
                Margin = new Thickness(layout.ClearFilterIconRect.Left * zoomFactor, layout.ClearFilterIconRect.Top * zoomFactor, 0, 0),
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
            });
        }

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

    private static void AddFramedBackground(Canvas canvas, double width, double height, double headerHeight, IBrush borderBrush, IBrush headerBrush)
    {
        canvas.Children.Add(new Border
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        });
        if (headerHeight <= 0)
            return;

        canvas.Children.Add(new Border
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, headerHeight),
            Background = headerBrush,
            IsHitTestVisible = false,
        });
    }

    private static void AddHeaderCaption(Canvas canvas, string caption, double width, double headerHeight, IBrush textBrush)
    {
        canvas.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 11,
            Foreground = textBrush,
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
        // Priority order: clear-filter icon > tile toggle.
        // The clear icon sits in the header and does not overlap any tile, but we test it first so
        // a click near the corner that just barely overlaps both is unambiguously handled as a clear.
        if (SlicerTimelineInteractionPlanner.BuildSlicerClearFilterCommand(slicer, layout, point) is { } clearCmd)
        {
            CommitFilterCommand(clearCmd, $"Slicer: {layout.Caption}");
            return;
        }

        var command = SlicerTimelineInteractionPlanner.BuildSlicerToggleCommand(slicer, availableItems, layout, point);
        if (command is null)
            return;

        CommitFilterCommand(command, $"Slicer: {layout.Caption}");
    }

    private void HandleTimelinePointer(TimelineModel timeline, TimelineLayoutModel layout, LayoutPoint point)
    {
        // Priority order: clear-filter icon > granularity dropdown > track/handle.
        if (SlicerTimelineInteractionPlanner.BuildTimelineClearFilterCommand(timeline, layout, point) is { } clearCmd)
        {
            CommitFilterCommand(clearCmd, $"Timeline: {layout.Caption}");
            return;
        }

        if (SlicerTimelineInteractionPlanner.BuildTimelineGranularityCommand(timeline, layout, point) is { } granCmd)
        {
            CommitFilterCommand(granCmd, $"Timeline: {layout.Caption}");
            return;
        }

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
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("ShellLoc_FilterUpdateFailed"));
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

        var headerLeft = showHeadings ? GetRowHeaderWidth(viewport, zoomFactor) : 0;
        var headerTop = showHeadings ? GetColumnHeaderHeight(viewport, zoomFactor) : 0;
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
