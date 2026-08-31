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

    /// <summary>
    /// Splits one row's paragraph into per-column cell text bodies on tab boundaries, undoing the
    /// RFC4180-style quoting FreeX's plain-text clipboard serializer applies to any field containing
    /// a delimiter, a quote, or a newline (<c>ClipboardSerializer.AppendTsvCell</c> /
    /// <c>RequiresTsvQuoting</c> in FreeX.Core.Commands). A tab inside a genuinely quoted field is
    /// cell content, not a column boundary; a doubled quote (<c>""</c>) inside one collapses to a
    /// single literal quote; and the field's own wrapping quotes are dropped. The same disagreement
    /// this method resolves is already resolved, for FreeX pasting into itself, by
    /// <c>ClipboardSerializer.Deserialize</c> / <c>IsProperlyQuotedField</c> -- this mirrors that
    /// algorithm rather than inventing a third one.
    /// <para>
    /// This method also receives paragraphs built by the RTF/XAML rich-clipboard table projection
    /// (<see cref="EditingSession.InsertTableFromClipboard"/>'s other caller), whose cell text is
    /// literal user content that was never CSV-quoted -- a Word table cell containing a typed
    /// quotation mark must not have it stripped. <see cref="IsProperlyQuotedCell"/> guards this: a
    /// leading quote only opens quoting when a genuine closing quote (immediately followed by a tab
    /// or the row's end) can be found ahead of it, exactly as FreeX's own reader requires, so an
    /// ordinary stray quote in rich text is left alone as data.
    /// </para>
    /// </summary>
    private static List<TextBody> SplitCells(Paragraph source)
    {
        var chars = new List<char>();
        var owners = new List<Run>();
        foreach (var sourceRun in source.Runs)
        {
            var text = sourceRun.Text ?? string.Empty;
            foreach (char character in text)
            {
                chars.Add(character);
                owners.Add(sourceRun);
            }
        }

        var cellRuns = new List<List<Run>> { new() };
        var segment = new StringBuilder();
        Run? segmentOwner = null;

        void Flush()
        {
            if (segment.Length > 0)
                AddRun(cellRuns[^1], segmentOwner!, segment);
            segment.Clear();
            segmentOwner = null;
        }

        void Append(char character, Run owner)
        {
            if (segmentOwner is not null && !ReferenceEquals(segmentOwner, owner))
                Flush();
            segmentOwner ??= owner;
            segment.Append(character);
        }

        var inQuotes = false;
        var atFieldStart = true;
        var i = 0;
        while (i < chars.Count)
        {
            var character = chars[i];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (i + 1 < chars.Count && chars[i + 1] == '"')
                    {
                        Append('"', owners[i]);
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    atFieldStart = false;
                    i++;
                    continue;
                }

                Append(character, owners[i]);
                i++;
                continue;
            }

            if (character == '"' && atFieldStart && IsProperlyQuotedCell(chars, i))
            {
                inQuotes = true;
                atFieldStart = false;
                i++;
                continue;
            }

            if (character == '\t')
            {
                Flush();
                cellRuns.Add(new List<Run>());
                atFieldStart = true;
                i++;
                continue;
            }

            Append(character, owners[i]);
            atFieldStart = false;
            i++;
        }

        Flush();

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

    /// <summary>Delegates to the shared quote scanner that the tabular-shape check in
    /// <see cref="PresentationClipboardContent.HasTabularText"/> also uses, so both sides of the
    /// paste -- deciding a payload is a grid, and cutting that grid into cells -- agree on where a
    /// field boundary is. See <see cref="ClipboardTsvFields.OpensQuotedField"/>.</summary>
    private static bool IsProperlyQuotedCell(List<char> chars, int quoteIndex) =>
        ClipboardTsvFields.OpensQuotedField(chars, quoteIndex);

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
