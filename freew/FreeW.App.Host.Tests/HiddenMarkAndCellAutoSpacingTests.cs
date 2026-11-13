using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// drawing.docx (HTML-sourced) trailed Word by a paragraph: empty spacer paragraphs whose mark is hidden
/// took a full empty line, and "Normal (Web)" automatic spacing did not collapse at table-cell edges.
/// </summary>
public sealed class HiddenMarkAndCellAutoSpacingTests
{
    private const double PxPerPoint = 96.0 / 72.0;

    [StaFact]
    public void EmptyHiddenMarkParagraph_RendersCollapsed_AndCommitKeepsItsSpacing()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("before"));
        doc.Blocks.Add(new Paragraph
        {
            MarkFormatting = RunFormatting.Default with { Hidden = true },
            Formatting = ParagraphFormatting.Default with
            {
                SpaceBeforePt = 6,
                SpaceAfterPt = 12,
                SpaceBeforeIsSet = true,
                SpaceAfterIsSet = true,
            },
        });
        doc.Blocks.Add(new Paragraph("after"));

        var view = new DocumentView();
        view.LoadModel(doc);

        var spacer = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ElementAt(1);
        Assert.Equal(0, spacer.Margin.Top);
        Assert.Equal(0, spacer.Margin.Bottom);
        Assert.True(spacer.LineHeight < 0.1);

        view.CommitToModel();
        var committed = Assert.IsType<Paragraph>(view.Model.Blocks[1]);
        Assert.Equal(6, committed.Formatting.SpaceBeforePt, 3);
        Assert.Equal(12, committed.Formatting.SpaceAfterPt, 3);
        Assert.NotEqual(LineSpacingRule.Exact, committed.Formatting.LineRule);
    }

    [StaFact]
    public void CellEdgeAutomaticSpacing_IsSuppressed_NumericSpacingKept()
    {
        var doc = TextDocument.CreateEmpty();
        var auto = ParagraphFormatting.Default with
        {
            SpaceBeforePt = 14,
            SpaceAfterPt = 14,
            SpaceBeforeIsSet = true,
            SpaceAfterIsSet = true,
            BeforeAutoSpacing = true,
            AfterAutoSpacing = true,
        };
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph("first") { Formatting = auto });
        cell.Paragraphs.Add(new Paragraph("last") { Formatting = auto });
        var numeric = new TableCell();
        numeric.Paragraphs.Add(new Paragraph("numeric")
        {
            Formatting = auto with { BeforeAutoSpacing = false, AfterAutoSpacing = false },
        });
        var row = new TableRow();
        row.Cells.Add(cell);
        row.Cells.Add(numeric);
        var table = new Table();
        table.Rows.Add(row);
        doc.Blocks.Clear();
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph());

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpfTable = view.Document.Blocks.OfType<System.Windows.Documents.Table>().Single();
        var cells = wpfTable.RowGroups[0].Rows[0].Cells;
        var autoParagraphs = cells[0].Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();
        Assert.Equal(0, autoParagraphs[0].Margin.Top);
        Assert.Equal(14 * PxPerPoint, autoParagraphs[0].Margin.Bottom, 1);
        Assert.Equal(14 * PxPerPoint, autoParagraphs[1].Margin.Top, 1);
        Assert.Equal(0, autoParagraphs[1].Margin.Bottom);

        var numericParagraph = cells[1].Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        Assert.Equal(14 * PxPerPoint, numericParagraph.Margin.Top, 1);
        Assert.Equal(14 * PxPerPoint, numericParagraph.Margin.Bottom, 1);
    }
}
