using System.Text;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Converts a standalone tab-delimited clipboard table into the native editable table model.
/// Rich text editors continue to use the projection because TextBody has no inline table node;
/// this planner is for slide-level paste, where a real Table shape is available.
/// </summary>
public static class ClipboardTablePlanner
{
    public static bool TryBuildStandaloneTable(
        TextBody body,
        IReadOnlyList<long>? columnWidthsEmu,
        IReadOnlyList<InCanvasRichClipboardTableCellStyle>? cellStyles,
        out TableShape table)
    {
        ArgumentNullException.ThrowIfNull(body);
        table = new TableShape
        {
            TableStyleId = "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}",
            Flags = new TableStyleFlags { FirstRow = true, BandRow = true },
        };

        var rows = body.Paragraphs
            .Where(paragraph => !string.IsNullOrEmpty(ParagraphText(paragraph)))
            .ToArray();
        if (rows.Length == 0 || rows.Any(row => !ParagraphText(row).Contains('\t')))
        {
            table = null!;
            return false;
        }

        var cellRows = rows.Select(SplitCells).ToArray();
        int columnCount = cellRows.Max(row => row.Count);
        if (columnCount < 2)
        {
            table = null!;
            return false;
        }

        const long widthEmu = 5_486_400;
        const long heightEmu = 2_743_200;
        long rowHeight = heightEmu / cellRows.Length;
        if (columnWidthsEmu is { Count: var widthCount }
            && widthCount == columnCount
            && columnWidthsEmu.All(width => width > 0))
        {
            table.ColumnWidthsEmu.AddRange(columnWidthsEmu);
        }
        else
        {
            long columnWidth = widthEmu / columnCount;
            for (int column = 0; column < columnCount; column++)
                table.ColumnWidthsEmu.Add(columnWidth);
        }

        int styleIndex = 0;
        foreach (var cells in cellRows)
        {
            var row = new TableRow { HeightEmu = rowHeight };
            for (int column = 0; column < columnCount; column++)
            {
                bool hasSourceCell = column < cells.Count;
                var cellBody = hasSourceCell
                    ? cells[column]
                    : new TextBody { Paragraphs = { new Paragraph { Runs = { new Run() } } } };
                var cell = new TableCell { TextBody = cellBody };
                if (hasSourceCell && cellStyles is { Count: > 0 } && styleIndex < cellStyles.Count)
                    ApplyCellStyle(cell, cellStyles[styleIndex]);
                if (hasSourceCell)
                    styleIndex++;
                row.Cells.Add(cell);
            }
            table.Rows.Add(row);
        }

        ApplyMergeTopology(table);

        return true;
    }

    private static void ApplyCellStyle(
        TableCell cell,
        InCanvasRichClipboardTableCellStyle style)
    {
        ClipboardTableCellStylePolicy.ApplyCore(cell, style);

        TableCellBorders? borders = null;
        borders = AssignBorder(borders, style.Left, TableCellBorderSide.Left);
        borders = AssignBorder(borders, style.Right, TableCellBorderSide.Right);
        borders = AssignBorder(borders, style.Top, TableCellBorderSide.Top);
        borders = AssignBorder(borders, style.Bottom, TableCellBorderSide.Bottom);
        cell.Borders = borders;
    }

    private static TableCellBorders? AssignBorder(
        TableCellBorders? borders,
        InCanvasRichClipboardTableBorder? source,
        TableCellBorderSide side)
    {
        if (source is null)
            return borders;

        borders ??= new TableCellBorders();
        ShapeOutline outline = source.IsNone
            ? ShapeOutline.None.Instance
            : new ShapeOutline.Visible(SrgbColor.FromRgb(source.ColorRgb), source.WidthPt);
        switch (side)
        {
            case TableCellBorderSide.Left: borders.Left = outline; break;
            case TableCellBorderSide.Right: borders.Right = outline; break;
            case TableCellBorderSide.Top: borders.Top = outline; break;
            case TableCellBorderSide.Bottom: borders.Bottom = outline; break;
        }
        return borders;
    }

    private static void ApplyMergeTopology(TableShape table)
    {
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (int column = 0; column < row.Cells.Count; column++)
            {
                var anchor = row.Cells[column];
                if (anchor.GridSpan <= 1)
                    continue;

                int span = 1;
                for (int continuation = column + 1;
                     continuation < row.Cells.Count && row.Cells[continuation].HMerge;
                     continuation++)
                {
                    span++;
                }

                anchor.GridSpan = span;
            }
        }

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (int column = 0; column < row.Cells.Count; column++)
            {
                var anchor = row.Cells[column];
                if (anchor.RowSpan <= 1)
                    continue;

                int span = 1;
                for (int continuationRow = rowIndex + 1;
                     continuationRow < table.Rows.Count
                     && column < table.Rows[continuationRow].Cells.Count
                     && table.Rows[continuationRow].Cells[column].VMerge;
                     continuationRow++)
                {
                    span++;
                }

                anchor.RowSpan = span;
            }
        }
    }

    private static List<TextBody> SplitCells(Paragraph source)
    {
        var cellRuns = new List<List<Run>> { new() };
        foreach (var sourceRun in source.Runs)
        {
            var text = sourceRun.Text ?? string.Empty;
            var segment = new StringBuilder();
            foreach (char character in text)
            {
                if (character == '\t')
                {
                    AddRun(cellRuns[^1], sourceRun, segment);
                    cellRuns.Add(new List<Run>());
                    segment.Clear();
                }
                else
                {
                    segment.Append(character);
                }
            }
            AddRun(cellRuns[^1], sourceRun, segment);
        }

        return cellRuns.Select(runs =>
        {
            var paragraph = TextBodyModelCloner.CloneParagraphMetadata(source);
            paragraph.Runs.Clear();
            foreach (var run in runs)
                paragraph.Runs.Add(run);
            if (paragraph.Runs.Count == 0)
                paragraph.Runs.Add(new Run());
            return new TextBody { Paragraphs = { paragraph, } };
        }).ToList();
    }

    private static void AddRun(List<Run> target, Run source, StringBuilder text)
    {
        if (text.Length == 0)
            return;
        var run = TextBodyModelCloner.CloneRun(source);
        run.Text = text.ToString();
        target.Add(run);
    }

    private static string ParagraphText(Paragraph paragraph) =>
        string.Concat(paragraph.Runs.Select(run => run.Text));
}
