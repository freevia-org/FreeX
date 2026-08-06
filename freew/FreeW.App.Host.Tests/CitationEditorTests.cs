using System.Linq;
using Free.Shared.Ribbon;
using FreeW.App.Host;
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

    [StaFact]
    public void InsertBibliography_BuildsBlockFromSourcesAndUndoReverts()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph("Body"));
        model.Sources.Add(new Source
        {
            Tag = "Sm24",
            Author = "Smith",
            Title = "A Work",
            Year = "2024"
        });
        var view = new DocumentView();
        view.LoadModel(model);
        var before = view.Model.Blocks.Count;

        view.InsertBibliography();
        view.CommitToModel();

        view.Model.Blocks.Count.Should().BeGreaterThan(before);
        view.Model.Blocks.OfType<Paragraph>()
            .Where(Citations.IsBibliographyParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Smith. (2024). A Work.");
        var bibliography = view.Model.Blocks.OfType<Paragraph>()
            .Where(Citations.IsBibliographyParagraph)
            .ToArray();
        bibliography[0].SpanningFieldOwner.Should().BeNull();
        bibliography[1].SpanningFieldStart!.Instruction.Should().Be(Citations.NativeFieldInstruction);
        bibliography[1].EndsSpanningField.Should().BeTrue();

        view.Commands.Undo().Should().BeTrue();
        view.Model.Blocks.OfType<Paragraph>()
            .Where(Citations.IsBibliographyParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().NotContain("Smith. (2024). A Work.");
    }

    [StaFact]
    public void ApplyCitationStyle_RefreshesExistingFieldAndBibliography_Undoably()
    {
        var source = new Source
        {
            Tag = "Sm24",
            Author = "Smith",
            Title = "A Work",
            Year = "2024",
            Publisher = "Press"
        };
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Sources.Add(source);
        model.Blocks.Add(new Paragraph { Runs = { Run.ComplexFieldRun(" CITATION Sm24 ", "(Smith, 2024)") } });
        model.Blocks.AddRange(Citations.BuildBibliography(model, CitationStyle.Apa));
        var view = new DocumentView();
        view.LoadModel(model);

        view.ApplyCitationStyle(CitationStyle.Ieee);

        view.Model.BibliographyStyle.Should().Be(CitationStyle.Ieee);
        view.Model.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.ComplexField?.Keyword == "CITATION").Text.Should().Be("[1]");
        view.Model.Blocks.Where(Citations.IsBibliographyParagraph)
            .Select(block => ((Paragraph)block).PlainText)
            .Should().Equal("References", "[1] Smith, \"A Work,\" Press, 2024.");

        view.Commands.Undo().Should().BeTrue();
        view.Model.BibliographyStyle.Should().Be(CitationStyle.Apa);
        view.Model.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.ComplexField?.Keyword == "CITATION").Text.Should().Be("(Smith, 2024)");
    }

    [StaFact]
    public void CitationStyleRibbonState_TracksInitialLoadedAndAppliedStyles()
    {
        var view = new DocumentView();
        view.LoadModel(new TextDocument { BibliographyStyle = CitationStyle.Harvard });
        var stateStore = new RibbonStateStore();
        var registry = FreeWRibbonCommands.Build(view, stateStore);
        var commandId = new RibbonCommandId("freew.citation-style");

        registry.TryGet(commandId, out var command).Should().BeTrue();
        var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
        stateStore.GetState(commandId).Value.Should().Be("Harvard");
        stateful.GetState().Value.Should().Be("Harvard");

        view.LoadModel(new TextDocument { BibliographyStyle = CitationStyle.Chicago });
        stateStore.GetState(commandId).Value.Should().Be("Chicago");

        command!.Execute(RibbonCommandContext.ForSelectedValue("Vancouver"));

        view.Model.BibliographyStyle.Should().Be(CitationStyle.Vancouver);
        stateful.GetState().Value.Should().Be("Vancouver");
        stateStore.GetState(commandId).Value.Should().Be("Vancouver");
    }
}
