using Free.Shared.Ribbon;
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
                new DocumentPropertyFieldCommandPlan(
                    "freew.docprop-title", "freew.quick-parts.title", "Document Property: Title", "T", RunFieldKind.Title),
                new DocumentPropertyFieldCommandPlan(
                    "freew.docprop-subject", "freew.quick-parts.subject", "Document Property: Subject", "S", RunFieldKind.Subject),
                new DocumentPropertyFieldCommandPlan(
                    "freew.docprop-author", "freew.quick-parts.author", "Document Property: Author", "A", RunFieldKind.Author),
                new DocumentPropertyFieldCommandPlan(
                    "freew.docprop-keywords", "freew.quick-parts.keywords", "Document Property: Keywords", "K", RunFieldKind.Keywords),
                new DocumentPropertyFieldCommandPlan(
                    "freew.docprop-comments", "freew.quick-parts.comments", "Document Property: Comments", "C", RunFieldKind.DocComments));
    }

    [Fact]
    public void DocumentPropertyFieldCommands_RegisterCanonicalAndLegacyIdsToOneSharedAction()
    {
        var registry = new RibbonCommandRegistry();
        RunFieldKind? inserted = null;

        DocumentPropertyFieldPlanner.RegisterCommands(registry, kind => inserted = kind);

        foreach (var plan in DocumentPropertyFieldPlanner.CommandPlans)
        {
            registry.TryGet(plan.CommandId, out var canonical).Should().BeTrue();
            registry.TryGet(plan.LegacyCommandId, out var legacy).Should().BeTrue();
            legacy.Should().BeSameAs(canonical);
            canonical!.Execute(RibbonCommandContext.Empty);
            inserted.Should().Be(plan.Kind);
        }
    }
}
