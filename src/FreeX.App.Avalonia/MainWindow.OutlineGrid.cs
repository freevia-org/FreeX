using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const double OutlineLevelPitch = 14;
    private const double OutlineGutterPadding = 6;
    private const double OutlineButtonSize = 13;

    private static readonly IBrush OutlineGlyphBrush = Brush(84, 130, 53);
    private static readonly IBrush OutlineButtonBorderBrush = Brush(117, 117, 117);

    private static double GetRowOutlineGutterWidth(ViewportModel viewport, double zoomFactor) =>
        GetOutlineGutterSize(viewport.RowOutlineGroups, zoomFactor);

    private static double GetColumnOutlineGutterHeight(ViewportModel viewport, double zoomFactor) =>
        GetOutlineGutterSize(viewport.ColumnOutlineGroups, zoomFactor);

    private static double GetColumnHeaderHeight(ViewportModel viewport, double zoomFactor) =>
        HeaderRowHeight * zoomFactor + GetColumnOutlineGutterHeight(viewport, zoomFactor);

    private static double GetOutlineGutterSize(IReadOnlyList<OutlineGroupRange>? groups, double zoomFactor)
    {
        if (groups is not { Count: > 0 })
            return 0;

        var maxLevel = groups.Max(group => group.Level);
        return maxLevel <= 0
            ? 0
            : (OutlineGutterPadding * 2 + maxLevel * OutlineLevelPitch) * zoomFactor;
    }

    private Canvas? BuildOutlineOverlay(ViewportModel viewport, bool showHeadings, double zoomFactor)
    {
        if (!showHeadings ||
            viewport.RowOutlineGroups is not { Count: > 0 } &&
            viewport.ColumnOutlineGroups is not { Count: > 0 })
        {
            return null;
        }

        var rowHeaderWidth = GetRowHeaderWidth(viewport, zoomFactor);
        var columnHeaderHeight = GetColumnHeaderHeight(viewport, zoomFactor);
        var rowOutlineWidth = GetRowOutlineGutterWidth(viewport, zoomFactor);
        var columnOutlineHeight = GetColumnOutlineGutterHeight(viewport, zoomFactor);
        var overlay = new Canvas
        {
            Width = CalculateDisplayedGridWidth(viewport, showHeadings, zoomFactor),
            Height = CalculateDisplayedGridHeight(viewport, showHeadings, zoomFactor),
            ClipToBounds = true,
            ZIndex = 20,
        };
        AutomationProperties.SetAutomationId(overlay, "WorksheetOutlineOverlay");

        AddRowOutlineLevelButtons(
            overlay, viewport.RowOutlineGroups, rowOutlineWidth, columnHeaderHeight, columnOutlineHeight, zoomFactor);
        AddColumnOutlineLevelButtons(
            overlay, viewport.ColumnOutlineGroups, rowHeaderWidth, rowOutlineWidth, columnOutlineHeight, zoomFactor);
        AddRowOutlineGroups(overlay, viewport, rowOutlineWidth, columnHeaderHeight, zoomFactor);
        AddColumnOutlineGroups(overlay, viewport, rowHeaderWidth, columnOutlineHeight, zoomFactor);
        return overlay;
    }

    private void AddRowOutlineLevelButtons(
        Canvas overlay,
        IReadOnlyList<OutlineGroupRange>? groups,
        double rowOutlineWidth,
        double columnHeaderHeight,
        double columnOutlineHeight,
        double zoomFactor)
    {
        var maxLevel = groups is { Count: > 0 } ? groups.Max(group => group.Level) : 0;
        if (maxLevel <= 0)
            return;

        var buttonSize = OutlineButtonSize * zoomFactor;
        var top = columnOutlineHeight > 0
            ? Math.Max(zoomFactor, columnOutlineHeight - buttonSize - 2 * zoomFactor)
            : Math.Max(zoomFactor, (columnHeaderHeight - buttonSize) / 2);
        for (var level = 1; level <= maxLevel; level++)
        {
            var centerX = GetOutlineLevelCenter(level, zoomFactor);
            var capturedLevel = level;
            AddOutlineLevelButton(
                overlay,
                centerX,
                top + buttonSize / 2,
                level,
                zoomFactor,
                $"WorksheetRowOutlineLevel-{level}",
                () => ShowRowOutlineLevel(capturedLevel));
        }
    }

    private void AddColumnOutlineLevelButtons(
        Canvas overlay,
        IReadOnlyList<OutlineGroupRange>? groups,
        double rowHeaderWidth,
        double rowOutlineWidth,
        double columnOutlineHeight,
        double zoomFactor)
    {
        var maxLevel = groups is { Count: > 0 } ? groups.Max(group => group.Level) : 0;
        if (maxLevel <= 0)
            return;

        var buttonSize = OutlineButtonSize * zoomFactor;
        var left = rowOutlineWidth > 0
            ? Math.Max(zoomFactor, rowOutlineWidth - buttonSize - 2 * zoomFactor)
            : Math.Max(zoomFactor, (rowHeaderWidth - buttonSize) / 2);
        for (var level = 1; level <= maxLevel; level++)
        {
            var centerY = GetOutlineLevelCenter(level, zoomFactor);
            var capturedLevel = level;
            AddOutlineLevelButton(
                overlay,
                left + buttonSize / 2,
                centerY,
                level,
                zoomFactor,
                $"WorksheetColumnOutlineLevel-{level}",
                () => ShowColumnOutlineLevel(capturedLevel));
        }
    }

    private static void AddOutlineLevelButton(
        Canvas overlay,
        double centerX,
        double centerY,
        int level,
        double zoomFactor,
        string automationId,
        Action onClick)
    {
        var size = OutlineButtonSize * zoomFactor;
        var levelButton = new Button
        {
            Width = size,
            Height = size,
            MinWidth = size,
            MinHeight = size,
            Padding = new Thickness(0),
            Background = Brushes.White,
            BorderBrush = OutlineButtonBorderBrush,
            BorderThickness = new Thickness(Math.Max(1, zoomFactor)),
            HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            Content = new TextBlock
            {
                Text = level.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 9 * zoomFactor,
                Foreground = Brushes.Black,
                TextAlignment = TextAlignment.Center,
            },
            Focusable = false,
            CornerRadius = new CornerRadius(0),
        };
        AutomationProperties.SetAutomationId(levelButton, automationId);
        AutomationProperties.SetName(levelButton, UiText.Format("Outline_ShowLevelButtonName", level));
        levelButton.Click += (_, args) =>
        {
            onClick();
            args.Handled = true;
        };
        Canvas.SetLeft(levelButton, centerX - size / 2);
        Canvas.SetTop(levelButton, centerY - size / 2);
        overlay.Children.Add(levelButton);
    }

    private void AddRowOutlineGroups(
        Canvas overlay,
        ViewportModel viewport,
        double rowOutlineWidth,
        double columnHeaderHeight,
        double zoomFactor)
    {
        if (viewport.RowOutlineGroups is not { Count: > 0 })
            return;

        var rows = CombineSplitRowMetrics(viewport);
        foreach (var group in viewport.RowOutlineGroups)
        {
            var centerX = GetOutlineLevelCenter(group.Level, zoomFactor);
            if (TryGetDisplayedRowOutlineSpan(rows, group, columnHeaderHeight, zoomFactor, out var top, out var bottom))
                AddRowOutlineBracket(overlay, centerX, top, bottom, zoomFactor);

            if (!TryGetDisplayedRowBounds(rows, group.ToggleIndex, columnHeaderHeight, zoomFactor, out var rowTop, out var rowHeight))
                continue;

            var capturedGroup = group;
            AddOutlineToggleButton(
                overlay,
                centerX,
                rowTop + rowHeight / 2,
                group.IsCollapsed,
                zoomFactor,
                $"WorksheetRowOutlineToggle-L{group.Level}-{group.Start}-{group.End}",
                () => ToggleRowOutlineGroup(capturedGroup));
        }
    }

    private void AddColumnOutlineGroups(
        Canvas overlay,
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnOutlineHeight,
        double zoomFactor)
    {
        if (viewport.ColumnOutlineGroups is not { Count: > 0 })
            return;

        var columns = CombineSplitColumnMetrics(viewport);
        foreach (var group in viewport.ColumnOutlineGroups)
        {
            var centerY = GetOutlineLevelCenter(group.Level, zoomFactor);
            if (TryGetDisplayedColumnOutlineSpan(columns, group, rowHeaderWidth, zoomFactor, out var left, out var right))
                AddColumnOutlineBracket(overlay, centerY, left, right, zoomFactor);

            if (!TryGetDisplayedColumnBounds(columns, group.ToggleIndex, rowHeaderWidth, zoomFactor, out var colLeft, out var colWidth))
                continue;

            var capturedGroup = group;
            AddOutlineToggleButton(
                overlay,
                colLeft + colWidth / 2,
                centerY,
                group.IsCollapsed,
                zoomFactor,
                $"WorksheetColumnOutlineToggle-L{group.Level}-{group.Start}-{group.End}",
                () => ToggleColumnOutlineGroup(capturedGroup));
        }
    }

    private static void AddOutlineToggleButton(
        Canvas overlay,
        double centerX,
        double centerY,
        bool isCollapsed,
        double zoomFactor,
        string automationId,
        Action onClick)
    {
        var size = OutlineButtonSize * zoomFactor;
        var glyph = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
        var inset = 3 * zoomFactor;
        var middle = size / 2;
        glyph.Children.Add(CreateOutlineLine(inset, middle, size - inset, middle, zoomFactor));
        if (isCollapsed)
            glyph.Children.Add(CreateOutlineLine(middle, inset, middle, size - inset, zoomFactor));

        var button = new Button
        {
            Width = size,
            Height = size,
            MinWidth = size,
            MinHeight = size,
            Padding = new Thickness(0),
            Background = Brushes.White,
            BorderBrush = OutlineButtonBorderBrush,
            BorderThickness = new Thickness(Math.Max(1, zoomFactor)),
            Content = glyph,
            Focusable = false,
            CornerRadius = new CornerRadius(0),
        };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, isCollapsed ? "Expand outline group" : "Collapse outline group");
        button.Click += (_, args) =>
        {
            onClick();
            args.Handled = true;
        };
        Canvas.SetLeft(button, centerX - size / 2);
        Canvas.SetTop(button, centerY - size / 2);
        overlay.Children.Add(button);
    }

    private void ToggleRowOutlineGroup(OutlineGroupRange group)
    {
        var result = _session.SetOutlineGroupCollapsed(
            OutlineGroupingAxis.Rows,
            group.Start,
            group.End,
            group.Level,
            !group.IsCollapsed);
        RefreshShell(result.Success
            ? UiText.Get(!group.IsCollapsed ? "Outline_RowGroupCollapsedStatus" : "Outline_RowGroupExpandedStatus")
            : result.ErrorMessage ?? UiText.Get("Outline_UpdateRowGroupFailed"));
    }

    private void ToggleColumnOutlineGroup(OutlineGroupRange group)
    {
        var result = _session.SetOutlineGroupCollapsed(
            OutlineGroupingAxis.Columns,
            group.Start,
            group.End,
            group.Level,
            !group.IsCollapsed);
        RefreshShell(result.Success
            ? UiText.Get(!group.IsCollapsed ? "Outline_ColumnGroupCollapsedStatus" : "Outline_ColumnGroupExpandedStatus")
            : result.ErrorMessage ?? UiText.Get("Outline_UpdateColumnGroupFailed"));
    }

    /// <summary>
    /// Handles a click on a row-gutter outline level button (the numbered "1 2 3..." boxes above
    /// the outline brackets). Matches Excel: shows every summary row through the clicked depth and
    /// collapses (hides) every row nested deeper than it, sheet-wide. Implemented as expand-all
    /// (level 1, the sheet-wide threshold overload's shallowest bound) followed by a re-collapse at
    /// level+1 so any row previously hidden by a shallower display level becomes visible again
    /// before the deeper levels are re-hidden.
    /// </summary>
    private void ShowRowOutlineLevel(int level)
    {
        var result = _session.ExecuteRepeatableCommandPreservingSelection(() =>
            new CompositeWorkbookCommand(
                "Show Outline Level",
                [
                    new ExpandRowGroupCommand(_session.ActiveSheet.Id, 1),
                    new CollapseRowGroupCommand(_session.ActiveSheet.Id, level + 1),
                ]));
        RefreshShell(result.Success
            ? UiText.Format("Outline_RowLevelShownStatus", level)
            : result.ErrorMessage ?? UiText.Get("Outline_UpdateRowGroupFailed"));
    }

    /// <summary>Column-axis counterpart of <see cref="ShowRowOutlineLevel"/>.</summary>
    private void ShowColumnOutlineLevel(int level)
    {
        var result = _session.ExecuteRepeatableCommandPreservingSelection(() =>
            new CompositeWorkbookCommand(
                "Show Outline Level",
                [
                    new ExpandColGroupCommand(_session.ActiveSheet.Id, 1),
                    new CollapseColGroupCommand(_session.ActiveSheet.Id, level + 1),
                ]));
        RefreshShell(result.Success
            ? UiText.Format("Outline_ColumnLevelShownStatus", level)
            : result.ErrorMessage ?? UiText.Get("Outline_UpdateColumnGroupFailed"));
    }

    private static void AddRowOutlineBracket(Canvas overlay, double x, double top, double bottom, double zoomFactor)
    {
        if (bottom <= top)
            return;

        var tick = 5 * zoomFactor;
        var inset = 3 * zoomFactor;
        overlay.Children.Add(CreateOutlineLine(x, top + inset, x, bottom - inset, zoomFactor));
        overlay.Children.Add(CreateOutlineLine(x, top + inset, x + tick, top + inset, zoomFactor));
        overlay.Children.Add(CreateOutlineLine(x, bottom - inset, x + tick, bottom - inset, zoomFactor));
    }

    private static void AddColumnOutlineBracket(Canvas overlay, double y, double left, double right, double zoomFactor)
    {
        if (right <= left)
            return;

        var tick = 5 * zoomFactor;
        var inset = 3 * zoomFactor;
        overlay.Children.Add(CreateOutlineLine(left + inset, y, right - inset, y, zoomFactor));
        overlay.Children.Add(CreateOutlineLine(left + inset, y, left + inset, y + tick, zoomFactor));
        overlay.Children.Add(CreateOutlineLine(right - inset, y, right - inset, y + tick, zoomFactor));
    }

    private static global::Avalonia.Controls.Shapes.Line CreateOutlineLine(
        double x1,
        double y1,
        double x2,
        double y2,
        double zoomFactor) =>
        new()
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = OutlineGlyphBrush,
            StrokeThickness = Math.Max(1, zoomFactor),
            IsHitTestVisible = false,
        };

    private static double GetOutlineLevelCenter(int level, double zoomFactor) =>
        (OutlineGutterPadding + (Math.Max(1, level) - 0.5) * OutlineLevelPitch) * zoomFactor;

    private static bool TryGetDisplayedRowOutlineSpan(
        IReadOnlyList<RowMetric> rows,
        OutlineGroupRange group,
        double headerHeight,
        double zoomFactor,
        out double top,
        out double bottom)
    {
        top = bottom = 0;
        var offset = headerHeight;
        var found = false;
        foreach (var row in rows)
        {
            var height = GetDisplayedRowHeight(row, zoomFactor);
            if (row.Row >= group.Start && row.Row <= group.End)
            {
                if (!found)
                {
                    top = offset;
                    found = true;
                }
                bottom = offset + height;
            }
            offset += height;
        }
        return found && bottom > top;
    }

    private static bool TryGetDisplayedColumnOutlineSpan(
        IReadOnlyList<ColMetric> columns,
        OutlineGroupRange group,
        double rowHeaderWidth,
        double zoomFactor,
        out double left,
        out double right)
    {
        left = right = 0;
        var offset = rowHeaderWidth;
        var found = false;
        foreach (var column in columns)
        {
            var width = GetDisplayedColumnWidth(column, zoomFactor);
            if (column.Col >= group.Start && column.Col <= group.End)
            {
                if (!found)
                {
                    left = offset;
                    found = true;
                }
                right = offset + width;
            }
            offset += width;
        }
        return found && right > left;
    }

    private static bool TryGetDisplayedRowBounds(
        IReadOnlyList<RowMetric> rows,
        uint target,
        double headerHeight,
        double zoomFactor,
        out double top,
        out double height)
    {
        top = headerHeight;
        height = 0;
        foreach (var row in rows)
        {
            height = GetDisplayedRowHeight(row, zoomFactor);
            if (row.Row == target)
                return true;
            top += height;
        }
        return false;
    }

    private static bool TryGetDisplayedColumnBounds(
        IReadOnlyList<ColMetric> columns,
        uint target,
        double rowHeaderWidth,
        double zoomFactor,
        out double left,
        out double width)
    {
        left = rowHeaderWidth;
        width = 0;
        foreach (var column in columns)
        {
            width = GetDisplayedColumnWidth(column, zoomFactor);
            if (column.Col == target)
                return true;
            left += width;
        }
        return false;
    }
}
