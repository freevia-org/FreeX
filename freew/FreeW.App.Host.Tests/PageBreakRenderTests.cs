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
}
