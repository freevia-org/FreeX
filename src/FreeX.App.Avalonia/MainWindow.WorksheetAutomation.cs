using Avalonia;
using FreeX.App.Presentation.Accessibility;
using FreeX.Core.Model;
using AvaloniaGrid = Avalonia.Controls.Grid;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private void UpdateWorksheetAutomation(
        AvaloniaGrid surface,
        IReadOnlyList<RowMetric> rowMetrics,
        IReadOnlyList<ColMetric> colMetrics)
    {
        var viewport = _session.Viewport;
        var sheet = _session.ActiveSheet;
        var zoom = GetActiveZoomFactor();
        var showHeadings = _session.IsShowingHeadings;
        var rowSlots = new Dictionary<uint, (int Index, double Offset, double Size)>();
        var columnSlots = new Dictionary<uint, (int Index, double Offset, double Size)>();
        var top = showHeadings ? GetColumnHeaderHeight(viewport, zoom) : 0;
        var rowIndex = showHeadings ? 1 : 0;
        foreach (var metric in rowMetrics)
        {
            var height = GetDisplayedRowHeight(metric, zoom);
            rowSlots.TryAdd(metric.Row, (rowIndex++, top, height));
            top += height;
        }

        var left = showHeadings ? GetRowHeaderWidth(viewport, zoom) : 0;
        var columnIndex = showHeadings ? 1 : 0;
        foreach (var metric in colMetrics)
        {
            var width = GetDisplayedColumnWidth(metric, zoom);
            columnSlots.TryAdd(metric.Col, (columnIndex++, left, width));
            left += width;
        }

        // The automation surface describes logical coordinates, including the cells hidden
        // beneath a rendered merged span. Its data must come from this viewport's literal cells,
        // never from the substitute anchor that the visual renderer uses for merged content.
        // Logical UIA axes follow WPF GridView's main-first, distinct split metric order.
        // Physical slot maps above remain renderer-owned and may cover only a subset of
        // this Cartesian product. A valid logical item need not have a visible slot.
        var rows = viewport.RowMetrics.Select(metric => metric.Row)
            .Concat(viewport.SplitPanes?.TopRows?.Select(metric => metric.Row) ?? [])
            .Concat(viewport.SplitPanes?.BottomLeftRows?.Select(metric => metric.Row) ?? [])
            .Distinct().ToArray();
        var columns = viewport.ColMetrics.Select(metric => metric.Col)
            .Concat(viewport.SplitPanes?.LeftColumns?.Select(metric => metric.Col) ?? [])
            .Concat(viewport.SplitPanes?.TopRightColumns?.Select(metric => metric.Col) ?? [])
            .Distinct().ToArray();
        var logicalRows = rows.ToHashSet();
        var logicalColumns = columns.ToHashSet();
        var literalCells = new Dictionary<(uint Row, uint Col), DisplayCell>();
        // Match WPF's lookup precedence when independent pane snapshots overlap.
        foreach (var cell in viewport.Cells.Concat(viewport.SplitPanes?.Cells ?? []))
            literalCells.TryAdd((cell.Row, cell.Col), cell);

        var sheetId = sheet.Id;
        var active = _session.ActiveCell;
        GridRange[] selected = _session.SelectedRanges.Count > 0
            ? _session.SelectedRanges.ToArray()
            : [_session.SelectedRange];
        var merges = sheet.MergedRegions.ToArray();
        var hyperlinks = sheet.Hyperlinks.Keys.ToHashSet();

        // Each refresh replaces the host's reader with a complete snapshot. Capturing these
        // inputs together prevents a retained coordinate peer from mixing an old viewport with
        // the newly active sheet's metadata during a sheet or workbook transition.
        WorksheetAutomationCell ReadCell(uint row, uint column)
        {
            var address = new CellAddress(sheetId, row, column);
            var hasRow = rowSlots.TryGetValue(row, out var rowSlot);
            var hasColumn = columnSlots.TryGetValue(column, out var columnSlot);
            var inViewport = hasRow && hasColumn;
            // Grid layout may honor a header/control minimum beyond the requested metric
            // (e.g. a 30px row header arranges at32px). Use the actual physical definitions
            // after layout; keep literal merged members as individual unit slots.
            var actualRow = hasRow && rowSlot.Index < surface.RowDefinitions.Count && surface.RowDefinitions[rowSlot.Index].ActualHeight > 0;
            var actualColumn = hasColumn && columnSlot.Index < surface.ColumnDefinitions.Count && surface.ColumnDefinitions[columnSlot.Index].ActualWidth > 0;
            var y = actualRow ? surface.RowDefinitions.Take(rowSlot.Index).Sum(definition => definition.ActualHeight) : rowSlot.Offset;
            var x = actualColumn ? surface.ColumnDefinitions.Take(columnSlot.Index).Sum(definition => definition.ActualWidth) : columnSlot.Offset;
            // The renderer gives each Border its metric Width/Height. A larger arranged
            // Grid slot centers that fixed-size control, then applies layout pixel rounding.
            // Follow that placement for literal unit cells, including covered merge members.
            var height = rowSlot.Size;
            var width = columnSlot.Size;
            if (actualRow) y += (surface.RowDefinitions[rowSlot.Index].ActualHeight - height) / 2;
            if (actualColumn) x += (surface.ColumnDefinitions[columnSlot.Index].ActualWidth - width) / 2;
            if (surface.UseLayoutRounding)
            {
                var scale = global::Avalonia.Controls.TopLevel.GetTopLevel(surface)?.RenderScaling ?? 1;
                x = global::Avalonia.Layout.LayoutHelper.RoundLayoutValue(x, scale);
                y = global::Avalonia.Layout.LayoutHelper.RoundLayoutValue(y, scale);
                width = global::Avalonia.Layout.LayoutHelper.RoundLayoutValue(width, scale);
                height = global::Avalonia.Layout.LayoutHelper.RoundLayoutValue(height, scale);
            }
            var bounds = inViewport ? new Rect(x, y, width, height) : default;
            var cell = default(DisplayCell);
            var hasCell = logicalRows.Contains(row) && logicalColumns.Contains(column) &&
                literalCells.TryGetValue((row, column), out cell);
            var value = hasCell ? cell.DisplayText : "";
            var comment = hasCell && cell.HasComment ? cell.CommentDisplay : null;
            var metadata = new CellAnnouncementMetadata(
                HasComment: comment is not null,
                CommentTitle: comment?.Title,
                IsFormula: hasCell && !string.IsNullOrEmpty(cell.Formula),
                IsMerged: merges.Any(merge => merge.Contains(address)),
                HasHyperlink: hyperlinks.Contains(address));
            var name = CellAnnouncementPlanner.BuildName(
                $"{CellAddress.NumberToColumnName(column)}{row}", value, metadata);

            return new WorksheetAutomationCell(
                name,
                value,
                comment is null ? "" : $"{comment.Title}: {comment.Body}",
                selected.Any(range => range.Contains(address)),
                active == address,
                bounds);
        }

        _sheetGridHost.UpdateAutomationViewport(surface, rows, columns, ReadCell);
    }
}
