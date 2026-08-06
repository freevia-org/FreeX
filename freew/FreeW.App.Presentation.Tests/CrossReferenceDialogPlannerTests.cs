using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class CrossReferenceDialogPlannerTests
{
    [Fact]
    public void BuildTypeChoices_UsesWordOrderAndFriendlyLabels()
    {
        var choices = CrossReferenceDialogPlanner.BuildTypeChoices();

        choices.Select(choice => choice.Type).Should().Equal(
            CrossRefType.Heading,
            CrossRefType.Bookmark,
            CrossRefType.Figure,
            CrossRefType.Table,
            CrossRefType.Equation,
            CrossRefType.Footnote,
            CrossRefType.Endnote,
            CrossRefType.NumberedItem);
        choices.Last().Label.Should().Be("Numbered item");
    }

    [Fact]
    public void BuildInsertAsChoices_UsesModelOptionsAndPreservesPreviousSelection()
    {
        var choices = CrossReferenceDialogPlanner.BuildInsertAsChoices(CrossRefType.Heading);

        choices.Select(choice => choice.InsertAs).Should().Equal(
            CrossRefInsertAs.Text,
            CrossRefInsertAs.PageNumber,
            CrossRefInsertAs.HeadingNumber,
            CrossRefInsertAs.AboveBelow);
        choices.Select(choice => choice.Label).Should().Equal(
            "Text",
            "Page number",
            "Heading number",
            "Above/below");

        CrossReferenceDialogPlanner.PreserveInsertAsSelection(choices, CrossRefInsertAs.PageNumber)
            .Should().Be(1);
        CrossReferenceDialogPlanner.PreserveInsertAsSelection(choices, CrossRefInsertAs.ParagraphNumber)
            .Should().Be(0);
    }

    [Fact]
    public void BuildInsertAsChoices_UsesWordsCaptionLabelsAndOrder()
    {
        var choices = CrossReferenceDialogPlanner.BuildInsertAsChoices(CrossRefType.Figure);

        choices.Select(choice => choice.InsertAs).Should().Equal(
            CrossRefInsertAs.Text,
            CrossRefInsertAs.CaptionLabelAndNumber,
            CrossRefInsertAs.CaptionText,
            CrossRefInsertAs.PageNumber,
            CrossRefInsertAs.AboveBelow);
        choices.Select(choice => choice.Label).Should().Equal(
            "Entire caption",
            "Only label and number",
            "Only caption text",
            "Page number",
            "Above/below");
    }

    [Fact]
    public void TryCreateChoice_SelectsRequestedTargetIndexInsteadOfDefaultingToFirstHeading()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("First") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph("Second") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph("Body"));

        var targets = CrossReferenceDialogPlanner.BuildTargetChoices(document, CrossRefType.Heading);

        targets.Select(target => target.Label).Should().Equal("First", "Second");

        CrossReferenceDialogPlanner.TryCreateChoice(
                document,
                CrossRefType.Heading,
                CrossRefInsertAs.PageNumber,
                selectedTargetIndex: 1,
                hyperlink: false,
                out var choice)
            .Should().BeTrue();

        choice.Should().NotBeNull();
        choice!.Target.Display.Should().Be("Second");
        choice.InsertAs.Should().Be(CrossRefInsertAs.PageNumber);
        choice.Hyperlink.Should().BeFalse();
    }

    [Fact]
    public void TryCreateChoice_RejectsMissingTargetSelection()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Body"));

        CrossReferenceDialogPlanner.TryCreateChoice(
                document,
                CrossRefType.Heading,
                CrossRefInsertAs.Text,
                selectedTargetIndex: 0,
                hyperlink: true,
                out var choice)
            .Should().BeFalse();

        choice.Should().BeNull();
    }
}
