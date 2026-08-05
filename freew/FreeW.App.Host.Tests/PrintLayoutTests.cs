using System;
using System.Linq;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for the print/preview paginator's FlowDocument clone. The editor stamps
/// non-public <c>Tag</c> payloads on paragraphs/runs/hyperlinks/cells; <see cref="PrintLayout"/>'s clone
/// goes through <c>XamlWriter.Save</c>, which used to throw "Cannot serialize a non-public type" on those
/// Tags — crashing Print and Print Preview on essentially any styled document. Runs on STA because it
/// builds the real WPF editing surface.
/// </summary>
public sealed class PrintLayoutTests
{
    [StaFact]
    public void BuildPaginator_DocumentWithTaggedParagraphs_DoesNotThrow()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        // StyleId and BookmarkName both cause DocumentView to stamp a non-public ParagraphTag.
        doc.Blocks.Add(new Paragraph("A heading") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Body text with a bookmark") { BookmarkName = "bm1" });

        var view = new DocumentView();
        view.LoadModel(doc);

        var ex = Record.Exception(() =>
        {
            var paginator = PrintLayout.BuildPaginator(view);
            paginator.ComputePageCount();
            _ = paginator.GetPage(0);
        });

        Assert.Null(ex);
    }

    [StaFact]
    public void BuildPaginator_LeavesEditorTagsIntact()
    {
        // The clone strips Tags on the live editor document during serialization; it must restore them so
        // a subsequent CommitToModel still recovers style ids, bookmarks, etc.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Styled") { StyleId = "Heading1" });

        var view = new DocumentView();
        view.LoadModel(doc);

        _ = PrintLayout.BuildPaginator(view);

        view.CommitToModel();
        var recovered = (Paragraph)view.Model.Blocks[0];
        Assert.Equal("Heading1", recovered.StyleId);
    }

    [StaFact]
    public void BuildPaginator_TwoSections_UsesEachSectionPageGeometry()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Portrait section content"));

        var portrait = new PageSettings
        {
            WidthPt = 612,
            HeightPt = 792,
            MarginLeftPt = 72,
            MarginRightPt = 72,
            MarginTopPt = 72,
            MarginBottomPt = 72,
        };
        document.Blocks.Add(new Paragraph("Section break")
        {
            SectionBreak = new Section(portrait, SectionBreakKind.NextPage)
        });

        document.Page.WidthPt = 792;
        document.Page.HeightPt = 612;
        document.Page.Landscape = true;
        document.Blocks.Add(new Paragraph("Landscape section content"));

        var view = new DocumentView();
        view.LoadModel(document);
        var paginator = PrintLayout.BuildPaginator(view);

        paginator.ComputePageCount();

