using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class AccessibilityCheckerService
{
    private static void AddHiddenContentIssues(List<AccessibilityIssue> issues, Sheet sheet)
    {
        if (!sheet.IsHidden &&
            !sheet.IsVeryHidden &&
            sheet.HiddenRows.Count == 0 &&
            sheet.FilterHiddenRows.Count == 0 &&
            sheet.GroupHiddenRows.Count == 0 &&
            sheet.HiddenCols.Count == 0 &&
            sheet.GroupHiddenCols.Count == 0)
        {
            return;
        }

        var hasContent = false;
        HashSet<uint>? hiddenRows = null;
        HashSet<uint>? hiddenCols = null;
        foreach (var ((row, col), _) in sheet.GetOccupiedCellMap())
        {
            MarkHiddenContentAddress(
                sheet,
                new CellAddress(sheet.Id, row, col),
                ref hasContent,
                ref hiddenRows,
                ref hiddenCols);
        }

        foreach (var address in sheet.Comments.Keys)
        {
            MarkHiddenContentAddress(sheet, address, ref hasContent, ref hiddenRows, ref hiddenCols);
        }

        foreach (var address in sheet.ThreadedComments.Keys)
        {
            MarkHiddenContentAddress(sheet, address, ref hasContent, ref hiddenRows, ref hiddenCols);
        }

        foreach (var address in sheet.Hyperlinks.Keys)
        {
            MarkHiddenContentAddress(sheet, address, ref hasContent, ref hiddenRows, ref hiddenCols);
        }

        foreach (var table in sheet.StructuredTables)
        {
            MarkHiddenContentRange(sheet, table.Range, ref hasContent, ref hiddenRows, ref hiddenCols);
        }

        foreach (var sparkline in sheet.Sparklines)
        {
            MarkHiddenContentAddress(sheet, sparkline.Location, ref hasContent, ref hiddenRows, ref hiddenCols);
        }

        foreach (var picture in sheet.Pictures)
        {
            if (picture.IsVisible)
                MarkHiddenContentAddress(sheet, picture.Anchor, ref hasContent, ref hiddenRows, ref hiddenCols);
        }

        foreach (var shape in sheet.DrawingShapes)
        {
            if (shape.IsVisible)
                MarkHiddenContentAddress(sheet, shape.Anchor, ref hasContent, ref hiddenRows, ref hiddenCols);
        }

        foreach (var textBox in sheet.TextBoxes)
        {
            if (textBox.IsVisible)
                MarkHiddenContentAddress(sheet, textBox.Anchor, ref hasContent, ref hiddenRows, ref hiddenCols);
        }

        if (!hasContent)
            return;

        if (sheet.IsHidden || sheet.IsVeryHidden)
        {
            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.HiddenSheetWithContent,
                sheet.Id,
                sheet.Name,
                sheet.Name,
                "Hidden sheets with content may not be available to assistive technologies."));
        }

        if (hiddenRows is not null)
        {
            var rows = hiddenRows.ToList();
            rows.Sort();
            foreach (var row in rows)
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.HiddenRowWithContent,
                    sheet.Id,
                    sheet.Name,
                    $"{row}:{row}",
                    "Hidden rows with content may not be available to assistive technologies."));
            }
        }

        if (hiddenCols is not null)
        {
            var cols = hiddenCols.ToList();
            cols.Sort();
            foreach (var col in cols)
            {
                var name = CellAddress.NumberToColumnName(col);
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.HiddenColumnWithContent,
                    sheet.Id,
                    sheet.Name,
                    $"{name}:{name}",
                    "Hidden columns with content may not be available to assistive technologies."));
            }
        }
    }

    private static void MarkHiddenContentAddress(
        Sheet sheet,
        CellAddress address,
        ref bool hasContent,
        ref HashSet<uint>? hiddenRows,
        ref HashSet<uint>? hiddenCols)
    {
        hasContent = true;
        if (sheet.IsRowEffectivelyHidden(address.Row))
        {
            hiddenRows ??= [];
            hiddenRows.Add(address.Row);
        }

        if (sheet.IsColEffectivelyHidden(address.Col))
        {
            hiddenCols ??= [];
            hiddenCols.Add(address.Col);
        }
    }

    private static void MarkHiddenContentRange(
        Sheet sheet,
        GridRange range,
        ref bool hasContent,
        ref HashSet<uint>? hiddenRows,
        ref HashSet<uint>? hiddenCols)
    {
        hasContent = true;
        MarkHiddenRowsInRange(sheet.HiddenRows, range, ref hiddenRows);
        MarkHiddenRowsInRange(sheet.FilterHiddenRows, range, ref hiddenRows);
        MarkHiddenRowsInRange(sheet.GroupHiddenRows, range, ref hiddenRows);
        MarkHiddenColsInRange(sheet.HiddenCols, range, ref hiddenCols);
        MarkHiddenColsInRange(sheet.GroupHiddenCols, range, ref hiddenCols);
    }

    private static void MarkHiddenRowsInRange(HashSet<uint> rows, GridRange range, ref HashSet<uint>? hiddenRows)
    {
        foreach (var row in rows)
        {
            if (row < range.Start.Row || row > range.End.Row)
                continue;

            hiddenRows ??= [];
            hiddenRows.Add(row);
        }
    }

    private static void MarkHiddenColsInRange(HashSet<uint> cols, GridRange range, ref HashSet<uint>? hiddenCols)
    {
        foreach (var col in cols)
        {
            if (col < range.Start.Col || col > range.End.Col)
                continue;

            hiddenCols ??= [];
            hiddenCols.Add(col);
        }
    }
}
