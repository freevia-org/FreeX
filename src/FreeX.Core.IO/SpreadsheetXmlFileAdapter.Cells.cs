using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class SpreadsheetXmlFileAdapter
{
    private static List<SpreadsheetXmlCell> BuildSortedXmlCells(Sheet sheet)
    {
        var mergeStarts = new Dictionary<(uint Row, uint Col), GridRange>(sheet.MergedRegions.Count);
        foreach (var region in sheet.MergedRegions)
        {
            if (IsValidGridRange(region))
                mergeStarts.TryAdd((region.Start.Row, region.Start.Col), region);
        }

        var cellCapacity = EstimateRichCellCapacity(sheet);
        var emitted = new HashSet<(uint Row, uint Col)>(cellCapacity);
        var cells = new List<SpreadsheetXmlCell>(cellCapacity);

        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (!IsValidCellAddress(row, col))
                continue;

            var address = new CellAddress(sheet.Id, row, col);
            if (IsCoveredByMergeNonAnchor(sheet, address))
                continue;

            mergeStarts.TryGetValue((row, col), out var mergeRange);
            sheet.Hyperlinks.TryGetValue(address, out var hyperlinkTarget);
            sheet.HyperlinkMetadata.TryGetValue(address, out var hyperlinkMetadata);
            sheet.Comments.TryGetValue(address, out var comment);
            emitted.Add((row, col));
            cells.Add(new SpreadsheetXmlCell(row, col, cell, mergeRange, hyperlinkTarget, hyperlinkMetadata, comment));
        }

        foreach (var (address, hyperlinkTarget) in sheet.Hyperlinks)
        {
            if (!IsValidCellAddress(address.Row, address.Col) || emitted.Contains((address.Row, address.Col)))
                continue;

            if (IsCoveredByMergeNonAnchor(sheet, address))
                continue;

            mergeStarts.TryGetValue((address.Row, address.Col), out var mergeRange);
            sheet.HyperlinkMetadata.TryGetValue(address, out var hyperlinkMetadata);
            sheet.Comments.TryGetValue(address, out var comment);
            emitted.Add((address.Row, address.Col));
            cells.Add(new SpreadsheetXmlCell(
                address.Row,
                address.Col,
                Cell.FromValue(BlankValue.Instance),
                mergeRange,
                hyperlinkTarget,
                hyperlinkMetadata,
                comment));
        }

        foreach (var (address, comment) in sheet.Comments)
        {
            if (!IsValidCellAddress(address.Row, address.Col) || emitted.Contains((address.Row, address.Col)))
                continue;

            if (IsCoveredByMergeNonAnchor(sheet, address))
                continue;

            mergeStarts.TryGetValue((address.Row, address.Col), out var mergeRange);
            emitted.Add((address.Row, address.Col));
            cells.Add(new SpreadsheetXmlCell(
                address.Row,
                address.Col,
                Cell.FromValue(BlankValue.Instance),
                mergeRange,
                HyperlinkTarget: null,
                HyperlinkMetadata: null,
                Comment: comment));
        }

        foreach (var (key, styleId) in sheet.GetStyleOnlyEntries())
        {
            if (!IsValidCellAddress(key.Row, key.Col) ||
                emitted.Contains((key.Row, key.Col)) ||
                styleId == StyleId.Default)
            {
                continue;
            }

            var address = new CellAddress(sheet.Id, key.Row, key.Col);
            if (IsCoveredByMergeNonAnchor(sheet, address))
                continue;

            mergeStarts.TryGetValue((key.Row, key.Col), out var mergeRange);
            emitted.Add((key.Row, key.Col));
            cells.Add(new SpreadsheetXmlCell(
                key.Row,
                key.Col,
                new Cell { StyleId = styleId },
                mergeRange,
                HyperlinkTarget: null,
                HyperlinkMetadata: null,
                Comment: null));
        }

        foreach (var mergeRange in sheet.MergedRegions)
        {
            if (!IsValidGridRange(mergeRange) ||
                emitted.Contains((mergeRange.Start.Row, mergeRange.Start.Col)))
            {
                continue;
            }

            cells.Add(new SpreadsheetXmlCell(
                mergeRange.Start.Row,
                mergeRange.Start.Col,
                Cell.FromValue(BlankValue.Instance),
                mergeRange,
                HyperlinkTarget: null,
                HyperlinkMetadata: null,
                Comment: null));
        }

        cells.Sort(static (left, right) =>
        {
            var rowComparison = left.Row.CompareTo(right.Row);
            return rowComparison != 0 ? rowComparison : left.Col.CompareTo(right.Col);
        });
        return cells;
    }

    private static int EstimateRichCellCapacity(Sheet sheet)
    {
        var capacity = sheet.CellCount;
        capacity = AddClamped(capacity, sheet.Hyperlinks.Count);
        capacity = AddClamped(capacity, sheet.Comments.Count);
        capacity = AddClamped(capacity, sheet.MergedRegions.Count);
        return capacity;
    }

    private static int AddClamped(int value, int add) =>
        value > int.MaxValue - add ? int.MaxValue : value + add;

    private static bool IsCoveredByMergeNonAnchor(Sheet sheet, CellAddress address) =>
        sheet.GetMergeRegion(address) is { } mergeRange &&
        (mergeRange.Start.Row != address.Row || mergeRange.Start.Col != address.Col);

    private static bool IsValidCellAddress(uint row, uint column) =>
        row is >= 1 and <= CellAddress.MaxRow &&
        column is >= 1 and <= CellAddress.MaxCol;

    private static bool IsValidGridRange(GridRange range) =>
        range.Start.Sheet == range.End.Sheet &&
        IsValidCellAddress(range.Start.Row, range.Start.Col) &&
        IsValidCellAddress(range.End.Row, range.End.Col);

    private static bool CanStreamValueCellRows(Sheet sheet) =>
        sheet.MergedRegions.Count == 0 &&
        sheet.Hyperlinks.Count == 0 &&
        sheet.HyperlinkMetadata.Count == 0 &&
        sheet.Comments.Count == 0 &&
        !sheet.HasStyleOnlyCells;

    private static bool IsRowColumnOrdered(IReadOnlyDictionary<(uint Row, uint Col), Cell> cells)
    {
        var hasPrevious = false;
        var previousRow = 0u;
        var previousCol = 0u;
        foreach (var ((row, col), _) in cells)
        {
            if (hasPrevious && (row < previousRow || (row == previousRow && col < previousCol)))
                return false;

            hasPrevious = true;
            previousRow = row;
            previousCol = col;
        }

        return true;
    }

    private static Dictionary<uint, List<SpreadsheetXmlValueCell>> BuildValueCellsByRow(
        IReadOnlyDictionary<(uint Row, uint Col), Cell> cells)
    {
        var rowCounts = new Dictionary<uint, int>(Math.Min(cells.Count, 1024));
        foreach (var ((row, col), _) in cells)
        {
            if (!IsValidCellAddress(row, col))
                continue;

            if (!rowCounts.TryAdd(row, 1))
                rowCounts[row]++;
        }

        var cellsByRow = new Dictionary<uint, List<SpreadsheetXmlValueCell>>(rowCounts.Count);
        foreach (var (row, count) in rowCounts)
            cellsByRow.Add(row, new List<SpreadsheetXmlValueCell>(count));

        foreach (var ((row, col), cell) in cells)
        {
            if (IsValidCellAddress(row, col))
                cellsByRow[row].Add(new SpreadsheetXmlValueCell(col, cell));
        }

        foreach (var rowCells in cellsByRow.Values)
            rowCells.Sort(static (left, right) => left.Col.CompareTo(right.Col));

        return cellsByRow;
    }

    private readonly record struct SpreadsheetXmlCell(
        uint Row,
        uint Col,
        Cell Cell,
        GridRange? MergeRange,
        string? HyperlinkTarget,
        HyperlinkMetadata? HyperlinkMetadata,
        string? Comment);

    private readonly record struct SpreadsheetXmlValueCell(uint Col, Cell Cell);
}
