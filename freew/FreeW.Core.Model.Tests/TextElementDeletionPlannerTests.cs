namespace FreeW.Core.Model.Tests;

public sealed class TextElementDeletionPlannerTests
{
    [Theory]
    [InlineData("a😀b", 3, true, 1, 2)]
    [InlineData("a😀b", 1, false, 1, 2)]
    [InlineData("ae\u0301b", 3, true, 1, 2)]
    [InlineData("ae\u0301b", 1, false, 1, 2)]
    [InlineData("a👩‍💻b", 6, true, 1, 5)]
    [InlineData("a👩‍💻b", 1, false, 1, 5)]
    [InlineData("a👩‍💻b", 3, false, 1, 5)]
    [InlineData("a👩‍💻b", 3, true, 1, 5)]
    public void PlanChoosesTheWholeAdjacentOrContainingTextElement(
        string text, int caret, bool backward, int start, int length)
    {
        TextElementDeletionPlanner.Plan(text, caret, backward).Should().Be(new TextElementDeletionRange(start, length));
    }

    [Fact]
    public void StoryEdgesDoNotManufactureADeletion()
    {
        TextElementDeletionPlanner.Plan("abc", 0, backward: true).Should().BeNull();
        TextElementDeletionPlanner.Plan("abc", 3, backward: false).Should().BeNull();
        TextElementDeletionPlanner.Plan("", 0, backward: false).Should().BeNull();
    }

    [Fact]
    public void ApplyingAcrossFormattedRunsRetainsZeroWidthStructuralRunsAndSurvivingMetadata()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("ae", RunFormatting.Default with { Bold = true }));
        paragraph.Runs.Add(Run.CommentReference(71));
        paragraph.Runs.Add(new Run("\u0301b") { HyperlinkUrl = "https://example.invalid/" });

        TextElementDeletionPlanner.Apply(paragraph, new(1, 2), false, "reviewer", null);

        paragraph.PlainText.Should().Be("ab");
        paragraph.Runs.Should().Contain(run => run.IsCommentReference && run.CommentId == 71);
        paragraph.Runs.First().Formatting.Bold.Should().BeTrue();
        paragraph.Runs.Last().HyperlinkUrl.Should().Be("https://example.invalid/");
    }

    [Fact]
    public void ApplyingADeletionPreservesAnUnrelatedAtomicField()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.DateField("cached"));
        paragraph.Runs.Add(new Run("a👩‍💻b"));

        TextElementDeletionPlanner.Apply(paragraph, new("cacheda".Length, "👩‍💻".Length), false, "reviewer", null);

        paragraph.Runs.First().FieldKind.Should().Be(RunFieldKind.Date);
        paragraph.Runs.First().Text.Should().Be("cached");
        paragraph.Runs.Last().Text.Should().Be("ab");
    }

    [Fact]
    public void TrackedDeletionCollapsesTheAuthorsOwnWholeClusterInsertion()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("a"));
        paragraph.Runs.Add(new Run("👩‍💻") { Revision = RevisionKind.Inserted, RevisionAuthor = "reviewer" });
        paragraph.Runs.Add(new Run("b"));

        var result = TextElementDeletionPlanner.Apply(paragraph, new(1, 5), true, "reviewer", null);

        paragraph.PlainText.Should().Be("ab");
        result.KeptDeletedText.Should().BeFalse();
        result.CaretOffset.Should().Be(1);
    }

    [Fact]
    public void TakingBackAllInsertedControlTextPreservesTheEmptyControl()
    {
        var paragraph = new Paragraph();
        var run = Run.PlainTextControl("😀", tag: "editable-field");
        run.Revision = RevisionKind.Inserted;
        run.RevisionAuthor = "reviewer";
        paragraph.Runs.Add(run);

        TextElementDeletionPlanner.Apply(paragraph, new(0, 2), true, "reviewer", null);

        paragraph.PlainText.Should().BeEmpty();
        paragraph.Runs.Should().ContainSingle().Which.Control!.Tag.Should().Be("editable-field");
    }
}
