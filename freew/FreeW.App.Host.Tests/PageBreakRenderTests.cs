using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Verifies the editor maps a manual page-break run to WPF <c>Paragraph.BreakPageBefore</c> (so the
/// paginator starts a new page) and preserves the break across an edit/commit cycle. Runs on STA.
/// </summary>
public sealed class PageBreakRenderTests
{
    private static TextDocument BreakDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(Run.PageBreak());
        p.Runs.Add(new Run("on a new page"));
        doc.Blocks.Add(p);
        return doc;
    }

    private static TextDocument ColumnBreakDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(Run.ColumnBreak());
        p.Runs.Add(new Run("in the next column"));
        doc.Blocks.Add(p);
        return doc;
    }

    private static TextDocument MixedPageBreakDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Before"));
        paragraph.Runs.Add(Run.PageBreak());
        paragraph.Runs.Add(new Run("Middle"));
        paragraph.Runs.Add(Run.PageBreak());
        paragraph.Runs.Add(new Run("After"));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [StaFact]
    public void PageBreakParagraph_SetsBreakPageBefore()
    {
        var view = new DocumentView();
        view.LoadModel(BreakDoc());

        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        Assert.True(paragraph.BreakPageBefore);
    }

    [StaFact]
    public void PageBreakRun_SurvivesCommit()
    {
        var view = new DocumentView();
        view.LoadModel(BreakDoc());
        view.CommitToModel();

        var paragraph = view.Model.Blocks.OfType<Paragraph>().First();
        Assert.Contains(paragraph.Runs, r => r.IsPageBreak);
    }

    [StaFact]
    public void PaginatedOutput_SplitsMixedPageBreakRunsWithoutSplittingTheEditableModel()
    {
        var view = new DocumentView();
        view.LoadModel(MixedPageBreakDoc());

        Assert.Single(view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>());

        var paginated = PrintLayout.BuildPaginatedDocument(view);
        var group = Assert.IsType<System.Windows.Documents.Section>(Assert.Single(paginated.Blocks));
        var fragments = group.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();
        Assert.Equal(3, fragments.Count);
        Assert.False(fragments[0].BreakPageBefore);
        Assert.True(fragments[1].BreakPageBefore);
        Assert.True(fragments[2].BreakPageBefore);
        Assert.Equal("Before", ParagraphText(fragments[0]));
        Assert.Equal("Middle", ParagraphText(fragments[1]));
        Assert.Equal("After", ParagraphText(fragments[2]));

        var paginator = ((System.Windows.Documents.IDocumentPaginatorSource)paginated).DocumentPaginator;
        paginator.ComputePageCount();
        Assert.Equal(3, paginator.PageCount);

        view.CommitToModel();
        var paragraph = Assert.IsType<Paragraph>(Assert.Single(view.Model.Blocks));
        Assert.Equal(
            ["Before", "break", "Middle", "break", "After"],
            paragraph.Runs.Select(run => run.IsPageBreak ? "break" : run.Text));
    }

    [StaFact]
    public void PageBreakBefore_CanSuppressEditorMarkerWithoutChangingPagination()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var source = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        };
        source.Runs.Add(new Run("next page"));
        doc.Blocks.Add(source);

        var view = new DocumentView { RenderPageBreakMarkers = false };
        view.LoadModel(doc);

        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        Assert.True(paragraph.BreakPageBefore);
        Assert.Equal(0, paragraph.BorderThickness.Top);
    }

    [StaFact]
    public void ColumnBreakParagraph_UsesNativeColumnBreakWithoutForcingPageBreak()
    {
        var view = new DocumentView();
        view.LoadModel(ColumnBreakDoc());

        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        Assert.True(paragraph.BreakColumnBefore);
        Assert.False(paragraph.BreakPageBefore);
    }

    [StaFact]
    public void ColumnBreakRun_SurvivesCommit()
    {
        var view = new DocumentView();
        view.LoadModel(ColumnBreakDoc());
        view.CommitToModel();

        var paragraph = view.Model.Blocks.OfType<Paragraph>().First();
        Assert.Contains(paragraph.Runs, run => run.IsColumnBreak);
        Assert.DoesNotContain(paragraph.Runs, run => run.IsPageBreak);
    }

    private static string ParagraphText(System.Windows.Documents.Paragraph paragraph) =>
        new System.Windows.Documents.TextRange(paragraph.ContentStart, paragraph.ContentEnd)
            .Text.TrimEnd('\r', '\n');
}
