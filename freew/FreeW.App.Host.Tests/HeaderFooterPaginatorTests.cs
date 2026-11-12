using System;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for <see cref="HeaderFooterPaginator"/>'s header/footer overlay. Runs on STA
/// (<c>[StaFact]</c>) because it builds a real WPF <see cref="FlowDocument"/> paginator.
/// </summary>
public sealed class HeaderFooterPaginatorTests
{
    [StaTheory]
    [InlineData(PageBorderZOrder.Front, 0)]
    [InlineData(PageBorderZOrder.Behind, 1)]
    public void PageBorderZOrder_PlacesBodyOnExpectedSideOfBorder(
        PageBorderZOrder zOrder,
        int expectedBodyIndex)
    {
        var model = TextDocument.CreateEmpty();
        model.Page.PageBorder = new PageBorder("#24536B", 1.5) { ZOrder = zOrder };
        var inner = new SinglePagePaginator(new Size(320, 480));
        var paginator = new HeaderFooterPaginator(inner, model, model.Page);

        var page = paginator.GetPage(0);

        var container = Assert.IsType<ContainerVisual>(page.Visual);
        var children = container.Children.Cast<Visual>().ToList();
        Assert.Equal(2, children.Count);
        Assert.Same(inner.BodyVisual, children[expectedBodyIndex]);
    }

    /// <summary>
    /// When the page margins meet or exceed the page width, the header/footer content width is &lt;= 0.
    /// The overlay used to set <c>FormattedText.MaxTextWidth = PositiveInfinity</c> in that case, which
    /// WPF's text formatter rejects with "paragraphWidth ('∞')" — crashing the whole print/preview
    /// paginator. The overlay must instead skip drawing, so pagination still succeeds.
    /// </summary>
    [StaFact]
    public void NonPositiveContentWidth_DoesNotThrow()
    {
        var model = TextDocument.CreateEmpty();
        model.Header = new HeaderFooter("a simple header");
        model.Footer = new HeaderFooter("a simple footer");
        // Margins (70 + 70 = 140 pt) exceed the page width (100 pt) -> content width <= 0.
        model.Page.WidthPt = 100;
        model.Page.MarginLeftPt = 70;
        model.Page.MarginRightPt = 70;

        var flow = new FlowDocument();
        flow.Blocks.Add(new System.Windows.Documents.Paragraph(
            new System.Windows.Documents.Run("body text")));
        var inner = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(model.Page);
        inner.PageSize = new Size(pageWidth, pageHeight);

        var paginator = new HeaderFooterPaginator(inner, model, model.Page, lineHeightDip: 16);

        var ex = Record.Exception(() =>
        {
            paginator.ComputePageCount();
            _ = paginator.GetPage(0);
        });

        Assert.Null(ex);
        Assert.True(paginator.PageCount >= 1);
    }

    /// <summary>A normal page width still renders header/footer overlays without throwing.</summary>
    [StaFact]
    public void NormalContentWidth_Paginates()
    {
        var model = TextDocument.CreateEmpty();
        model.Header = new HeaderFooter("header");
        model.Footer = new HeaderFooter("footer");

        var flow = new FlowDocument();
        flow.Blocks.Add(new System.Windows.Documents.Paragraph(
            new System.Windows.Documents.Run("body text")));
        var inner = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(model.Page);
        inner.PageSize = new Size(pageWidth, pageHeight);

        var paginator = new HeaderFooterPaginator(inner, model, model.Page, lineHeightDip: 16);

        var ex = Record.Exception(() =>
        {
            paginator.ComputePageCount();
            _ = paginator.GetPage(0);
        });

        Assert.Null(ex);
    }

