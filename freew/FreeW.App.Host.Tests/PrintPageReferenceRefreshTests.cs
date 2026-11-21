using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class PrintPageReferenceRefreshTests
{
    [StaFact]
    public void Refresh_changes_only_the_displayed_text_of_an_unlocked_pageref_run()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Target") { BookmarkName = "t" });
        var para = new Paragraph("");
        var field = Run.ComplexFieldRun(" PAGEREF t ", "40");
        para.Runs.Add(field);
        document.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(document);

        var changed = DocumentView.RefreshPrintPageReferences(view.Document.Blocks.ToList(), (_, _) => "3");

        changed.Should().ContainSingle().Which.Previous.Should().Be("40");
        changed[0].Run.Text.Should().Be("3");
        ((Paragraph)view.Model.Blocks[1]).Runs.Single(r => r.ComplexField is not null).Text.Should().Be("40");
    }
}
