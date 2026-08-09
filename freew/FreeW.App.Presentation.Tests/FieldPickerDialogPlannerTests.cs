using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class FieldPickerDialogPlannerTests
{
    [Fact]
    public void Categories_PreserveDialogOrder()
    {
        FieldPickerDialogPlanner.Categories.Should().Equal(
            "Date and Time",
            "Document Information",
            "Numbering",
            "References");
    }

    [Fact]
    public void ChoicesForCategory_ReturnsLabelsAndInstructions()
    {
        var choices = FieldPickerDialogPlanner.ChoicesForCategory("Document Information");

        choices.Select(choice => choice.Label).Should().ContainInOrder(
            "Author (AUTHOR)",
            "File Name (FILENAME)",
            "Title (TITLE)",
            "Subject (SUBJECT)",
            "Keywords (KEYWORDS)",
            "Comments (COMMENTS)",
            "Template (TEMPLATE)",
            "Revision Number (REVNUM)");

        FieldPickerDialogPlanner.TryGetInstruction(
                "Document Information",
                "Title (TITLE)",
                out var instruction)
            .Should().BeTrue();

        instruction.Should().Be(" TITLE ");

        FieldPickerDialogPlanner.TryGetInstruction(
                "Document Information",
                "Template (TEMPLATE)",
                out instruction)
            .Should().BeTrue();
        instruction.Should().Be(" TEMPLATE ");

        FieldPickerDialogPlanner.TryGetInstruction(
                "Document Information",
                "Revision Number (REVNUM)",
                out instruction)
            .Should().BeTrue();
        instruction.Should().Be(" REVNUM ");
    }

    [Fact]
    public void TryGetInstruction_RejectsUnknownCategoryOrLabel()
    {
        FieldPickerDialogPlanner.TryGetInstruction("Missing", "Title", out var instruction)
            .Should().BeFalse();

        instruction.Should().BeEmpty();
    }

    [Fact]
    public void DocumentPropertyFieldCommandPlans_MapRibbonIdsToRunFieldKinds()
    {
        DocumentPropertyFieldPlanner.CommandPlans
            .Should().Equal(
                new DocumentPropertyFieldCommandPlan("freew.docprop-title", RunFieldKind.Title),
                new DocumentPropertyFieldCommandPlan("freew.docprop-subject", RunFieldKind.Subject),
                new DocumentPropertyFieldCommandPlan("freew.docprop-author", RunFieldKind.Author),
                new DocumentPropertyFieldCommandPlan("freew.docprop-keywords", RunFieldKind.Keywords),
                new DocumentPropertyFieldCommandPlan("freew.docprop-comments", RunFieldKind.DocComments));
    }
}
