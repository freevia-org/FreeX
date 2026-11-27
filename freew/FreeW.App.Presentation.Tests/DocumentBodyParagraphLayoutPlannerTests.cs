using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentBodyParagraphLayoutPlannerTests
{
    [Theory]
    [InlineData(0, 18)]
    [InlineData(1, 36)]
    public void UnpositionedNativeListRetainsItsApplicationInset(int level, double expected)
    {
        DocumentBodyParagraphLayoutPlanner.AdditionalListInsetPt(
            new ParagraphFormatting { ListKind = ListKind.Number }, level).Should().Be(expected);
    }

    [Theory]
    [InlineData(36)]
    [InlineData(72)]
    [InlineData(0)]
    public void NumberingGeometryDoesNotReceiveASecondNativeInset(double left)
    {
        var formatting = new ParagraphFormatting
        {
            ListLevelIndentLeftPt = left,
            ListLevelFirstLineIndentPt = -18,
            IndentLeftPt = left,
            FirstLineIndentPt = -18,
        };
        DocumentBodyParagraphLayoutPlanner.AdditionalListInsetPt(formatting, 1).Should().Be(0);
        DocumentBodyParagraphLayoutPlanner.TextFirstLineIndentPt(formatting).Should().Be(0);
        formatting.FirstLineIndentPt.Should().Be(-18, "layout must leave authored formatting intact");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(72)]
    public void AuthoredAbsoluteIndentIsNotAddedToANativeInset(double left)
    {
        DocumentBodyParagraphLayoutPlanner.AdditionalListInsetPt(
            new ParagraphFormatting { ListKind = ListKind.Number, IndentLeftPt = left, IndentLeftIsSet = true }, 0)
            .Should().Be(0);
    }

    [Fact]
    public void OrdinaryHangingAndPositiveFirstLineIndentsRemainUnchanged()
    {
        DocumentBodyParagraphLayoutPlanner.TextFirstLineIndentPt(
            new ParagraphFormatting { FirstLineIndentPt = -12 }).Should().Be(-12);
        DocumentBodyParagraphLayoutPlanner.TextFirstLineIndentPt(
            new ParagraphFormatting { ListLevelIndentLeftPt = 36, FirstLineIndentPt = 12 }).Should().Be(12);
    }

    [Fact]
    public void ImportedHangingMarkerAnchorIsIndependentOfItsDigitWidth()
    {
        var imported = new ParagraphFormatting { ListLevelIndentLeftPt = 36, FirstLineIndentPt = -18 };
        DocumentBodyParagraphLayoutPlanner.MarkerOffsetFromTextPt(imported, 8, 4.5).Should().Be(-18);
        DocumentBodyParagraphLayoutPlanner.MarkerOffsetFromTextPt(imported, 14, 4.5).Should().Be(-18);
        DocumentBodyParagraphLayoutPlanner.MarkerOffsetFromTextPt(ParagraphFormatting.Default, 8, 4.5)
            .Should().Be(-12.5, "unpositioned native markers retain the width plus gap fallback");
    }

    [Theory]
    [InlineData(true, false, 10, 12, 2)]
    [InlineData(false, true, 12, 10, 2)]
    [InlineData(true, true, 10, 12, 0)]
    [InlineData(true, false, 12, 10, 0)]
    [InlineData(false, false, 10, 12, 22)]
    public void MatchingStyleBoundaryUsesContextualSubtractionAndPreservesUnrelatedAdditivePolicy(
        bool previousContextual, bool nextContextual, double after, double before, double expected)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("first") { StyleId = "Same", Formatting = new ParagraphFormatting
        {
            SpaceAfterPt = after, SpaceAfterIsSet = true, ContextualSpacing = previousContextual,
        } });
        document.Blocks.Add(new Paragraph("next") { StyleId = "Same", Formatting = new ParagraphFormatting
        {
            SpaceBeforePt = before, SpaceBeforeIsSet = true, ContextualSpacing = nextContextual,
        } });
        var original = document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.Formatting).ToArray();
        var first = Spacing(document, 0);
        var next = Spacing(document, 1);
        (first.AfterPt + next.BeforePt).Should().Be(expected);
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.Formatting).Should().Equal(original);
    }

    [Fact]
    public void InheritedContextualSpacingCollapsesAcrossListInstancesButNotOtherStylesOrTables()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["List"] = new DocumentStyle
        {
            Id = "List", Name = "List", Paragraph = new ParagraphFormatting
            {
                ContextualSpacing = true, SpaceAfterPt = 10, SpaceAfterIsSet = true,
            },
        };
        document.Blocks.Add(new Paragraph("one") { StyleId = "List" });
        document.Blocks.Add(new Paragraph("restarted") { StyleId = "List", Formatting = new ParagraphFormatting
        {
            ListKind = ListKind.Number, ListStartOverride = 1,
        } });
        document.Blocks.Add(new Paragraph("other") { StyleId = "Other" });
        document.Blocks.Add(new Paragraph("last") { StyleId = "List" });
        document.Blocks.Add(new Table());
        document.Blocks.Add(new Paragraph("after table") { StyleId = "List" });

        Spacing(document, 0).AfterPt.Should().Be(0);
        Spacing(document, 1).BeforePt.Should().Be(0);
        Spacing(document, 1).AfterPt.Should().Be(10, "the following paragraph has another style");
        Spacing(document, 3).AfterPt.Should().Be(10, "contextual spacing cannot cross a table");
    }

    [Fact]
    public void ExplicitFalseOverridesTheInheritedContextualFlag()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["List"] = new DocumentStyle
        {
            Id = "List", Name = "List", Paragraph = new ParagraphFormatting
            {
                ContextualSpacing = true, SpaceAfterPt = 10, SpaceAfterIsSet = true,
            },
        };
        for (var index = 0; index < 2; index++)
            document.Blocks.Add(new Paragraph("item") { StyleId = "List", Formatting = new ParagraphFormatting
            {
                ContextualSpacing = false,
            } });
        Spacing(document, 0).AfterPt.Should().Be(10);
    }

    [Fact]
    public void OmittedStyleAndExplicitDefaultStyleShareAnIdentity()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles.Clear();
        document.Styles["Default"] = new DocumentStyle
        {
            Id = "Default", Name = "Default", IsDefault = true, Paragraph = new ParagraphFormatting
            {
                ContextualSpacing = true, SpaceAfterPt = 10, SpaceAfterIsSet = true,
            },
        };
        document.Blocks.Add(new Paragraph("implicit"));
        document.Blocks.Add(new Paragraph("explicit") { StyleId = "Default" });
        Spacing(document, 0).AfterPt.Should().Be(0);
    }

    private static DocumentBodyParagraphSpacingPlan Spacing(TextDocument document, int index) =>
        DocumentBodyParagraphLayoutPlanner.Spacing(document, index,
            DocumentParagraphFormattingResolver.Resolve(document, (Paragraph)document.Blocks[index]));
}
