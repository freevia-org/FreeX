using System;
using System.Windows;
using System.Windows.Documents;
using FreeW.App.Host;
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
}
