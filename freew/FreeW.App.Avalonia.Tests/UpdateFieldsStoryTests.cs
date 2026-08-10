using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class UpdateFieldsStoryTests
{
    [Fact]
    public void ToggleFieldCodes_TogglesEveryModelledStoryFromTheSharedMajority()
    {
        var document = CreateStoryDocument();
        AddOneComplexFieldPerStory(document);
        var view = new DocumentView();
        view.LoadDocument(document);

        view.ToggleFieldCodes();

        ComplexFields(document).Should().OnlyContain(run => run.ComplexField!.ShowCode);

        view.ToggleFieldCodes();

        ComplexFields(document).Should().OnlyContain(run => !run.ComplexField!.ShowCode);
    }

    [Fact]
    public void UpdateFields_RefreshesEveryModelledStoryAndHonorsStoryGuards()
    {
        var document = CreateStoryDocument();
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        var titleFields = DocumentFieldStories.Enumerate(document)
            .SelectMany(item => item.Paragraph.Runs)
            .Where(run => run.FieldKind == RunFieldKind.Title)
            .ToList();
        titleFields.Should().HaveCount(8);
        titleFields.Should().OnlyContain(run => run.Text == "Current title");

        document.Footer!.Paragraphs[0].Runs[0].Text.Should().Be("locked footer");
        document.Footer.Paragraphs[0].Runs[0].ComplexField!.IsLocked.Should().BeTrue();
        document.Header!.Paragraphs[0].Runs[1].Text.Should().Be("cached heading");
        var nestedIf = ((Paragraph)document.Blocks[0]).Runs[1];
        nestedIf.ComplexField!.NestedFields.Should().ContainSingle()
            .Which.CachedResult.Should().Be("Current title");
        nestedIf.ComplexField.Instruction.Should().Contain("Current title");
        nestedIf.Text.Should().Be("matched");
    }

    private static TextDocument CreateStoryDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Title = "Current title";
        document.Blocks.Clear();

        var textBoxParagraph = new Paragraph { Runs = { Run.TitleField("stale text box") } };
        document.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run(string.Empty)
                {
                    Shape = new Shape { TextParagraphs = { textBoxParagraph } },
                },
                CreateNestedTitleConditional(),
            },
        });
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
}
