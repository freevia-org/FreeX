using System.Text;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// R139: Review &gt; Show Markup &gt; Balloons content (comments, tracked-change insertions/deletions)
/// used to never reach Print, Print Preview, or PDF/XPS export -- <see cref="PrintLayout.BuildPaginator"/>
/// (the single hub behind all four of those, see <see cref="PrintPreviewWindow"/>, <c>MainWindow.Print</c>,
/// <see cref="PdfExport"/>, and <c>XpsExport</c>) built its clone purely from the editor's FlowDocument
/// blocks, with nothing referencing <c>BalloonOverlay</c>/<see cref="ReviewBalloonLayoutPlanner"/> at all.
/// These tests exercise the real entry point (<see cref="PrintLayout.BuildPaginator"/> and
/// <see cref="PdfExport.RenderToBytes"/>) with <see cref="DocumentView.ShowMarkupBalloons"/> set the way
/// the live app sets it (<c>MainWindow.ToggleBalloons</c>), not a helper that supplies balloon content
/// directly.
/// </summary>
public sealed class PrintLayoutBalloonsTests
{
    private const string CommentMarker = "UniqueBalloonStripCommentMarkerXYZ";

    [StaFact]
    public void BuildPaginator_ShowMarkupBalloonsOn_WidensPageBeyondBalloonsOff()
    {
        var narrowView = MakeEditorWithComment();
        var narrowPaginator = PrintLayout.BuildPaginator(narrowView);
        narrowPaginator.ComputePageCount();
        var narrowWidth = narrowPaginator.PageSize.Width;

        var wideView = MakeEditorWithComment();
        wideView.ShowMarkupBalloons = true;
        var widePaginator = PrintLayout.BuildPaginator(wideView);
        widePaginator.ComputePageCount();
        var wideWidth = widePaginator.PageSize.Width;

        Assert.Equal(narrowWidth + PrintLayout.BalloonStripWidthDip, wideWidth, precision: 3);
    }

    [StaFact]
    public void BuildPaginator_ShowMarkupBalloonsOff_LeavesPageAtOrdinaryWidth()
    {
        // Sibling/no-regression: with balloons off (the default -- matches every document printed
        // today), the page must stay exactly the ordinary PageLayout.PageSizeDip width. If disabling
        // ShowMarkupBalloons stopped widening the page, this proves it -- the companion test above
        // proves the widening actually happens when it should.
        var view = MakeEditorWithComment();

        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();

        var (expectedWidth, _) = PageLayout.PageSizeDip(view.Model.Page);
        Assert.Equal(expectedWidth, paginator.PageSize.Width, precision: 3);
    }

    [StaFact]
    public void RenderToBytes_ShowMarkupBalloonsOnWithComment_DrawsCommentTextInPrintedPage()
    {
        // The real user path: PrintPreviewWindow/MainWindow.Print/PdfExport/XpsExport all consume
        // PrintLayout.BuildPaginator(editor) -- so proving the exported PDF bytes carry the comment
        // text proves Print, Print Preview, and PDF/XPS export all pick it up (they share this one hub).
        var view = MakeEditorWithComment();
        view.ShowMarkupBalloons = true;

        var paginator = PrintLayout.BuildPaginator(view);
        var bytes = PdfExport.RenderToBytes(paginator, "BalloonsOn");

        var pdfText = Encoding.Latin1.GetString(bytes);
        Assert.Contains(CommentMarker, pdfText);
    }

    [StaFact]
    public void RenderToBytes_ShowMarkupBalloonsOff_DoesNotDrawCommentTextAnywhereInPrintedPage()
    {
        // Sibling/no-regression: the same document, same comment, but balloons mode off (today's
        // default behaviour for every existing document) -- the comment text must not leak into the
        // printed page at all, confirming the balloon strip -- not some other unconditional code path --
        // is what puts it there.
        var view = MakeEditorWithComment();

        var paginator = PrintLayout.BuildPaginator(view);
        var bytes = PdfExport.RenderToBytes(paginator, "BalloonsOff");

        var pdfText = Encoding.Latin1.GetString(bytes);
        Assert.DoesNotContain(CommentMarker, pdfText);
    }

    [StaFact]
    public void ResolvePrintBalloonSources_ShowMarkupBalloonsOn_ReturnsTheCommentAndRevisions()
    {
        var view = MakeEditorWithComment();
        view.ShowMarkupBalloons = true;

        var sources = PrintLayout.ResolvePrintBalloonSources(view);

        Assert.Contains(sources, source => source.Kind == ReviewBalloonKind.Comment && source.Text == CommentMarker);
    }

    [StaFact]
    public void ResolvePrintBalloonSources_ShowMarkupBalloonsOff_ReturnsEmpty()
    {
        var view = MakeEditorWithComment();

        var sources = PrintLayout.ResolvePrintBalloonSources(view);

        Assert.Empty(sources);
    }

    private static DocumentView MakeEditorWithComment()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Body text ") { CommentId = 0 });
        paragraph.Runs.Add(Run.CommentReference(0));
        doc.Blocks.Add(paragraph);
        doc.Comments[0] = new Comment(0, CommentMarker, "Carol", "C");

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }
}
