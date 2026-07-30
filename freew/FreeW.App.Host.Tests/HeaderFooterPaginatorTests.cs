using System;
using System.Windows;
using System.Windows.Documents;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for <see cref="HeaderFooterPaginator"/>'s header/footer overlay. Runs on STA
/// (<c>[StaFact]</c>) because it builds a real WPF <see cref="FlowDocument"/> paginator.
/// </summary>
public sealed class HeaderFooterPaginatorTests
{
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
    public void MultiPageEndnotes_DrawOnTheFinalPage()
    {
        var model = TextDocument.CreateEmpty();
        model.Endnotes[1] = new Endnote(1, "endnote body");

        var flow = new FlowDocument();
        for (var i = 0; i < 120; i++)
            flow.Blocks.Add(new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run($"body paragraph {i}")));

        var inner = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(model.Page);
        inner.PageSize = new Size(pageWidth, pageHeight);

        var paginator = new HeaderFooterPaginator(inner, model, model.Page, lineHeightDip: 16);
        paginator.ComputePageCount();
        Assert.True(paginator.PageCount >= 2);

        var lastPage = paginator.GetPage(paginator.PageCount - 1);
        var container = Assert.IsType<System.Windows.Media.ContainerVisual>(lastPage.Visual);
        Assert.True(container.Children.Count >= 2,
            "the final page should contain the base page plus the endnote overlay");
    }
}
