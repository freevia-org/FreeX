using System.Linq;
using System.Text;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Section segments are measured over the RENDERED top-level blocks, which are not one per model block:
/// a run of list paragraphs coalesces into one WPF List and a table that pre-splits renders as one block
/// per page piece. Segmenting by model index shifted every later section by that difference -- each
/// section then opened on the previous section's empty section-break paragraph, alone on a blank page
/// (the Persian A5 book in the fidelity corpus printed one blank page per section), or lost blocks.
/// </summary>
public sealed class SectionSegmentRenderedBlockMapTests
{
    [StaFact]
    public void ListBeforeASectionBreak_DoesNotShiftTheNextSectionsBlocks()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        for (var i = 0; i < 5; i++)
        {
            doc.Blocks.Add(new Paragraph($"bullet {i}")
            {
                Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet }
            });
        }
        doc.Blocks.Add(new Paragraph(string.Empty)
        {
            SectionBreak = new FreeW.Core.Model.Section(LandscapePage(), SectionBreakKind.NextPage)
        });
        doc.Blocks.Add(new Paragraph("second section opening"));
        doc.Blocks.Add(new Paragraph("second section closing"));

        var editor = new DocumentView();
        editor.LoadModel(doc);
        var assignment = PaginationEngine.ComputeBlockPageAssignment(editor);

        Assert.All(assignment.Take(6), page => Assert.Equal(0, page));
        Assert.Equal(1, assignment[6]);
        Assert.Equal(1, assignment[7]);

        var printed = PrintedPages(editor);
        Assert.Equal(2, printed.Length);
        Assert.Contains("bullet 4", printed[0]);
        Assert.Contains("second section opening", printed[1]);
        Assert.Contains("second section closing", printed[1]);
    }

    [StaFact]
    public void PreSplitTableBeforeASectionBreak_DoesNotLeaveABlankPageAheadOfTheNextSection()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = new Table();
        table.ColumnWidthsPt.AddRange([200, 200]);
        for (var row = 0; row < 90; row++)
            table.Rows.Add(new TableRow { Cells = { new TableCell($"row {row}"), new TableCell("value") } });
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph(string.Empty)
        {
            SectionBreak = new FreeW.Core.Model.Section(LandscapePage(), SectionBreakKind.NextPage)
        });
        doc.Blocks.Add(new Paragraph("second section opening"));

        var editor = new DocumentView();
        editor.LoadModel(doc);
        var renderedStart = PaginationEngine.MapModelBlocksToRenderedBlocks(
            doc, PrintLayout.BuildPaginatedDocument(editor).Blocks.Count);
        Assert.NotNull(renderedStart);
        Assert.True(renderedStart![1] > 1, "the fixture table must pre-split into several rendered blocks");

        var printed = PrintedPages(editor);
        var opening = System.Array.FindIndex(printed, page => page.Contains("second section opening"));
        Assert.True(opening > 0);
        // The page before the second section's first page still belongs to the table.
        Assert.Contains("row", printed[opening - 1]);
    }

    // A section geometry that differs from the final section's routes printing through the
    // section-aware paginator, the path that measures one segment per section.
    private static PageSettings LandscapePage() => new()
    {
        WidthPt = 792,
        HeightPt = 612,
        Landscape = true,
        MarginLeftPt = 72,
        MarginRightPt = 72,
        MarginTopPt = 72,
        MarginBottomPt = 72,
    };

    private static string[] PrintedPages(DocumentView editor)
    {
        var paginator = PrintLayout.BuildPaginator(editor);
        Assert.IsType<SectionAwareDocumentPaginator>(paginator);
        paginator.ComputePageCount();
        return Enumerable.Range(0, paginator.PageCount)
            .Select(index => ExtractDrawnText(paginator.GetPage(index).Visual))
            .ToArray();
    }

    private static string ExtractDrawnText(Visual visual)
    {
        var sb = new StringBuilder();
        Append(VisualTreeHelper.GetDrawing(visual), sb);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
        {
            if (VisualTreeHelper.GetChild(visual, i) is Visual child)
                sb.Append(ExtractDrawnText(child));
        }
        return sb.ToString();
    }

    private static void Append(Drawing? drawing, StringBuilder sb)
    {
        switch (drawing)
        {
            case System.Windows.Media.DrawingGroup group:
                foreach (var child in group.Children)
                    Append(child, sb);
                break;
            case GlyphRunDrawing { GlyphRun.Characters: { } characters }:
                sb.Append(characters.ToArray());
                break;
        }
    }
}
