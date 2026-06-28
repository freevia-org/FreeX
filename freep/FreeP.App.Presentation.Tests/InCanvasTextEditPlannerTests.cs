using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class InCanvasTextEditPlannerTests
{
    [Fact]
    public void CommitPlainText_UnchangedText_ReturnsNoCommand()
    {
        var body = MakeBody("Hello");
        var planner = InCanvasTextEditPlanner.BeginPlainText(0, 1, body);

        var decision = planner.CommitPlainText("Hello");

        decision.Outcome.Should().Be(InCanvasTextEditOutcome.Unchanged);
        decision.Command.Should().BeNull();
    }

    [Fact]
    public void CommitPlainText_ChangedText_BuildsUndoableShapeTextBodyCommand()
    {
        var presentation = Presentation.CreateEmpty();
        var shape = new SlideShape { Id = 1, TextBody = MakeBody("Hello") };
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);

        var planner = InCanvasTextEditPlanner.BeginPlainText(0, shape.Id, shape.TextBody);
        var decision = planner.CommitPlainText("First\nSecond");

        decision.Outcome.Should().Be(InCanvasTextEditOutcome.Commit);
        decision.Command.Should().NotBeNull();
        decision.Command!.Label.Should().Be("Edit Text");

        var bus = new PresentationCommandBus(presentation);
        bus.Execute(decision.Command);

        shape.TextBody!.Paragraphs.Should().HaveCount(2);
        shape.TextBody.Paragraphs[0].Runs[0].Text.Should().Be("First");
        shape.TextBody.Paragraphs[1].Runs[0].Text.Should().Be("Second");
        shape.TextBody.Paragraphs[0].Runs[0].FontFamily.Should().Be("Aptos");
        shape.TextBody.Paragraphs[0].Runs[0].Bold.Should().BeTrue();

        bus.Undo();
        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Hello");
    }

    [Fact]
    public void CommitRichText_ColorOnlyChange_ReturnsCommand()
    {
        var original = MakeBody("Hello", new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)));
        var edited = MakeBody("Hello", new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)));
        var planner = InCanvasTextEditPlanner.BeginRichText(0, 1, original);

        var decision = planner.CommitRichText(edited);

        decision.Outcome.Should().Be(InCanvasTextEditOutcome.Commit);
        decision.Command.Should().NotBeNull();
        decision.Command!.Label.Should().Be("Edit Rich Text");
    }

    [Fact]
    public void SetShapeTextBodyCommand_ClonesInputAndUndoSnapshots()
    {
        var presentation = Presentation.CreateEmpty();
        var original = MakeBody("Original");
        original.Paragraphs[0].Runs[0].Hyperlink = new Hyperlink { Url = "https://example.test" };
        var replacement = MakeBody("Replacement");

        var shape = new SlideShape { Id = 1, TextBody = original };
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);

        var bus = new PresentationCommandBus(presentation);
        var command = new SetShapeTextBodyCommand(0, shape.Id, replacement);
        replacement.Paragraphs[0].Runs[0].Text = "Mutated before apply";

        bus.Execute(command);
        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Replacement");

        shape.TextBody.Paragraphs[0].Runs[0].Text = "Mutated after apply";
        bus.Undo();

        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Original");
        shape.TextBody.Paragraphs[0].Runs[0].Hyperlink.Should().NotBeSameAs(original.Paragraphs[0].Runs[0].Hyperlink);
        shape.TextBody.Paragraphs[0].Runs[0].Hyperlink!.Url.Should().Be("https://example.test");
    }

    private static TextBody MakeBody(string text, ThemeAwareColor? color = null)
    {
        var body = new TextBody { Wrap = true, Anchor = VerticalAnchor.Middle };
        var paragraph = new Paragraph { Align = TextAlign.Left };
        paragraph.Runs.Add(new Run
        {
            Text = text,
            FontFamily = "Aptos",
            FontSizePt = 18,
            Bold = true,
            Color = color,
        });
        body.Paragraphs.Add(paragraph);
        return body;
    }
}
