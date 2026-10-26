using FluentAssertions;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r482: replacing text that spans several formatting runs.
///
/// <para>A coverage scan for production types no test names found FindReplaceMatchResolver and
/// FindReplaceRunSpanWriter. Those names are a false signal on their own - both are reached through
/// ReplaceOneCommand, which FindReplaceStaleMatchTests exercises. What the scan did lead to is real:
/// that file is the ONLY test touching these commands and every paragraph it builds has a single
/// run, so the multi-run branch of ApplyReplacement - which empties the runs between start and end,
/// truncates the end run, and splices the replacement into the start run - had no coverage at
/// all.</para>
///
/// <para>A match spanning runs is the normal case, not an exotic one: PowerPoint splits a run at
/// every formatting change, so bolding one letter inside a word is enough to make "beautiful" span
/// three runs. The behaviour is correct today; these tests exist so it stays that way, and each
/// checks undo as well, because the writer edits several runs and the command has to put all of them
/// back.</para>
/// </summary>
public sealed class R482_ReplaceAcrossFormattingRunsTests
{
    private static Presentation WithRuns(out SlideShape shape, params string[] runTexts)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var body = new TextBody();
        var paragraph = new Paragraph();

        // Distinct formatting on the first run, so a run boundary is a real one rather than an
        // arbitrary split: this is what makes a match span runs in a real deck.
        for (var index = 0; index < runTexts.Length; index++)
            paragraph.Runs.Add(new Run { Text = runTexts[index], Bold = index == 0 });

        body.Paragraphs.Add(paragraph);
        shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 685800,
            TextBody = body,
        };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        return presentation;
    }

    private static string Text(SlideShape shape) =>
        string.Concat(shape.TextBody!.Paragraphs[0].Runs.Select(run => run.Text));

    private static string[] RunTexts(SlideShape shape) =>
        shape.TextBody!.Paragraphs[0].Runs.Select(run => run.Text).ToArray();

    private static (Presentation Presentation, ReplaceOneCommand Command, string[] Before) Arrange(
        string replacement, out SlideShape shape, params string[] runTexts)
    {
        var presentation = WithRuns(out shape, runTexts);
        var before = RunTexts(shape);

        var matches = PresentationTextSearch.FindAll(presentation, "beautiful");
        matches.Should().ContainSingle("the probe text must actually contain one match to replace");

        return (presentation, new ReplaceOneCommand(matches[0], replacement), before);
    }

    [Theory]
    // The single-run case the existing suite already covers, kept as a control: if this ever
    // diverges from the spanning cases the fault is in the harness, not the writer.
    [InlineData("small", "Hello small world", new[] { "Hello beautiful world" })]
    [InlineData("small", "Hello small world", new[] { "Hello beau", "tiful world" })]
    [InlineData("small", "Hello small world", new[] { "Hello beau", "ti", "ful world" })]
    [InlineData("small", "Hello small", new[] { "Hello ", "beautiful" })]
    [InlineData("", "Hello  world", new[] { "Hello beau", "tiful world" })]
    [InlineData("extraordinarily lovely", "Hello extraordinarily lovely world", new[] { "Hello beau", "tiful world" })]
    public void ReplacingAMatchThatSpansRunsProducesTheRightText(
        string replacement, string expected, string[] runTexts)
    {
        var (presentation, command, _) = Arrange(replacement, out var shape, runTexts);

        command.Apply(presentation);

        Text(shape).Should().Be(expected);
    }

    [Fact]
    public void UndoRestoresEveryRunAcrossTwoRuns() => AssertUndoRestoresRuns("Hello beau", "tiful world");

    [Fact]
    public void UndoRestoresEveryRunAcrossThreeRuns() => AssertUndoRestoresRuns("Hello beau", "ti", "ful world");

    [Fact]
    public void UndoRestoresEveryRunWhenTheMatchEndsTheParagraph() => AssertUndoRestoresRuns("Hello ", "beautiful");

    private static void AssertUndoRestoresRuns(params string[] runTexts)
    {
        // The writer edits the start run, every run between, and the end run. Undo has to restore
        // all of them, not just the one holding the match's start.
        var (presentation, command, before) = Arrange("small", out var shape, runTexts);

        command.Apply(presentation);
        Text(shape).Should().NotBe(string.Concat(before), "the replacement must have done something");

        command.Revert(presentation);

        RunTexts(shape).Should().Equal(before, "undo must restore each run's original text exactly");
    }

    [Fact]
    public void TheRunCarryingTheReplacementKeepsItsOwnFormatting()
    {
        // The replacement lands in the START run, so it inherits that run's formatting -- matching
        // PowerPoint, where replaced text takes the formatting of the match's first character.
        var (presentation, command, _) = Arrange("small", out var shape, "Hello beau", "tiful world");

        command.Apply(presentation);

        var runs = shape.TextBody!.Paragraphs[0].Runs;
        runs[0].Text.Should().Be("Hello small");
        runs[0].Bold.Should().BeTrue("the start run's formatting must survive the splice");
    }
}
