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
            "Equations and Formulas",
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
            "Revision Number (REVNUM)",
            "Edit Time (EDITTIME)");

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

        FieldPickerDialogPlanner.TryGetInstruction(
                "Document Information",
                "Edit Time (EDITTIME)",
                out instruction)
            .Should().BeTrue();
        instruction.Should().Be(" EDITTIME ");

        var formulaChoices = FieldPickerDialogPlanner.ChoicesForCategory("Equations and Formulas");
        formulaChoices.Should().ContainSingle(choice => choice.Label == "Formula (=)");
        FieldPickerDialogPlanner.TryGetInstruction(
                "Equations and Formulas",
                "Formula (=)",
                out instruction)
            .Should().BeTrue();
        instruction.Should().Be(" =2*(3+4) \\# \"0.00\" ");

        var dateChoices = FieldPickerDialogPlanner.ChoicesForCategory("Date and Time");
        dateChoices.Select(choice => choice.Label).Should().ContainInOrder(
            "Date (DATE)",
            "Time (TIME)",
            "Print Date (PRINTDATE)");
        FieldPickerDialogPlanner.TryGetInstruction(
                "Date and Time",
                "Print Date (PRINTDATE)",
                out instruction)
            .Should().BeTrue();
        instruction.Should().Be(" PRINTDATE \\@ \"M/d/yyyy h:mm am/pm\" ");

        var numberingChoices = FieldPickerDialogPlanner.ChoicesForCategory("Numbering");
        numberingChoices.Select(choice => choice.Label).Should().ContainInOrder(
            "Page Number (PAGE)",
            "Number of Pages (NUMPAGES)",
            "Section Number (SECTION)",
            "Number of Section Pages (SECTIONPAGES)");
        FieldPickerDialogPlanner.TryGetInstruction(
                "Numbering",
                "Number of Section Pages (SECTIONPAGES)",
                out instruction)
            .Should().BeTrue();
        instruction.Should().Be(" SECTIONPAGES ");
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
