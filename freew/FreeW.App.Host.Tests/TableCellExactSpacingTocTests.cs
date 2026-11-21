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
    public void LeadingContentEstimate_ReservesTheNoteBandOnlyForAPageThatCarriesANoteReference()
    {
        static TextDocument Build(bool referenceOnTablePage)
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Footnotes[1] = new Footnote(1, "a note");
            var earlier = new Paragraph("an earlier page with a note");
            earlier.Runs.Add(new Run("1") { FootnoteId = 1 });
            doc.Blocks.Add(earlier);
            var heading = new Paragraph("Contents");
            heading.Runs.Insert(0, new Run(string.Empty) { IsPageBreak = true });
            if (referenceOnTablePage)
                heading.Runs.Add(new Run("1") { FootnoteId = 1 });
            doc.Blocks.Add(heading);
            doc.Blocks.Add(Table.Create(1, 1));
            return doc;
        }

        var without = DocumentViewLayoutPlanner.EstimateLeadingContentHeightDip(Build(false), 2);
        var with = DocumentViewLayoutPlanner.EstimateLeadingContentHeightDip(Build(true), 2);

        // The 80 DIP band belongs to a page that has a note; a footnoted document's note-free table page
        // must not lose it (that pushed the last rows of a table onto an extra page).
        (with - without).Should().BeApproximately(80, 0.01);
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

    [Fact]
    public void OneLinePersianCell_IsNotEstimatedAsTwoLines()
    {
        // 35 characters at 15pt in a ~300pt cell: Latin text of that length wraps, Arabic-script text does not.
        var table = Table.Create(1, 1);
        var cell = table.Rows[0].Cells[0];
        cell.WidthPt = 300;
        cell.Paragraphs.Clear();
        var paragraph = new Paragraph("کلیدهای تدبر قرآن و پیروزی در زندگی");
        paragraph.Runs[0].Formatting = paragraph.Runs[0].Formatting with { FontSizePt = 15 };
        cell.Paragraphs.Add(paragraph);
        table.ColumnWidthsPt.Add(300);

        var plan = DocumentViewLayoutPlanner.BuildTablePaginationPlan(table, new PageSettings());

        plan.Rows.Single().EstimatedHeightDip.Should().Be(26, "one 18 DIP line plus the 8 DIP gutter");
    }

    [Fact]
    public void LeadingContentEstimate_UsesTheStyleCascadeLineHeightAndSpacingOfTheHeadingBeforeTheTable()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Styles["HeadingExact"] = new DocumentStyle
        {
            Id = "HeadingExact",
            Name = "Heading Exact",
            Paragraph = ParagraphFormatting.Default with
            {
                LineRule = LineSpacingRule.Exact,
                LineHeightPt = 22.5,
                LineSpacingIsSet = true,
            },
        };
        var heading = new Paragraph("Contents")
        {
            StyleId = "HeadingExact",
            Formatting = ParagraphFormatting.Default with { SpaceAfterPt = 6, SpaceAfterIsSet = true },
        };
        heading.Runs.Insert(0, new Run(string.Empty) { IsPageBreak = true });
        doc.Blocks.Add(heading);
        doc.Blocks.Add(Table.Create(1, 1));

        var estimate = DocumentViewLayoutPlanner.EstimateLeadingContentHeightDip(doc, 1);

        // 22.5pt exact line (from the style) + 6pt after (direct) = 28.5pt = 38 DIP, not an 11pt natural line.
        estimate.Should().BeApproximately(28.5 * 96.0 / 72.0, 0.01);
    }

    [Fact]
    public void ExpandedCharacterSpacing_NarrowsTheCharactersPerLineOfAnArabicCell()
    {
        static double RowHeight(double spacingPt)
        {
            var table = Table.Create(1, 1);
            var cell = table.Rows[0].Cells[0];
            cell.WidthPt = CellWidthPt;
            cell.Paragraphs.Clear();
            var paragraph = new Paragraph(new string('ا', 110))
            {
                Formatting = ExactTocFormatting(),
            };
            paragraph.Runs[0].Formatting = paragraph.Runs[0].Formatting with
            {
                FontSizePt = 8.5,
                CharacterSpacingPt = spacingPt,
            };
            cell.Paragraphs.Add(paragraph);
            table.ColumnWidthsPt.Add(CellWidthPt);
            return DocumentViewLayoutPlanner.BuildTablePaginationPlan(table, new PageSettings()).Rows.Single().EstimatedHeightDip;
        }

        var plain = RowHeight(0);
        var spaced = RowHeight(3);

        plain.Should().BeApproximately(23 * 96.0 / 72.0, 0.02, "110 narrow Arabic letters fit one line unspaced");
        spaced.Should().BeApproximately((2 * 17 + 6) * 96.0 / 72.0, 0.02, "3pt spacing per letter wraps the same text onto a second line");
    }

    [StaFact]
    public void SplitTableAtFirstOverflowRow_MovesTheRowsBelowThePageEdgeIntoAContinuationTable()
    {
        var flow = new System.Windows.Documents.FlowDocument
        {
            PageWidth = 400,
            PageHeight = 300,
            PagePadding = new System.Windows.Thickness(20),
        };
        var table = new System.Windows.Documents.Table();
        table.Columns.Add(new System.Windows.Documents.TableColumn { Width = new System.Windows.GridLength(300) });
        var group = new System.Windows.Documents.TableRowGroup();
        table.RowGroups.Add(group);
        for (var index = 0; index < 12; index++)
        {
            var row = new System.Windows.Documents.TableRow();
            row.Cells.Add(new System.Windows.Documents.TableCell(new WpfParagraph(new System.Windows.Documents.Run("row " + index))
            {
                Margin = new System.Windows.Thickness(0, 0, 0, 40),
            }));
            group.Rows.Add(row);
        }

        flow.Blocks.Add(table);
        var paginator = (System.Windows.Documents.DynamicDocumentPaginator)((System.Windows.Documents.IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new System.Windows.Size(400, 300);
        paginator.ComputePageCount();
        paginator.PageCount.Should().BeGreaterThan(1, "the table is taller than one page");

        var remainder = PaginationEngine.SplitTableAtFirstOverflowRow(paginator, table);

        remainder.Should().NotBeNull();
        var kept = table.RowGroups.SelectMany(g => g.Rows).Count();
        var moved = remainder!.RowGroups.SelectMany(g => g.Rows).Count();
        kept.Should().BeGreaterThan(0);
        moved.Should().BeGreaterThan(0);
        (kept + moved).Should().Be(12);
        remainder.Columns.Should().HaveCount(1);
    }
}