    [StaFact]
    public void SinglePageWithFootnote_DrawsNoteBodyAtFoot()
    {
        var model = TextDocument.CreateEmpty();
        var footnote = new Footnote(1);
        footnote.Content.Add(new Paragraph("the footnote body"));
        model.Footnotes[1] = footnote;

        var flow = new FlowDocument();
        flow.Blocks.Add(new System.Windows.Documents.Paragraph(
            new System.Windows.Documents.Run("body with a note")));
        var inner = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(model.Page);
        inner.PageSize = new Size(pageWidth, pageHeight);

        var paginator = new HeaderFooterPaginator(inner, model, model.Page, lineHeightDip: 16);
        paginator.ComputePageCount();
        var page = paginator.GetPage(0);

        // With a footnote on a single page the paginator wraps the base page and composites the note
        // visual, rather than returning the bare base page (the no-overlay early-return).
        var container = Assert.IsType<System.Windows.Media.ContainerVisual>(page.Visual);
        Assert.True(container.Children.Count >= 2);
    }

    [StaFact]
    public void PrintLayout_MultiPageFootnoteUsesAssignedPageAndReservesBodySpace()
    {
        var model = TextDocument.CreateEmpty();
        var footnote = new Footnote(1);
        footnote.Content.Add(new Paragraph("the footnote body"));
        model.Footnotes[1] = footnote;

        var first = new Paragraph();
        first.Runs.Add(new Run("body with a note "));
        first.Runs.Add(Run.FootnoteReference(1));
        model.Blocks.Add(first);
        for (var i = 0; i < 90; i++)
            model.Blocks.Add(new Paragraph("filler paragraph to force a second printed page"));

        var view = new DocumentView();
        view.LoadModel(model);
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();

        Assert.True(paginator.PageCount >= 2);
        var page = paginator.GetPage(0);
        var container = Assert.IsType<System.Windows.Media.ContainerVisual>(page.Visual);
        Assert.True(container.Children.Count >= 2);
    }

    [StaFact]
    public void PrintLayout_LongFootnoteUsesBoundedCanonicalContinuationFlow()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Footnotes[1] = new Footnote(
            1,
            "Footnote 1 overflow authority: "
            + string.Join(" ", Enumerable.Range(1, 700).Select(index => $"continuation{index}")));
        model.Footnotes[2] = new Footnote(2, "Footnote 2 remains attached to its later reference.");

        model.Blocks.Add(new FreeW.Core.Model.Paragraph("Footnotes Test"));
        model.Blocks.Add(new FreeW.Core.Model.Paragraph(
            "This tests whether footnote content appears at the foot of each page."));
        var firstReference = new FreeW.Core.Model.Paragraph();
        firstReference.Runs.Add(new FreeW.Core.Model.Run("This sentence has a footnote reference"));
        firstReference.Runs.Add(FreeW.Core.Model.Run.FootnoteReference(1));
        model.Blocks.Add(firstReference);
        for (var index = 1; index <= 22; index++)
            model.Blocks.Add(new FreeW.Core.Model.Paragraph(
                $"Filler paragraph {index}: Lorem ipsum dolor sit amet consectetur adipiscing."));

        var secondReference = new FreeW.Core.Model.Paragraph();
        secondReference.Runs.Add(new FreeW.Core.Model.Run("This sentence has a second footnote reference"));
        secondReference.Runs.Add(FreeW.Core.Model.Run.FootnoteReference(2));
        model.Blocks.Add(secondReference);
        for (var index = 1; index <= 20; index++)
            model.Blocks.Add(new FreeW.Core.Model.Paragraph(
                $"More filler {index}: Additional content to ensure footnote reference is on page 2."));

        var view = new DocumentView();
        view.LoadModel(model);
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();

