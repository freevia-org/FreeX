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
    public void EmptyPageBreakBefore_UsesLayoutSpacerWithoutChangingTheModel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Before break"));
        doc.Blocks.Add(DocumentOps.CreatePageBreak());
        doc.Blocks.Add(new Paragraph("After break"));

        var view = new DocumentView();
        view.LoadModel(doc);

        var breakParagraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ElementAt(1);
        Assert.Single(breakParagraph.Inlines);
        Assert.Equal("\u00A0", new System.Windows.Documents.TextRange(
            breakParagraph.ContentStart, breakParagraph.ContentEnd).Text.TrimEnd('\r', '\n'));

        view.CommitToModel();

        var readBreak = view.Model.Blocks.OfType<Paragraph>().ElementAt(1);
        Assert.Empty(readBreak.Runs);
        Assert.True(readBreak.Formatting.PageBreakBefore);
    }
}
