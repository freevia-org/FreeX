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

    [StaFact]
    public void InsertCitation_TaggedSource_InsertsCitationComplexField()
    {
        var source = new Source { Tag = "Doe2020", Author = "Jane Q. Doe", Year = "2020" };
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph("See "));
        model.Sources.Add(source);
        var view = new DocumentView();
        view.LoadModel(model);

        view.InsertCitation(source);
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(r => r.ComplexField is not null);
        run.Text.Should().Be("(Doe, 2020)");
        run.ComplexField!.Instruction.Should().Be(" CITATION Doe2020 ");
    }

    [StaFact]
    public void InsertCitation_TaggedSourceWithQuotedFieldArgument_RenumbersOnUpdateFields()
    {
        var source = new Source
        {
            Tag = "Doe \"AI\" 2020",
            Author = "Jane Q. Doe",
            Year = "2020"
        };
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph("See "));
        model.BibliographyStyle = CitationStyle.Ieee;
        model.Sources.Add(new Source { Tag = "Other2020", Author = "Other Author", Year = "2020" });
        model.Sources.Add(source);
        var view = new DocumentView();
        view.LoadModel(model);

        view.InsertCitation(source);
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(r => r.ComplexField is not null);
        run.Text.Should().Be("[2]");
        run.ComplexField!.Instruction.Should().Be(" CITATION \"Doe \\\"AI\\\" 2020\" ");

        view.Model.Sources.Clear();
        view.Model.Sources.Add(source);
        view.LoadModel(view.Model);
        view.UpdateFields();
        view.CommitToModel();

        view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(r => r.ComplexField is not null)
            .Text.Should().Be("[1]");
    }
}
