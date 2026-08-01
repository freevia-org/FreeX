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

    private static string RepositoryFile(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }

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
