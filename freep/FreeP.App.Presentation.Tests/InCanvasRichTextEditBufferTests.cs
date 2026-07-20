using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class InCanvasRichTextEditBufferTests
{
    [Fact]
    public void MultiCharacterReplacement_PreservesMixedRunsAndUsesSelectedRunFormat()
    {
        var buffer = new InCanvasRichTextEditBuffer(MixedBody("Alpha", "Beta"));

        buffer.ReplacePlainText("AlXYBeta");

        var runs = buffer.Body.Paragraphs.Single().Runs;
        runs.Should().HaveCount(2);
        runs[0].Text.Should().Be("AlXY");
        runs[0].Bold.Should().BeTrue();
        runs[1].Text.Should().Be("Beta");
        runs[1].Italic.Should().BeTrue();
    }

    [Fact]
    public void PasteLikeInsertionAtCaret_InheritsPrecedingRunAndKeepsFollowingRun()
    {
        var buffer = new InCanvasRichTextEditBuffer(MixedBody("Alpha", "Beta"));

        buffer.ReplacePlainText("Alpha pasted Beta");

        var runs = buffer.Body.Paragraphs.Single().Runs;
        runs.Should().HaveCount(2);
        runs[0].Text.Should().Be("Alpha pasted ");
        runs[0].Bold.Should().BeTrue();
        runs[1].Text.Should().Be("Beta");
        runs[1].Italic.Should().BeTrue();
    }

    [Fact]
    public void NewlineInsertion_SplitsParagraphAndRetainsRunAndParagraphFormatting()
    {
        var source = MixedBody("Alpha", "Beta");
        source.Paragraphs[0].Align = TextAlign.Center;
        source.Paragraphs[0].BulletKind = BulletKind.Char;
        source.Paragraphs[0].BulletChar = "*";
        var buffer = new InCanvasRichTextEditBuffer(source);

        buffer.ReplacePlainText("Alpha\nBeta");

        var body = buffer.Body;
        body.Paragraphs.Should().HaveCount(2);
        body.Paragraphs[0].Runs.Single().Text.Should().Be("Alpha");
        body.Paragraphs[0].Runs.Single().Bold.Should().BeTrue();
        body.Paragraphs[1].Runs.Single().Text.Should().Be("Beta");
        body.Paragraphs[1].Runs.Single().Italic.Should().BeTrue();
        body.Paragraphs.Should().OnlyContain(paragraph =>
            paragraph.Align == TextAlign.Center
            && paragraph.BulletKind == BulletKind.Char
            && paragraph.BulletChar == "*");
    }

    [Fact]
    public void NewlineDeletion_MergesParagraphsAndRetainsMixedRuns()
    {
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph
        {
            Align = TextAlign.Right,
            Runs = { new Run { Text = "Alpha", Bold = true } },
        });
        source.Paragraphs.Add(new Paragraph
        {
            Align = TextAlign.Center,
            Runs = { new Run { Text = "Beta", Italic = true } },
        });
        var buffer = new InCanvasRichTextEditBuffer(source);

        buffer.ReplacePlainText("AlphaBeta");

        var paragraph = buffer.Body.Paragraphs.Single();
        paragraph.Align.Should().Be(TextAlign.Right);
        paragraph.Runs.Should().HaveCount(2);
        paragraph.Runs[0].Text.Should().Be("Alpha");
        paragraph.Runs[0].Bold.Should().BeTrue();
        paragraph.Runs[1].Text.Should().Be("Beta");
        paragraph.Runs[1].Italic.Should().BeTrue();
    }

    [Fact]
    public void ImeLikeCompositionReplacement_ChangesOnlyComposedSpan()
    {
        var source = MixedBody("pre", "compose");
        source.Paragraphs[0].Runs.Add(new Run { Text = "post", Underline = true });
        var buffer = new InCanvasRichTextEditBuffer(source);

        buffer.ReplacePlainText("pre\u65e5\u672cpost");

        var runs = buffer.Body.Paragraphs.Single().Runs;
        runs.Should().HaveCount(3);
        runs[0].Text.Should().Be("pre");
        runs[0].Bold.Should().BeTrue();
        runs[1].Text.Should().Be("\u65e5\u672c");
        runs[1].Italic.Should().BeTrue();
        runs[2].Text.Should().Be("post");
        runs[2].Underline.Should().BeTrue();
    }

    [Fact]
    public void LocalTextAndFormattingMutations_DoNotChangeOriginalBeforeCommit()
    {
        var original = MixedBody("Alpha", "Beta");
        var buffer = new InCanvasRichTextEditBuffer(original);

        buffer.ReplacePlainText("Alpha changed Beta");
        buffer.ToggleTextFormat(
            TableCellTextFormatKind.Underline,
            new InCanvasEditorTextSelection(0, 5)).Should().BeTrue();

        InCanvasTextEditPlanner.ExtractPlainText(original).Should().Be("AlphaBeta");
        original.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => !run.Underline);
        InCanvasTextEditPlanner.ExtractPlainText(buffer.Body).Should().Be("Alpha changed Beta");
        buffer.Body.Paragraphs[0].Runs.First().Underline.Should().BeTrue();
    }

    [Fact]
    public void CollapsedCaretFormatting_AppliesToSubsequentTypingWithoutRestylingBody()
    {
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "plain" } } });
        var buffer = new InCanvasRichTextEditBuffer(source);
        var caret = new InCanvasEditorTextSelection(2, 2);

        buffer.ToggleTextFormat(TableCellTextFormatKind.Bold, caret).Should().BeTrue();

        buffer.Body.Paragraphs[0].Runs.Should().OnlyContain(run => !run.Bold);
        buffer.Plan(caret).InitialSelectionStyle.Bold.Should().BeTrue();

        buffer.ReplacePlainText("plXain");

        var runs = buffer.Body.Paragraphs[0].Runs;
        runs.Select(run => run.Text).Should().Equal("pl", "X", "ain");
        runs.Select(run => run.Bold).Should().Equal(false, true, false);
    }

    [Fact]
    public void CollapsedCaretParagraphFormatting_OnlyChangesCaretParagraph()
    {
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "One" } } });
        source.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Two" } } });
        var buffer = new InCanvasRichTextEditBuffer(source);

        buffer.ToggleParagraphBullets(new InCanvasEditorTextSelection(5, 5)).Should().BeTrue();

        buffer.Body.Paragraphs[0].BulletKind.Should().Be(BulletKind.None);
        buffer.Body.Paragraphs[1].BulletKind.Should().Be(BulletKind.Char);
    }

    [Fact]
    public void RichCommitEquality_DetectsListOnlyAndIndentOnlyChanges()
    {
        var original = MixedBody("Alpha", "Beta");
        var listEdited = CloneWith(original, paragraph =>
        {
            paragraph.BulletKind = BulletKind.Auto;
            paragraph.AutoNumType = AutoNumType.AlphaLcPeriod;
        });
        var indentEdited = CloneWith(original, paragraph => paragraph.Level = 2);

        InCanvasTextEditPlanner.TextBodiesEqualForTableCellCommit(original, listEdited)
            .Should().BeFalse();
        InCanvasTextEditPlanner.TextBodiesEqualForTableCellCommit(original, indentEdited)
            .Should().BeFalse();
    }

    private static TextBody MixedBody(string first, string second)
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run { Text = first, Bold = true },
                new Run { Text = second, Italic = true },
            },
        });
        return body;
    }

    private static TextBody CloneWith(TextBody source, Action<Paragraph> mutate)
    {
        var clone = new InCanvasRichTextEditBuffer(source).Body;
        mutate(clone.Paragraphs[0]);
        return clone;
    }
}