        Assert.True(paginator.PageCount >= 2);
        Assert.Equal(816, paginator.GetPage(0).Size.Width, precision: 3);
        Assert.Equal(1056, paginator.GetPage(1).Size.Width, precision: 3);
        Assert.Equal(816, paginator.GetPage(1).Size.Height, precision: 3);
        Assert.Equal(1056, paginator.GetPage(0).Size.Height, precision: 3);
    }

    [StaFact]
    public void BuildPaginator_HomogeneousNextPageSection_PreservesPageBoundary()
    {
        var document = BuildHomogeneousTwoSectionDocument(SectionBreakKind.NextPage);
        var view = new DocumentView();
        view.LoadModel(document);

        var flow = PrintLayout.BuildPaginatedDocument(view);
        var paragraphs = flow.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();

        Assert.True(paragraphs[1].BreakPageBefore);
        Assert.Equal(2, paginator.PageCount);
    }

    [StaFact]
    public void BuildPaginator_HomogeneousContinuousSection_DoesNotAddPageBoundary()
    {
        var document = BuildHomogeneousTwoSectionDocument(SectionBreakKind.Continuous);
        var view = new DocumentView();
        view.LoadModel(document);

        var flow = PrintLayout.BuildPaginatedDocument(view);
        var paragraphs = flow.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();

        Assert.False(paragraphs[1].BreakPageBefore);
        Assert.Equal(1, paginator.PageCount);
    }

    [StaTheory]
    [InlineData(SectionBreakKind.EvenPage, 2, false)]
    [InlineData(SectionBreakKind.OddPage, 3, true)]
    public void BuildPaginator_HomogeneousParitySection_InsertsOnlyRequiredPhysicalBlank(
        SectionBreakKind breakKind,
        int expectedPageCount,
        bool expectsBlankPage)
    {
        var document = BuildHomogeneousTwoSectionDocument(breakKind);
        var footer = new HeaderFooter();
        var footerParagraph = new Paragraph();
        footerParagraph.Runs.Add(Run.NumPagesField());
        footer.Paragraphs.Add(footerParagraph);
        document.Footer = footer;
        var view = new DocumentView();
        view.LoadModel(document);

        var panel = PaginatedEditorPanel.Build(view, includeParityBlankPages: true);
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();

        Assert.Equal(expectedPageCount, panel.PageBoxes.Count);
        Assert.Equal(expectedPageCount, paginator.PageCount);
        for (var pageIndex = 0; pageIndex < expectedPageCount; pageIndex++)
            Assert.NotSame(System.Windows.Documents.DocumentPage.Missing, paginator.GetPage(pageIndex));
        Assert.Equal(expectsBlankPage, panel.PageBoxes.Any(box => box.IsParitySyntheticPage));
        Assert.Contains("First section", BodyText(panel.PageBoxes.First()));
        Assert.Contains("Second section", BodyText(panel.PageBoxes.Last()));
        Assert.Equal(expectedPageCount.ToString(), panel.PageBoxes.Last().PageNumberText);
        Assert.NotNull(panel.PageBoxes.Last().FooterSubEditor);
        Assert.Equal(
            expectedPageCount.ToString(),
            BodyText(panel.PageBoxes.Last().FooterSubEditor!).Trim());

        if (expectsBlankPage)
        {
            var blank = panel.PageBoxes[1];
            Assert.True(blank.IsParitySyntheticPage);
            Assert.True(blank.Body.IsReadOnly);
            Assert.True(string.IsNullOrWhiteSpace(BodyText(blank)));
            Assert.Empty(blank.FootnoteIds);
            Assert.Empty(blank.EndnoteIds);
            Assert.Null(blank.HeaderSubEditor);
            Assert.Null(blank.FooterSubEditor);
            Assert.Equal([1, 2, 3], panel.PageBoxes.Select(box => box.PageNumber));
        }
    }

    [StaFact]
    public void BuildPaginator_ListCompactionBeforeHomogeneousSection_PreservesPageBoundary()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var listFormatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet };
        document.Blocks.Add(new Paragraph("First list item") { Formatting = listFormatting });
        document.Blocks.Add(new Paragraph("Second list item") { Formatting = listFormatting });
        document.Blocks.Add(new Paragraph("Section end")
        {
            SectionBreak = new Section(document.Page.Clone(), SectionBreakKind.NextPage)
        });
        document.Blocks.Add(new Paragraph("Second section"));
        var view = new DocumentView();
        view.LoadModel(document);

        var flow = PrintLayout.BuildPaginatedDocument(view);
        var target = flow.Blocks.OfType<System.Windows.Documents.Paragraph>().Last();
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();

        Assert.True(target.BreakPageBefore);
        Assert.Equal(2, paginator.PageCount);
    }

    [StaTheory]
    [InlineData(SectionBreakKind.NextPage, true, 2)]
    [InlineData(SectionBreakKind.Continuous, false, 1)]
    public void BuildPaginator_HomogeneousSectionBeginningWithTable_PreservesBoundary(
        SectionBreakKind breakKind,
        bool expectedBreakBefore,
        int expectedPageCount)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var listFormatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet };
        document.Blocks.Add(new Paragraph("First list item") { Formatting = listFormatting });
        document.Blocks.Add(new Paragraph("Second list item") { Formatting = listFormatting });
        document.Blocks.Add(new Paragraph("Section end")
        {
            SectionBreak = new Section(document.Page.Clone(), breakKind)
        });
        document.Blocks.Add(Table.Create(1, 1));
        var view = new DocumentView();
        view.LoadModel(document);

        var flow = PrintLayout.BuildPaginatedDocument(view);
        var target = flow.Blocks.Single(block =>
            block is System.Windows.Documents.Table
            || block is System.Windows.Documents.Section section
               && section.Blocks.OfType<System.Windows.Documents.Table>().Any());
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();

        Assert.Equal(expectedBreakBefore, target.BreakPageBefore);
        Assert.Equal(expectedPageCount, paginator.PageCount);
    }

    private static TextDocument BuildHomogeneousTwoSectionDocument(SectionBreakKind breakKind)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("First section")
        {
            SectionBreak = new Section(document.Page.Clone(), breakKind)
        });
        document.Blocks.Add(new Paragraph("Second section"));
        return document;
    }

    private static string BodyText(PageBox box) =>
        new System.Windows.Documents.TextRange(
            box.Body.Document.ContentStart,
            box.Body.Document.ContentEnd).Text;

    private static string BodyText(DocumentView view) =>
        new System.Windows.Documents.TextRange(
            view.Document.ContentStart,
            view.Document.ContentEnd).Text;
}
