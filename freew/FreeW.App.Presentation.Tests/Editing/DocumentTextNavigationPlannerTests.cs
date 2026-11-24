using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentTextNavigationPlannerTests
{
    [Theory]
    [InlineData(DocumentEditorInputKey.Left, false, true, false, DocumentTextNavigationIntent.LineStart)]
    [InlineData(DocumentEditorInputKey.Right, false, true, false, DocumentTextNavigationIntent.LineEnd)]
    [InlineData(DocumentEditorInputKey.Up, false, true, false, DocumentTextNavigationIntent.PreviousParagraph)]
    [InlineData(DocumentEditorInputKey.Down, false, true, false, DocumentTextNavigationIntent.NextParagraph)]
    [InlineData(DocumentEditorInputKey.Down, true, false, false, DocumentTextNavigationIntent.NextParagraphStart)]
    [InlineData(DocumentEditorInputKey.Home, false, true, false, DocumentTextNavigationIntent.StoryStart)]
    [InlineData(DocumentEditorInputKey.End, true, false, false, DocumentTextNavigationIntent.StoryEnd)]
    [InlineData(DocumentEditorInputKey.Left, false, false, true, DocumentTextNavigationIntent.PreviousWord)]
    [InlineData(DocumentEditorInputKey.Right, false, false, true, DocumentTextNavigationIntent.NextWord)]
    [InlineData(DocumentEditorInputKey.Right, true, false, false, DocumentTextNavigationIntent.NextWordStart)]
    public void CommandOptionAndControlHaveDistinctWordNavigationBindings(
        DocumentEditorInputKey key, bool control, bool command, bool option, DocumentTextNavigationIntent expected)
    {
        var plan = DocumentTextNavigationPlanner.Plan(key, control, command, option, shift: true);

        plan.Intent.Should().Be(expected);
        plan.ExtendSelection.Should().BeTrue();
    }

    [Fact]
    public void OptionStopsAtWordEndsWhileControlStopsAtTheNextWordStart()
    {
        string[] text = ["alpha   beta, gamma"];
        var position = new DocumentTextNavigationPosition(0, 0);

        DocumentTextNavigationPlanner.Move(text, position, DocumentTextNavigationIntent.NextWord)
            .Should().Be(new DocumentTextNavigationPosition(0, 5));
        DocumentTextNavigationPlanner.Move(text, position, DocumentTextNavigationIntent.NextWordStart)
            .Should().Be(new DocumentTextNavigationPosition(0, 8));
        DocumentTextNavigationPlanner.Move(text, new(0, 8), DocumentTextNavigationIntent.PreviousWord)
            .Should().Be(position);
    }

    [Fact]
    public void CharacterNavigationDoesNotSplitEmojiOrCombiningSequences()
    {
        string[] text = ["a👩‍💻e\u0301z"];
        var afterEmoji = "a👩‍💻".Length;
        var afterAccent = "a👩‍💻e\u0301".Length;

        DocumentTextNavigationPlanner.Move(text, new(0, 1), DocumentTextNavigationIntent.NextCharacter)
            .Should().Be(new DocumentTextNavigationPosition(0, afterEmoji));
        DocumentTextNavigationPlanner.Move(text, new(0, afterAccent), DocumentTextNavigationIntent.PreviousCharacter)
            .Should().Be(new DocumentTextNavigationPosition(0, afterEmoji));
    }

    [Fact]
    public void ParagraphEdgesAdvanceAgainAtBoundariesAndStoryEdgesIncludeEmptyParagraphs()
    {
        string[] text = ["first", "", "last"];

        DocumentTextNavigationPlanner.Move(text, new(2, 3), DocumentTextNavigationIntent.PreviousParagraph)
            .Should().Be(new DocumentTextNavigationPosition(2, 0));
        DocumentTextNavigationPlanner.Move(text, new(2, 0), DocumentTextNavigationIntent.PreviousParagraph)
            .Should().Be(new DocumentTextNavigationPosition(1, 0));
        DocumentTextNavigationPlanner.Move(text, new(0, 5), DocumentTextNavigationIntent.NextParagraph)
            .Should().Be(new DocumentTextNavigationPosition(1, 0));
        DocumentTextNavigationPlanner.Move(text, new(1, 0), DocumentTextNavigationIntent.StoryEnd)
            .Should().Be(new DocumentTextNavigationPosition(2, 4));
    }

    [Fact]
    public void ControlDownMovesToTheNextParagraphStartWhileCommandDownMovesToTheCurrentEnd()
    {
        string[] text = ["first paragraph", "second", "last"];
        var current = new DocumentTextNavigationPosition(0, 3);

        DocumentTextNavigationPlanner.Move(text, current, DocumentTextNavigationIntent.NextParagraphStart)
            .Should().Be(new DocumentTextNavigationPosition(1, 0));
        DocumentTextNavigationPlanner.Move(text, current, DocumentTextNavigationIntent.NextParagraph)
            .Should().Be(new DocumentTextNavigationPosition(0, text[0].Length));
        DocumentTextNavigationPlanner.Move(text, new(2, 2), DocumentTextNavigationIntent.NextParagraphStart)
            .Should().Be(new DocumentTextNavigationPosition(2, text[2].Length));
    }

    [Theory]
    [InlineData(2, 6)]
    [InlineData(3, 6)]
    [InlineData(5, 6)]
    [InlineData(7, 8)]
    public void ForwardNavigationRecoversToATrueBoundaryFromInsideAnExistingTextElement(int offset, int expected)
    {
        string[] text = ["a👩‍💻e\u0301z"];

        DocumentTextNavigationPlanner.Move(text, new(0, offset), DocumentTextNavigationIntent.NextCharacter)
            .Should().Be(new DocumentTextNavigationPosition(0, expected));
    }
}
