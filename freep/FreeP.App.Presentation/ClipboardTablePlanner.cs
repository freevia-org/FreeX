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

        foreach (var cells in cellRows)
        {
            var row = new TableRow { HeightEmu = rowHeight };
            for (int column = 0; column < columnCount; column++)
            {
                var cell = column < cells.Count
                    ? cells[column]
                    : new TextBody { Paragraphs = { new Paragraph { Runs = { new Run() } } } };
                row.Cells.Add(new TableCell { TextBody = cell });
            }
            table.Rows.Add(row);
        }

        return true;
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
            var paragraph = InCanvasRichTextParagraphEditPlanner.CloneParagraphMetadata(source);
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
