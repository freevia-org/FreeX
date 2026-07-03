using System.Linq;
using FreeW.App.Host.Editing;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class NumericCitationEditorTests
{
    [StaFact]
    public void InsertCitation_IeeeUsesSharedSourceOrderNumber()
    {
        var first = new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" };
        var second = new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" };
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph("See "));
        model.BibliographyStyle = CitationStyle.Ieee;
        model.Sources.Add(first);
        model.Sources.Add(second);

        var view = new DocumentView();
        view.LoadModel(model);
        view.InsertCitation(second);
        view.CommitToModel();

        var text = view.Model.Blocks.OfType<Paragraph>().Single().PlainText;
        text.Should().Contain("[2]");
        text.Should().NotContain("[Turing]");
        view.Model.Blocks.OfType<Paragraph>().Single().Runs
            .Select(run => run.ComplexField?.Keyword)
            .Should().Contain("CITATION");
    }

    [StaFact]
    public void UpdateFields_IeeeCitationFieldRenumbersAfterSourceOrderChanges()
    {
        var first = new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" };
        var second = new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" };
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph { Runs = { Run.ComplexFieldRun(" CITATION Tur1936 ", "[2]") } });
        model.BibliographyStyle = CitationStyle.Ieee;
        model.Sources.Add(second);
        model.Sources.Add(first);

        var view = new DocumentView();
        view.LoadModel(model);

        view.UpdateFields();
        view.CommitToModel();

        view.Model.Blocks.OfType<Paragraph>().Single().PlainText.Should().Be("[1]");
    }
}
