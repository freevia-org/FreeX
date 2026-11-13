using System.Linq;
using FluentAssertions;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// A Word table-of-contents-like table (one entry per row, "exactly 17pt" lines with 6pt after, a dot-leader
/// right tab running to the cell's right edge) must render and paginate at Word's pitch. Three faults made
/// stress010-style TOCs spill onto extra pages: the last paragraph's space-after was counted twice (paragraph
/// margin AND cell padding), a second trailing tab at the full line wrapped onto a line of its own, and the
/// page-planner's leading-content estimate summed everything before the table instead of only its own page.
/// </summary>
public sealed class TableCellExactSpacingTocTests
{
    private const double CellWidthPt = 451.4;

    private static ParagraphFormatting ExactTocFormatting() => ParagraphFormatting.Default with
    {
        LineRule = LineSpacingRule.Exact,
        LineHeightPt = 17,
        LineSpacingIsSet = true,
        SpaceAfterPt = 6,
        SpaceAfterIsSet = true,
        TabStops = []
    };

    private static (TextDocument Document, Table Table) BuildTocTable(string entryText, string cellText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(1, 1);
        var cell = table.Rows[0].Cells[0];
        cell.WidthPt = CellWidthPt;
        cell.Paragraphs.Clear();
        cell.Paragraphs.Add(new Paragraph(cellText)
        {
            // Only the leader stop: the entry text is past every other stop by the time the tabs come.
            Formatting = ExactTocFormatting() with { TabStops = [new TabStop(CellWidthPt, TabStopAlignment.Right, TabLeader.Dots)] }
        });
        table.ColumnWidthsPt.Add(CellWidthPt);
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph(entryText));
        return (doc, table);
    }

    [StaFact]
    public void CellLastParagraphSpaceAfter_IsCountedOnce_NotAlsoAsCellPadding()
    {
        var (doc, _) = BuildTocTable("after", "Entry");
        var view = new DocumentView();
        view.LoadModel(doc);

        var table = view.Document.Blocks.OfType<System.Windows.Documents.Table>().Single();
        var cell = table.RowGroups.Single().Rows.Single().Cells.Single();
        var paragraph = cell.Blocks.OfType<WpfParagraph>().Single();

        paragraph.Margin.Bottom.Should().BeApproximately(6 * 96.0 / 72.0, 0.001);
        cell.Padding.Bottom.Should().Be(0, "the paragraph margin already carries the cell's trailing space-after");
    }

    [StaFact]
    public void SecondTrailingTab_AtTheFullLine_DoesNotTakeWidthThatWrapsIt()
    {
        var (doc, _) = BuildTocTable("after", "Entry\t\t");
        var view = new DocumentView();
        view.LoadModel(doc);

        var cell = view.Document.Blocks.OfType<System.Windows.Documents.Table>().Single()
            .RowGroups.Single().Rows.Single().Cells.Single();
        var tabs = cell.Blocks.OfType<WpfParagraph>().Single().Inlines
            .OfType<System.Windows.Documents.InlineUIContainer>()
            .Select(inline => inline.Child)
            .OfType<TabStopLeaderElement>()
            .ToList();

        tabs.Should().HaveCount(2);
        tabs[0].HasLeader.Should().BeTrue("the first tab runs the dot leader up to the cell edge");
        tabs[1].Width.Should().Be(0, "no room is left on the line, and a tab with nothing after it may overhang");
    }

    [Fact]
    public void LeadingContentEstimate_RestartsAtAPageBreakRun()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        for (var index = 0; index < 30; index++)
            doc.Blocks.Add(new Paragraph("cover page paragraph " + index));
        var heading = new Paragraph("Contents");
        heading.Runs.Insert(0, new Run(string.Empty) { IsPageBreak = true });
        doc.Blocks.Add(heading);
        doc.Blocks.Add(Table.Create(1, 1));

        var withBreak = DocumentViewLayoutPlanner.EstimateLeadingContentHeightDip(doc, doc.Blocks.Count - 1);
        var beforeBreak = DocumentViewLayoutPlanner.EstimateLeadingContentHeightDip(doc, 30);

        withBreak.Should().BeLessThan(beforeBreak / 4, "only the heading after the break shares the table's page");
    }

    [Fact]
    public void LeadingContentEstimate_RestartsAfterAPageTypeSectionBreak()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        for (var index = 0; index < 30; index++)
            doc.Blocks.Add(new Paragraph("cover page paragraph " + index));
        doc.Blocks.Add(DocumentOps.CreateSectionBreak(SectionBreakKind.NextPage));
        doc.Blocks.Add(Table.Create(1, 1));

        DocumentViewLayoutPlanner.EstimateLeadingContentHeightDip(doc, doc.Blocks.Count - 1).Should().Be(0);
    }

    [Fact]
    public void ExactLineCellRows_AreEstimatedAtTheirTruePitch()
    {
        var (doc, table) = BuildTocTable("after", "Entry");
        var plan = DocumentViewLayoutPlanner.BuildTablePaginationPlan(table, doc.Page);

        // 17pt exact line + 6pt space-after = 23pt = 30.67 DIP -- not the 18 DIP/line + 8 DIP gutter guess (26).
        plan.Rows.Single().EstimatedHeightDip.Should().BeApproximately(23 * 96.0 / 72.0, 0.02);
    }
}
