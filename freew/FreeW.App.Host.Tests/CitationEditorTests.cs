using System.Linq;
using FreeW.App.Host.Editing;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class CitationEditorTests
{
    [StaFact]
    public void InsertCitation_UsesSharedFamilyNameDisplay()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph("See "));
        var view = new DocumentView();
        view.LoadModel(model);

        view.InsertCitation(new Source { Author = "Jane Q. Doe", Year = "2020" });
        view.CommitToModel();

        view.Model.Blocks.OfType<Paragraph>()
            .Single()
            .PlainText
            .Should().Contain("(Doe, 2020)");
    }
}
