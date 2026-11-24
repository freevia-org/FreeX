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
        IReadOnlyList<ColMetric> colMetrics,
        Dictionary<(uint Row, uint Col), DisplayCell> cellsByAddress)
    {
        var viewport = _session.Viewport;
        var sheet = _session.ActiveSheet;
        var zoom = GetActiveZoomFactor();
        var showHeadings = _session.IsShowingHeadings;
        var rowSlots = new Dictionary<uint, (double Offset, double Size)>();
        var columnSlots = new Dictionary<uint, (double Offset, double Size)>();
        var top = showHeadings ? GetColumnHeaderHeight(viewport, zoom) : 0;
        foreach (var metric in rowMetrics)
        {
            var height = GetDisplayedRowHeight(metric, zoom);
            rowSlots.TryAdd(metric.Row, (top, height));
            top += height;
        }

        var left = showHeadings ? GetRowHeaderWidth(viewport, zoom) : 0;
        foreach (var metric in colMetrics)
        {
            var width = GetDisplayedColumnWidth(metric, zoom);
            columnSlots.TryAdd(metric.Col, (left, width));
            left += width;
        }

        // The automation surface describes logical coordinates, including the cells hidden
        // beneath a rendered merged span. Its data must come from this viewport's literal cells,
        // never from the substitute anchor that the visual renderer uses for merged content.
        var coordinates = new List<(uint Row, uint Column)>(rowSlots.Count * columnSlots.Count);
        foreach (var row in rowSlots.Keys)
            foreach (var column in columnSlots.Keys)
                coordinates.Add((row, column));

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
            var bounds = inViewport
                ? new Rect(columnSlot.Offset, rowSlot.Offset, columnSlot.Size, rowSlot.Size)
                : default;
            var cell = default(DisplayCell);
            var hasCell = inViewport && cellsByAddress.TryGetValue((row, column), out cell);
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

        _sheetGridHost.UpdateAutomationViewport(surface, coordinates, ReadCell);
    }
}
