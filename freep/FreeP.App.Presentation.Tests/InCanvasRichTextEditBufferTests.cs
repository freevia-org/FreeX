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
    public void SoftBreakInsertion_StaysInParagraphAsDedicatedBreakRun()
    {
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph
        {
            Runs = { new Run { Text = "AlphaBeta", Bold = true } },
        });
        var buffer = new InCanvasRichTextEditBuffer(source);

        buffer.InsertSoftBreak(new InCanvasEditorTextSelection(5, 5)).Should().BeTrue();

        buffer.Body.Paragraphs.Should().ContainSingle();
        buffer.Body.Paragraphs[0].Runs.Select(run => run.Text)
            .Should().Equal("Alpha", "\n", "Beta");
        buffer.PlainText.Should().Be("Alpha\nBeta");
        buffer.Body.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
        buffer.Body.Paragraphs[0].Runs[2].Bold.Should().BeTrue();
    }

    [Fact]
    public void SoftBreakInsertion_ReplacesSelectedTextWithoutCreatingParagraph()
    {
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph
        {
            Runs = { new Run { Text = "AlphaBeta" } },
        });
        var buffer = new InCanvasRichTextEditBuffer(source);

        buffer.InsertSoftBreak(new InCanvasEditorTextSelection(5, 9)).Should().BeTrue();

        buffer.PlainText.Should().Be("Alpha\n");
        buffer.Body.Paragraphs.Should().ContainSingle();
        buffer.Body.Paragraphs[0].Runs.Select(run => run.Text)
            .Should().Equal("Alpha", "\n");
    }

    [Fact]
    public void SoftBreakInsertion_EmptyBodyCreatesDedicatedBreakRun()
    {
        var buffer = new InCanvasRichTextEditBuffer(new TextBody());

        buffer.InsertSoftBreak(new InCanvasEditorTextSelection(0, 0)).Should().BeTrue();

        buffer.Body.Paragraphs.Should().ContainSingle();
        buffer.Body.Paragraphs[0].Runs.Select(run => run.Text)
            .Should().Equal("\n");
    }

    [Fact]
    public void EnterSplit_ClonesNumberingMetadataToTheNewParagraph()
    {
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph
        {
            Level = 1,
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.AlphaLcParenBoth,
            AutoNumStartAt = 4,
            Runs = { new Run { Text = "AlphaBeta" } },
        });
        var buffer = new InCanvasRichTextEditBuffer(source);

        buffer.ReplacePlainText("Alpha\nBeta");

        buffer.Body.Paragraphs.Should().HaveCount(2);
        buffer.Body.Paragraphs.Should().OnlyContain(paragraph =>
            paragraph.Level == 1
            && paragraph.BulletKind == BulletKind.Auto
            && paragraph.AutoNumType == AutoNumType.AlphaLcParenBoth
            && paragraph.AutoNumStartAt == 4);
    }

    [Fact]
    public void BackspaceJoin_KeepsLeadingParagraphListMetadata()
    {
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph
        {
            Level = 1,
            BulletKind = BulletKind.Char,
            BulletChar = "*",
            Runs = { new Run { Text = "Alpha" } },
        });
        source.Paragraphs.Add(new Paragraph
        {
            Level = 2,
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.AlphaUcPeriod,
            Runs = { new Run { Text = "Beta" } },
        });
        var buffer = new InCanvasRichTextEditBuffer(source);

        buffer.ReplacePlainText("AlphaBeta");

        var paragraph = buffer.Body.Paragraphs.Single();
        paragraph.Level.Should().Be(1);
        paragraph.BulletKind.Should().Be(BulletKind.Char);
        paragraph.BulletChar.Should().Be("*");
    }

    [Fact]
    public void SplitFirstParagraph_PreservesFollowingParagraphLineage()
    {
        var source = DistinctParagraphBody();
        var buffer = new InCanvasRichTextEditBuffer(source);

        buffer.ReplacePlainText("A1\nA2\nB\nC");

        AssertMetadata(buffer.Body.Paragraphs, 0, BulletKind.Char, 1, 10, 100);
        AssertMetadata(buffer.Body.Paragraphs, 1, BulletKind.Char, 1, 10, 100);
        AssertMetadata(buffer.Body.Paragraphs, 2, BulletKind.Auto, 2, 20, 200);
        AssertMetadata(buffer.Body.Paragraphs, 3, BulletKind.None, 3, 30, 300);
    }

    [Fact]
    public void SplitMiddleParagraph_PreservesTrailingParagraphLineage()
    {
        var source = DistinctParagraphBody();
        var buffer = new InCanvasRichTextEditBuffer(source);

        buffer.ReplacePlainText("A\nB1\nB2\nC");

        AssertMetadata(buffer.Body.Paragraphs, 0, BulletKind.Char, 1, 10, 100);
        AssertMetadata(buffer.Body.Paragraphs, 1, BulletKind.Auto, 2, 20, 200);
        AssertMetadata(buffer.Body.Paragraphs, 2, BulletKind.Auto, 2, 20, 200);
        AssertMetadata(buffer.Body.Paragraphs, 3, BulletKind.None, 3, 30, 300);
    }

    [Fact]
    public void SequenceAlignment_DistinguishesDuplicateSourceParagraphs()
    {
        var source = new TextBody();
        source.Paragraphs.Add(Paragraph("foo", BulletKind.Char, 1, 10, 100, "*"));
        source.Paragraphs.Add(Paragraph("foo", BulletKind.Auto, 2, 20, 200, null));

        InCanvasRichTextParagraphEditPlanner.ResolveSourceParagraphIndices(
                source.Paragraphs,
                new[] { "foo", "foo" })
            .Should().Equal(0, 1);
    }

    [Fact]
    public void EmptySplitParagraph_InheritsTheSourceParagraphBeforeTheAnchor()
    {
        var buffer = new InCanvasRichTextEditBuffer(DistinctParagraphBody());

        buffer.ReplacePlainText("\nA\nB\nC");

        AssertMetadata(buffer.Body.Paragraphs, 0, BulletKind.Char, 1, 10, 100);
        AssertMetadata(buffer.Body.Paragraphs, 1, BulletKind.Char, 1, 10, 100);
        AssertMetadata(buffer.Body.Paragraphs, 2, BulletKind.Auto, 2, 20, 200);
        AssertMetadata(buffer.Body.Paragraphs, 3, BulletKind.None, 3, 30, 300);
    }

    [Fact]
    public void RewrittenSplitBeforeTrailingAnchors_InheritsTheUnmatchedSourceParagraph()
    {
        var buffer = new InCanvasRichTextEditBuffer(DistinctParagraphBody());

        buffer.ReplacePlainText("X\nY\nB\nC");

        AssertMetadata(buffer.Body.Paragraphs, 0, BulletKind.Char, 1, 10, 100);
        AssertMetadata(buffer.Body.Paragraphs, 1, BulletKind.Char, 1, 10, 100);
        AssertMetadata(buffer.Body.Paragraphs, 2, BulletKind.Auto, 2, 20, 200);
        AssertMetadata(buffer.Body.Paragraphs, 3, BulletKind.None, 3, 30, 300);
    }

    [Fact]
    public void AnchorlessRewrite_EqualCount_UsesOrderedSourceLineage()
    {
        var source = DistinctParagraphBody();

        InCanvasRichTextParagraphEditPlanner.ResolveSourceParagraphIndices(
                source.Paragraphs,
                new[] { "X", "Y", "Z" })
            .Should().Equal(0, 1, 2);
    }

    [Fact]
    public void AnchorlessRewriteWithSplit_AssignsSurplusToLeadingLineage()
    {
        var source = DistinctParagraphBody();

        InCanvasRichTextParagraphEditPlanner.ResolveSourceParagraphIndices(
                source.Paragraphs,
                new[] { "X", "Y", "Z", "W" })
            .Should().Equal(0, 0, 1, 2);
    }

    [Fact]
    public void AnchorlessJoin_RetainsOrderedLeadingLineage()
    {
        var source = DistinctParagraphBody();

        InCanvasRichTextParagraphEditPlanner.ResolveSourceParagraphIndices(
                source.Paragraphs,
                new[] { "X", "Y" })
            .Should().Equal(0, 1);
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
    public void BaselineFormatting_TogglesSuperscriptAndSubscriptInSharedBuffer()
    {
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "x" } } });
        var buffer = new InCanvasRichTextEditBuffer(source);
        var selection = new InCanvasEditorTextSelection(0, 1);

        buffer.ToggleTextFormat(TableCellTextFormatKind.Superscript, selection).Should().BeTrue();
        buffer.Body.Paragraphs[0].Runs.Single().BaselineOffset.Should().Be(10000);
        source.Paragraphs[0].Runs.Single().BaselineOffset.Should().BeNull();

        buffer.ToggleTextFormat(TableCellTextFormatKind.Superscript, selection).Should().BeTrue();
        buffer.Body.Paragraphs[0].Runs.Single().BaselineOffset.Should().BeNull();
        buffer.ToggleTextFormat(TableCellTextFormatKind.Subscript, selection).Should().BeTrue();
        buffer.Body.Paragraphs[0].Runs.Single().BaselineOffset.Should().Be(-10000);
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

    private static TextBody DistinctParagraphBody()
    {
        var body = new TextBody();
        body.Paragraphs.Add(Paragraph("A", BulletKind.Char, 1, 10, 100, "*"));
        body.Paragraphs.Add(Paragraph("B", BulletKind.Auto, 2, 20, 200, null));
        body.Paragraphs.Add(Paragraph("C", BulletKind.None, 3, 30, 300, null));
        return body;
    }

    private static Paragraph Paragraph(
        string text,
        BulletKind bulletKind,
        int level,
        double spaceBefore,
        double spaceAfter,
        string? bulletChar)
    {
        var paragraph = new Paragraph
        {
            Level = level,
            BulletKind = bulletKind,
            BulletChar = bulletChar,
            AutoNumType = AutoNumType.AlphaLcPeriod,
            AutoNumStartAt = level + 1,
            SpaceBeforePt = spaceBefore,
            SpaceAfterPt = spaceAfter,
            Runs = { new Run { Text = text } },
        };
        paragraph.TabStops.Add(new TabStop { PositionEmu = level * 100L });
        return paragraph;
    }

    private static void AssertMetadata(
        IReadOnlyList<Paragraph> paragraphs,
        int index,
        BulletKind bulletKind,
        int level,
        double spaceBefore,
        double spaceAfter)
    {
        var paragraph = paragraphs[index];
        paragraph.BulletKind.Should().Be(bulletKind);
        paragraph.Level.Should().Be(level);
        paragraph.SpaceBeforePt.Should().Be(spaceBefore);
        paragraph.SpaceAfterPt.Should().Be(spaceAfter);
        paragraph.AutoNumStartAt.Should().Be(level + 1);
        paragraph.TabStops.Should().ContainSingle(stop => stop.PositionEmu == level * 100L);
    }

    private static TextBody CloneWith(TextBody source, Action<Paragraph> mutate)
    {
        var clone = new InCanvasRichTextEditBuffer(source).Body;
        mutate(clone.Paragraphs[0]);
        return clone;
    }
}
