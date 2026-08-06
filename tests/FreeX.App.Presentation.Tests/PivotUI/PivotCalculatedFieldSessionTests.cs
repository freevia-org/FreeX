using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotCalculatedFieldSessionTests
{
    [Fact]
    public void Create_ProjectsListsAndSelectionOwnsTheDraft()
    {
        var pivot = PivotWith(
            new PivotCalculatedFieldModel("Margin", "Revenue-Cost"),
            new PivotCalculatedFieldModel("Tax", "Revenue*0.1"));
        var session = PivotCalculatedFieldSession.Create(
            pivot,
            [" Revenue ", "", "Cost", "revenue"]);

        session.ExistingNames.Should().Equal("Margin", "Tax");
        session.FieldReferences.Should().Equal("Revenue", "Cost");

        session.SelectExisting("margin").Should().Be(new PivotCalculatedDraft("Margin", "Revenue-Cost"));
        session.SelectedExistingName.Should().Be("Margin");
        session.SelectExisting(null).Should().Be(new PivotCalculatedDraft("", "= "));
        session.SelectedExistingName.Should().BeNull();
    }

    [Fact]
    public void PlanSave_MapsValidationToLocalizedIssueAndInputTarget()
    {
        var text = PivotCalculatedFieldSessionText.Default with
        {
            EmptyNameMessage = "localized name",
            EmptyFormulaMessage = "localized formula"
        };
        var session = PivotCalculatedFieldSession.CreateDraft("", "", [], text);

        var missingName = session.PlanSave(" ", "=Revenue");
        missingName.Success.Should().BeFalse();
        missingName.Issue.Should().Be(new PivotCalculatedWorkflowIssue(
            PivotCalculatedInputTarget.Name,
            "localized name"));

        var missingFormula = session.PlanSave("Margin", " ");
        missingFormula.Issue.Should().Be(new PivotCalculatedWorkflowIssue(
            PivotCalculatedInputTarget.Formula,
            "localized formula"));
    }

    [Fact]
    public void SaveCommit_ReplacesInPlaceAndFormatsStatus()
    {
        var pivot = PivotWith(
            new PivotCalculatedFieldModel("Margin", "old"),
            new PivotCalculatedFieldModel("Tax", "Revenue*0.1"));
        var text = PivotCalculatedFieldSessionText.Default with
        {
            SavedStatusFormat = "saved:{0}"
        };
        var session = PivotCalculatedFieldSession.Create(pivot, ["Revenue"], text);

        var submission = session.PlanSave(" margin ", " Revenue-Cost ").Submission!;
        var commit = session.Commit(submission);

        commit.Success.Should().BeTrue();
        commit.CalculatedFields.Select(field => (field.Name, field.Formula)).Should().Equal(
            ("margin", "Revenue-Cost"),
            ("Tax", "Revenue*0.1"));
        commit.Status.Should().Be("saved:margin");
    }

    [Fact]
    public void DeletePlanningAndCommit_OwnMissingSelectionAndMissingModelErrors()
    {
        var pivot = PivotWith(new PivotCalculatedFieldModel("Margin", "Revenue-Cost"));
        var text = PivotCalculatedFieldSessionText.Default with
        {
            NoFieldToDeleteMessage = "choose field",
            DeletedStatusFormat = "deleted:{0}"
        };
        var session = PivotCalculatedFieldSession.Create(pivot, [], text);

        session.PlanDelete(" ").Issue.Should().Be(new PivotCalculatedWorkflowIssue(
            PivotCalculatedInputTarget.Name,
            "choose field"));

        var removed = session.Commit(session.PlanDelete(" margin ").Submission!);
        removed.Success.Should().BeTrue();
        removed.CalculatedFields.Should().BeEmpty();
        removed.Status.Should().Be("deleted:margin");

        var missing = session.Commit(session.PlanDelete("Other").Submission!);
        missing.Success.Should().BeFalse();
        missing.Issue!.Message.Should().Be("choose field");
        missing.CalculatedFields.Should().ContainSingle();
    }

    [Fact]
    public void InsertReference_UpdatesFormulaStateAndReturnsCaret()
    {
        var session = PivotCalculatedFieldSession.CreateDraft("Margin", "= XX + 1", ["Revenue"]);

        var inserted = session.InsertReference("Revenue", 2, 2);

        inserted.Should().Be(("= Revenue + 1", 2 + "Revenue".Length));
        session.Formula.Should().Be("= Revenue + 1");
    }

    private static PivotTableModel PivotWith(params PivotCalculatedFieldModel[] fields)
    {
        var pivot = new PivotTableModel { Name = "P" };
        pivot.CalculatedFields.AddRange(fields);
        return pivot;
    }
}