        // The point of this fixture is that a 700-word footnote flows through a BOUNDED continuation
        // rather than growing the document without limit, so assert the bound first -- that part does
        // not move with layout metrics. The exact total does: it fell from 6 to 5 once an omitted
        // widow-control token stopped making every paragraph atomic, which lets the filler split and
        // pack the way Word packs it.
        Assert.InRange(paginator.PageCount, 2, 12);
        Assert.Equal(5, paginator.PageCount);
        for (var pageIndex = 0; pageIndex < 3; pageIndex++)
        {
            var page = paginator.GetPage(pageIndex);
            var container = Assert.IsType<ContainerVisual>(page.Visual);
            Assert.True(container.Children.Count >= 2, $"page {pageIndex + 1} should include its note overlay");
        }
    }

    [StaFact]
    public void CanonicalPageStartOffsets_FollowWpfPaginatorPagePositions()
    {
        var flow = new FlowDocument { PagePadding = new Thickness(48) };
        for (var i = 0; i < 180; i++)
            flow.Blocks.Add(new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run($"canonical paragraph {i} with enough text to wrap across the printable frame.")));

        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new Size(360, 480);

        var starts = PaginationEngine.ComputeCanonicalPageStartOffsets(flow, paginator);

        Assert.True(starts.Count >= 2);
        Assert.Equal(0, starts[0]);
        Assert.All(starts.Zip(starts.Skip(1)), pair => Assert.True(pair.First < pair.Second));
    }

    [StaFact]
    public void FootnotePageOwnership_UsesMarkersFromTheRenderedPaginator()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Footnotes[1] = new Footnote(1, "first note");
        model.Footnotes[2] = new Footnote(2, "second note");

        var first = new FreeW.Core.Model.Paragraph();
        first.Runs.Add(new FreeW.Core.Model.Run("first page reference"));
        first.Runs.Add(FreeW.Core.Model.Run.FootnoteReference(1));
        model.Blocks.Add(first);
        for (var i = 0; i < 180; i++)
            model.Blocks.Add(new FreeW.Core.Model.Paragraph("filler paragraph forcing the second footnote onto a later page"));
        var second = new FreeW.Core.Model.Paragraph();
        second.Runs.Add(new FreeW.Core.Model.Run("second page reference"));
        second.Runs.Add(FreeW.Core.Model.Run.FootnoteReference(2));
        model.Blocks.Add(second);

        var view = new DocumentView();
        view.LoadModel(model);
        var flow = view.Document;
        flow.PagePadding = new Thickness(48);
        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new Size(360, 480);

        var ownership = PaginationEngine.ComputeFootnotePageOwnership(flow, paginator);

        ownership[0].Should().Contain(1);
        ownership.Where(pair => pair.Key > 0).SelectMany(pair => pair.Value).Should().Contain(2);
    }

    [StaFact]
    public void MultiPageEndnotes_AppendAPhysicalPageWhenTheFinalBodyPageIsFull()
    {
        var model = DocxReader.Read(RepositoryFile(
            "freew-fidelity-corpus", "files", "review", "endnotes.docx"));
        var view = new DocumentView();
        view.LoadModel(model);
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();

        Assert.Equal(3, paginator.PageCount);

        var lastPage = paginator.GetPage(paginator.PageCount - 1);
        var container = Assert.IsType<System.Windows.Media.ContainerVisual>(lastPage.Visual);
        Assert.True(container.Children.Count >= 2,
            "the dedicated physical page should contain its page surface plus the endnote overlay");
    }

    [StaFact]
    public void FittingEndnotes_RemainOnTheFinalBodyPage()
    {
        var model = TextDocument.CreateEmpty();
        model.Endnotes[1] = new Endnote(1, "endnote body");

        var flow = new FlowDocument();
        flow.Blocks.Add(new System.Windows.Documents.Paragraph(
            new System.Windows.Documents.Run("short body")));

        var inner = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(model.Page);
        inner.PageSize = new Size(pageWidth, pageHeight);
        inner.ComputePageCount();
        var bodyPageCount = inner.PageCount;

        var paginator = new HeaderFooterPaginator(inner, model, model.Page, lineHeightDip: 16);
        paginator.ComputePageCount();

        Assert.Equal(bodyPageCount, paginator.PageCount);
        var page = paginator.GetPage(paginator.PageCount - 1);
        var container = Assert.IsType<System.Windows.Media.ContainerVisual>(page.Visual);
        Assert.True(container.Children.Count >= 2,
            "a fitting endnote should remain overlaid after the final body content");
    }

    /// <summary>
    /// The print overlay used to flatten a header to one left-aligned 11pt line. Word lays out every
    /// header paragraph with its own alignment and tab stops: a centred paragraph, a paragraph tabbed
    /// to a centre stop and a right-aligned paragraph are three separate lines at three positions.
    /// </summary>
    [StaFact]
    public void Header_LaysOutEachParagraphWithItsAlignmentAndTabStops()
    {
        var model = TextDocument.CreateEmpty();
        var (contentWidthDip, _) = PageLayout.ContentAreaDip(model.Page);
        var contentWidthPt = contentWidthDip * 72.0 / 96.0;
        var header = new HeaderFooter();
        header.Paragraphs.Add(new Paragraph("HHHHHH")
        {
            Formatting = new ParagraphFormatting { Alignment = FreeW.Core.Model.TextAlignment.Center }
        });
        header.Paragraphs.Add(new Paragraph("\tHHHHHH")
        {
            Formatting = new ParagraphFormatting
            {
                TabStops = [new TabStop(contentWidthPt / 2, TabStopAlignment.Center)]
            }
        });
        header.Paragraphs.Add(new Paragraph("HHHHHH")
        {
            Formatting = new ParagraphFormatting { Alignment = FreeW.Core.Model.TextAlignment.Right }
        });
        model.Header = header;

        var lines = RenderHeaderInkLines(model);

        Assert.Equal(3, lines.Count);
        var (marginLeft, _, marginRight, _) = PageLayout.MarginsDip(model.Page);
        var (pageWidth, _) = PageLayout.PageSizeDip(model.Page);
        var centre = marginLeft + contentWidthDip / 2;
        Assert.InRange((lines[0].Left + lines[0].Right) / 2, centre - 4, centre + 4);
        Assert.InRange((lines[1].Left + lines[1].Right) / 2, centre - 4, centre + 4);
        Assert.InRange(lines[2].Right, pageWidth - marginRight - 4, pageWidth - marginRight + 1);
        // The header starts at "Header from edge" (0.5" by default), not at an arbitrary strip offset.
        Assert.InRange(lines[0].Top, 48, 48 + 12);
    }

    /// <summary>
    /// Word's classic page-number gallery writes the PAGE field as a framed paragraph
    /// (w:framePr w:xAlign="right"): it floats at the right of the text area beside the first header
    /// line instead of taking a line of its own at the left.
    /// </summary>
    [StaFact]
    public void Header_FramedPageNumberFloatsAtItsAlignedSideBesideTheText()
    {
        var model = TextDocument.CreateEmpty();
        var header = new HeaderFooter();
        var pageNumber = new Paragraph { FrameHorizontalAlignment = FreeW.Core.Model.TextAlignment.Right };
        pageNumber.Runs.Add(Run.PageNumberField());
        header.Paragraphs.Add(pageNumber);
        header.Paragraphs.Add(new Paragraph("HHHHHH"));
        model.Header = header;

        var lines = RenderHeaderInkLines(model);

        var line = Assert.Single(lines);
        var (marginLeft, _, marginRight, _) = PageLayout.MarginsDip(model.Page);
        var (pageWidth, _) = PageLayout.PageSizeDip(model.Page);
        Assert.InRange(line.Left, marginLeft - 1, marginLeft + 4);
        Assert.InRange(line.Right, pageWidth - marginRight - 4, pageWidth - marginRight + 1);
    }

    /// <summary>
    /// A header taller than the top margin pushes the body down (Word starts the body below the
    /// header's last line); a header that fits leaves the margin alone.
    /// </summary>
    [StaFact]
    public void TallHeader_PushesTheBodyBelowItsLastLine()
    {
        var model = TextDocument.CreateEmpty();
        model.Header = new HeaderFooter("one line");
        Assert.Equal((0d, 0d), PrintLayout.ResolveHeaderFooterBodyInsetsDip(model, model.Page));

        var tall = new HeaderFooter();
        for (var i = 0; i < 8; i++)
            tall.Paragraphs.Add(new Paragraph($"header line {i}"));
        model.Header = tall;

        var (top, bottom) = PrintLayout.ResolveHeaderFooterBodyInsetsDip(model, model.Page);

        Assert.True(top > 40, $"eight header lines overflow a 1in margin by several lines, got {top}");
        Assert.Equal(0d, bottom);
    }

    /// <summary>
    /// A single-section document still selects its header per page: with "Different first page" on,
    /// page 1 prints the first-page header (here none), never the default running header.
    /// </summary>
    [StaFact]
    public void DifferentFirstPage_DoesNotPrintTheRunningHeaderOnPageOne()
    {
        var model = TextDocument.CreateEmpty();
        model.Header = new HeaderFooter("HHHHHH");
        Assert.Single(RenderHeaderInkLines(model));

        model.Page.DifferentFirstPage = true;
        Assert.Empty(RenderHeaderInkLines(model));

        model.FinalSectionHeadersFooters.FirstHeader = new HeaderFooter("HHHHHH");
        model.FinalSectionHeadersFooters.FirstHeader.Paragraphs.Add(new Paragraph("HHHHHH"));
        Assert.Equal(2, RenderHeaderInkLines(model).Count);
    }

    private readonly record struct InkLine(double Top, double Bottom, double Left, double Right);

    /// <summary>
    /// Renders page 1 through <see cref="HeaderFooterPaginator"/> over an empty body and returns the
    /// rows of ink found above the body area, top to bottom, with each row's horizontal ink extent.
    /// </summary>
    private static List<InkLine> RenderHeaderInkLines(TextDocument model)
    {
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(model.Page);
        var inner = new SinglePagePaginator(new Size(pageWidth, pageHeight));
        var paginator = new HeaderFooterPaginator(inner, model, model.Page);
        var page = paginator.GetPage(0);

        var width = (int)Math.Ceiling(pageWidth);
        var height = (int)Math.Ceiling(pageHeight / 2);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(page.Visual);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        var lines = new List<InkLine>();
        int? start = null;
        int left = int.MaxValue, right = int.MinValue;
        for (var y = 0; y < height; y++)
        {
            var rowHasInk = false;
            for (var x = 0; x < width; x++)
            {
                var offset = y * stride + x * 4;
                if (pixels[offset] < 128 && pixels[offset + 1] < 128 && pixels[offset + 2] < 128)
                {
                    rowHasInk = true;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }
            }

            if (rowHasInk)
            {
                start ??= y;
            }
            else if (start is { } top)
            {
                lines.Add(new InkLine(top, y, left, right + 1));
                start = null;
                left = int.MaxValue;
                right = int.MinValue;
            }
        }

        return lines;
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);

    private sealed class SinglePagePaginator : DocumentPaginator
    {
        public SinglePagePaginator(Size pageSize)
        {
            PageSize = pageSize;
            using var drawing = BodyVisual.RenderOpen();
            drawing.DrawRectangle(Brushes.White, null, new Rect(new Point(), pageSize));
        }

        public DrawingVisual BodyVisual { get; } = new();
        public override bool IsPageCountValid => true;
        public override int PageCount => 1;
        public override Size PageSize { get; set; }
        public override IDocumentPaginatorSource Source => null!;

        public override DocumentPage GetPage(int pageNumber) =>
            pageNumber == 0
                ? new DocumentPage(
                    BodyVisual,
                    PageSize,
                    new Rect(new Point(), PageSize),
                    new Rect(new Point(), PageSize))
                : DocumentPage.Missing;
    }
}
