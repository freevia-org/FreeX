using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using System.Windows.Documents;

namespace FreeW.App.Host.Tests;

public sealed class UpdateFieldsStoryTests
{
    [StaFact]
    public void WrapperStoryFieldsRenderAndUpdateFromTheOwningDocumentContext()
    {
        var owner = TextDocument.CreateEmpty();
        owner.Properties.Title = "Owning title";
        owner.Properties.Author = "Owning author";
        var wrapper = TextDocument.CreateEmpty();
        wrapper.Blocks.Clear();
        wrapper.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.TitleField("stale title"),
                Run.ComplexFieldRun(" AUTHOR ", "stale author"),
            },
        });
        var view = new DocumentView
        {
            FieldEvaluationDocument = owner,
        };

        view.LoadModel(wrapper);

        new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text
            .Should().ContainAll("Owning title", "Owning author");

        view.UpdateFields();

        var runs = view.Model.Blocks.OfType<Paragraph>().Single().Runs;
        runs[0].Text.Should().Be("Owning title");
        runs[1].Text.Should().Be("Owning author");
    }

    [StaFact]
    public void ToggleFieldCodes_TogglesEveryModelledStoryFromTheSharedMajority()
    {
        var document = CreateStoryDocument();
        AddOneComplexFieldPerStory(document);
        var view = new DocumentView();
        view.LoadModel(document);

        view.ToggleFieldCodes();

        var fields = ComplexFields(view.Model);
        fields.Should().OnlyContain(run => run.ComplexField!.ShowCode);

        view.ToggleFieldCodes();

        ComplexFields(view.Model).Should().OnlyContain(run => !run.ComplexField!.ShowCode);
    }

    [StaFact]
    public void UpdateFields_RefreshesEveryModelledStoryAndHonorsStoryGuards()
    {
        var document = CreateStoryDocument();
        var view = new DocumentView();
        view.LoadModel(document);

        view.UpdateFields();

        var updated = view.Model;
        var titleFields = DocumentFieldStories.Enumerate(updated)
            .SelectMany(item => item.Paragraph.Runs)
            .Where(run => run.FieldKind == RunFieldKind.Title)
            .ToList();
        titleFields.Should().HaveCount(8);
        titleFields.Should().OnlyContain(run => run.Text == "Current title");

        updated.Footer!.Paragraphs[0].Runs[0].Text.Should().Be("locked footer");
        updated.Footer.Paragraphs[0].Runs[0].ComplexField!.IsLocked.Should().BeTrue();
        updated.Header!.Paragraphs[0].Runs[1].Text.Should().Be("cached heading");
        var nestedIf = ((Paragraph)updated.Blocks[0]).Runs[1];
        nestedIf.ComplexField!.NestedFields.Should().ContainSingle()
            .Which.CachedResult.Should().Be("Current title");
        nestedIf.ComplexField.Instruction.Should().Contain("Current title");
        nestedIf.Text.Should().Be("matched");
    }

    [StaFact]
    public void UpdateFields_RefreshesSeqFieldsInsideNestedTablesInStoryOrder()
    {
        var document = BuildNestedTableSequenceDocument();
        var view = new DocumentView();
        view.LoadModel(document);

        view.UpdateFields();

        SequenceResults(view.Model).Should().Equal("1", "2", "3", "4");
    }

    internal static TextDocument CreateStoryDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Title = "Current title";
        document.Blocks.Clear();

        var textBoxParagraph = new Paragraph { Runs = { Run.TitleField("stale text box") } };
        var body = new Paragraph
        {
            Runs =
            {
                new Run(string.Empty)
                {
                    Shape = new Shape { TextParagraphs = { textBoxParagraph } },
                },
                CreateNestedTitleConditional(),
            },
        };
        document.Blocks.Add(body);

        document.Header = new HeaderFooter
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        Run.TitleField("stale header"),
                        Run.ComplexFieldRun(" STYLEREF 1 ", "cached heading"),
                    },
                },
            },
        };
        document.Footer = new HeaderFooter
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        Run.ComplexFieldRun(
                            " DOCPROPERTY Title ",
                            "locked footer",
                            sequence: new ComplexFieldSequenceMetadata(IsLocked: true, IsDirty: true)),
                    },
                },
            },
        };
        document.EvenHeader = new HeaderFooter
        {
            Paragraphs = { new Paragraph { Runs = { Run.TitleField("stale even header") } } },
        };
        document.FirstFooter = new HeaderFooter
        {
            Paragraphs = { new Paragraph { Runs = { Run.TitleField("stale first footer") } } },
        };
        document.Footnotes[1] = new Footnote(1)
        {
            Content = { new Paragraph { Runs = { Run.TitleField("stale footnote") } } },
        };
        document.Endnotes[1] = new Endnote(1)
        {
            Content = { new Paragraph { Runs = { Run.TitleField("stale endnote") } } },
        };
        var comment = new Comment(1)
        {
            Content = { new Paragraph { Runs = { Run.TitleField("stale comment") } } },
        };
        comment.Replies.Add(new Comment(2)
        {
            Content = { new Paragraph { Runs = { Run.TitleField("stale reply") } } },
        });
        document.Comments[1] = comment;
        return document;
    }

    private static void AddOneComplexFieldPerStory(TextDocument document)
    {
        var stories = DocumentFieldStories.Enumerate(document)
            .GroupBy(story => story.StoryKind)
            .Select(group => group.First())
            .ToList();
        stories.Should().HaveCount(Enum.GetValues<DocumentFieldStoryKind>().Length);

        for (var index = 0; index < stories.Count; index++)
        {
            stories[index].Paragraph.Runs.Add(new Run("cached")
            {
                ComplexField = new ComplexField(" TITLE ") { ShowCode = index == 0 },
            });
        }
    }

    private static IReadOnlyList<Run> ComplexFields(TextDocument document) =>
        DocumentFieldStories.Enumerate(document)
            .SelectMany(story => story.Paragraph.Runs)
            .Where(run => run.ComplexField is not null)
            .ToList();

    private static Run CreateNestedTitleConditional() =>
        Run.ComplexFieldRun(
            " IF stale = \"Current title\" \"matched\" \"missed\" ",
            "missed",
            nestedFields:
            [
                new NestedComplexField(
                    new ComplexField(" DOCPROPERTY Title "),
                    "stale",
                    NestedComplexFieldPlacement.Instruction,
                    Offset: 4,
                    Length: 5)
            ]);

    private static TextDocument BuildNestedTableSequenceDocument()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph { Runs = { Run.ComplexFieldRun(" SEQ Figure ", "stale") } });
        var outer = Table.Create(1, 1);
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0].Runs.Clear();
        nested.Rows[0].Cells[0].Paragraphs[0].Runs.Add(Run.ComplexFieldRun(" SEQ Figure ", "stale"));
        outer.Rows[0].Cells[0].NestedTables.Add(nested);
        outer.Rows[0].Cells[0].Paragraphs[0].Runs.Clear();
        outer.Rows[0].Cells[0].Paragraphs[0].Runs.Add(Run.ComplexFieldRun(" SEQ Figure ", "stale"));
        document.Blocks.Add(outer);
        document.Blocks.Add(new Paragraph { Runs = { Run.ComplexFieldRun(" SEQ Figure ", "stale") } });
        return document;
    }

    private static IEnumerable<string> SequenceResults(TextDocument document) =>
        DocumentFieldStories.Enumerate(document)
            .Where(story => story.StoryKind == DocumentFieldStoryKind.MainDocument)
            .SelectMany(story => story.Paragraph.Runs)
            .Where(run => run.ComplexField is { Keyword: "SEQ" })
            .Select(run => run.Text);
}
