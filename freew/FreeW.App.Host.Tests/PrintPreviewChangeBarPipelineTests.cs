using System.Linq;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// freew-change-bars F4: the Simple Markup change bar (<c>ChangeBarAdorner</c> in
/// FreeW.App.Host/Editing/DocumentView.cs) is the only visual cue that a paragraph carries a tracked
/// change while Review &gt; Display for Review is Simple Markup, but it used to be added only to the
/// live on-screen editor's own <see cref="AdornerLayer"/> (<c>DocumentView.SyncChangeBarAdorner</c>).
/// <see cref="PrintLayout.BuildPaginator"/> is the single hub behind Print, Print Preview, and PDF/XPS
/// export (see <see cref="PrintPreviewWindow"/>, <c>MainWindow.Print</c>, <c>PdfExport</c>,
/// <c>XpsExport</c>), and its <see cref="HeaderFooterPaginator.GetPage"/> composited header, footer,
/// watermark, border, line numbers, notes, and balloons onto every page but never a change bar -- a
/// tracked-change document printed in Simple Markup came out looking identical to an unmodified one.
/// These tests exercise the real production entry point (<see cref="PrintLayout.BuildPaginator"/> plus
/// the returned paginator's <see cref="System.Windows.Documents.DocumentPaginator.GetPage"/>) and walk
/// the actual rendered <see cref="System.Windows.Media.Visual"/> tree for a change-bar stroke, rather
/// than asserting against a helper that supplies the bar directly.
/// </summary>
public sealed class PrintPreviewChangeBarPipelineTests
{
    // Matches HeaderFooterPaginator.BuildChangeBars / ChangeBarAdorner.CreateBarPen exactly.
    private static readonly Color ChangeBarColor = Color.FromRgb(0x60, 0x60, 0xC0);
    private const double ChangeBarWidth = 3.0;

    [StaFact]
    public void BuildPaginator_SimpleMarkupWithTrackedInsertion_DrawsChangeBarOnPrintedPage()
    {
        var view = MakeEditorWithInsertedRun();
        view.ApplyDisplayForReview(ReviewDisplayMode.SimpleMarkup);

        var paginator = PrintLayout.BuildPaginator(view);
        var page = paginator.GetPage(0);

        Assert.True(
            ContainsChangeBarStroke(page.Visual),
            "Print/Print Preview/PDF/XPS must draw the Simple Markup change bar for a paragraph carrying a tracked insertion.");
    }

    [StaFact]
    public void BuildPaginator_SimpleMarkupWithNoRevisions_DrawsNoChangeBar()
    {
        // Sibling/no-regression: same Simple Markup mode, but nothing in the document is a tracked
        // change -- today's overwhelmingly common document. No bar should appear.
        var view = MakeEditorWithPlainText();
        view.ApplyDisplayForReview(ReviewDisplayMode.SimpleMarkup);

        var paginator = PrintLayout.BuildPaginator(view);
        var page = paginator.GetPage(0);

        Assert.False(
            ContainsChangeBarStroke(page.Visual),
            "A document with no tracked changes must not grow a change bar just because Simple Markup is active.");
    }

    [StaFact]
    public void BuildPaginator_AllMarkupWithTrackedInsertion_DrawsNoChangeBar()
    {
        // Sibling/no-regression: All Markup mode shows the tracked change inline (colour/strikethrough)
        // instead of a margin bar -- ShouldShowSimpleMarkupChangeBar is false there, on screen and here.
        var view = MakeEditorWithInsertedRun();
        view.ApplyDisplayForReview(ReviewDisplayMode.AllMarkup);

        var paginator = PrintLayout.BuildPaginator(view);
        var page = paginator.GetPage(0);

        Assert.False(
            ContainsChangeBarStroke(page.Visual),
            "All Markup already shows the change inline; it must not also draw the Simple Markup margin bar.");
    }

    [StaFact]
    public void BuildPaginator_SimpleMarkupWithFormatRevisionOnly_DrawsChangeBar()
    {
        // A tracked *formatting* change (w:rPrChange) carries no Revision insert/delete mark, only
        // FormatRevision -- ChangeBarAdorner.InlineHasRevision treats that as a change too.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("reformatted")
        {
            FormatRevision = new FormatRevision(RunFormatting.Default, "A", null)
        });
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.ApplyDisplayForReview(ReviewDisplayMode.SimpleMarkup);
        view.LoadModel(doc);

        var paginator = PrintLayout.BuildPaginator(view);
        var page = paginator.GetPage(0);

        Assert.True(
            ContainsChangeBarStroke(page.Visual),
            "A tracked formatting-only change must also draw the Simple Markup change bar.");
    }

    [StaFact]
    public void BuildPaginator_NoMarkupWithTrackedInsertion_DrawsNoChangeBar()
    {
        // Sibling/no-regression: No Markup mode hides deleted text entirely and shows insertions as
        // plain accepted text -- ShouldShowSimpleMarkupChangeBar is false there too.
        var view = MakeEditorWithInsertedRun();
        view.ApplyDisplayForReview(ReviewDisplayMode.NoMarkup);

        var paginator = PrintLayout.BuildPaginator(view);
        var page = paginator.GetPage(0);

        Assert.False(
            ContainsChangeBarStroke(page.Visual),
            "No Markup must not draw the Simple Markup margin bar either.");
    }

    private static DocumentView MakeEditorWithInsertedRun()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("base "));
        para.Runs.Add(new Run("added") { Revision = RevisionKind.Inserted, RevisionAuthor = "A" });
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static DocumentView MakeEditorWithPlainText()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("nothing has changed here"));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    /// <summary>Walks a rendered page's visual tree for a stroked line matching the change-bar pen.</summary>
    private static bool ContainsChangeBarStroke(System.Windows.Media.Visual? visual)
    {
        if (visual is null)
            return false;
        if (visual is DrawingVisual dv && VisualTreeHelper.GetDrawing(dv) is { } drawing && ContainsChangeBarStroke(drawing))
            return true;

        var count = VisualTreeHelper.GetChildrenCount(visual);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(visual, i) is System.Windows.Media.Visual child
                && ContainsChangeBarStroke(child))
                return true;
        }
        return false;
    }

    private static bool ContainsChangeBarStroke(Drawing drawing)
    {
        switch (drawing)
        {
            case System.Windows.Media.DrawingGroup group:
                return group.Children.Any(ContainsChangeBarStroke);
            case GeometryDrawing gd:
                return gd.Pen is { Brush: SolidColorBrush { Color: var c } } pen
                    && c == ChangeBarColor
                    && pen.Thickness == ChangeBarWidth;
            default:
                return false;
        }
    }
}
