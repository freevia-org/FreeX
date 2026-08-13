using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotCalculatedItemSessionTests
{
    [Fact]
    public void Create_ProjectsLayoutFieldsAndRefreshesExistingNamesWithSelection()
    {
        var pivot = PivotWithLayout();
        pivot.CalculatedItems.AddRange([
            new PivotCalculatedItemModel(0, "North", "Region=North"),
            new PivotCalculatedItemModel(1, "Hardware", "Product=Hardware")
        ]);
        var session = PivotCalculatedItemSession.Create(pivot, ["Region", "Product", "Value"]);

        session.Fields.Select(field => (field.SourceFieldIndex, field.Caption)).Should().Equal(
            (0, "Region"),
            (1, "Product"));
        session.ExistingNames.Should().Equal("North");
        session.SelectExisting("north").Should().Be(new PivotCalculatedDraft("North", "Region=North"));

        session.SelectSourceField(1, startNew: true).Should().Be(new PivotCalculatedDraft("", "= "));
        session.ExistingNames.Should().Equal("Hardware");
        session.SelectedSourceField!.Caption.Should().Be("Product");
    }

    [Fact]
    public void CreateDraft_PreservesSourceIndexesAndRefreshesInsertableItems()
    {
        var session = PivotCalculatedItemSession.CreateDraft(
            [" Region ", "", " Product "],
            selectedSourceFieldIndex: 2,
            name: "Bundle",
            formula: "East+West",
            itemNamesBySourceFieldIndex: new Dictionary<int, IEnumerable<string>>
            {
                [0] = [" East ", "east", "West"],
                [2] = ["Hardware", " Software "]
            });

        session.Fields.Select(field => (field.SourceFieldIndex, field.Caption)).Should().Equal(
            (0, "Region"),
            (2, "Product"));
        session.SelectedSourceFieldIndex.Should().Be(2);
        session.ItemReferences.Should().Equal("Hardware", "Software");

        session.SelectSourceField(0);
        session.ItemReferences.Should().Equal("East", "West");
        session.Draft.Should().Be(new PivotCalculatedDraft("Bundle", "East+West"));
    }

    [Fact]
    public void EmptyLayout_ExposesLocalizedOpenAndSaveIssues()
    {
        var text = PivotCalculatedItemSessionText.Default with
        {
            NoSourceFieldMessage = "add layout field"
        };
        var session = PivotCalculatedItemSession.Create(
            new PivotTableModel { Name = "P" },
            ["Region"],
            text);

        session.OpenIssue.Should().Be(new PivotCalculatedWorkflowIssue(
            PivotCalculatedInputTarget.SourceField,
            "add layout field"));
        session.PlanSave("North", "Region=North").Issue.Should().Be(session.OpenIssue);
    }

    [Fact]
    public void PlanSave_MapsNameAndFormulaErrorsToInputTargets()
    {
        var session = PivotCalculatedItemSession.Create(PivotWithLayout(), ["Region", "Product"]);

        session.PlanSave(" ", "Region=North").Issue!.Target.Should().Be(PivotCalculatedInputTarget.Name);
        session.PlanSave("North", " ").Issue!.Target.Should().Be(PivotCalculatedInputTarget.Formula);
    }

    [Fact]
    public void SaveCommit_ReplacesOnlyTheSelectedFieldItemAndFormatsStatus()
    {
        var pivot = PivotWithLayout();
        pivot.CalculatedItems.AddRange([
            new PivotCalculatedItemModel(0, "North", "old"),
            new PivotCalculatedItemModel(1, "North", "Product=North")
        ]);
        var text = PivotCalculatedItemSessionText.Default with { SavedStatusFormat = "saved:{0}" };
        var session = PivotCalculatedItemSession.Create(pivot, ["Region", "Product"], text);

        var commit = session.Commit(session.PlanSave(" north ", " Region=North ").Submission!);

        commit.Success.Should().BeTrue();
        commit.CalculatedItems.Should().HaveCount(2);
        commit.CalculatedItems[0].Should().Be(new PivotCalculatedItemModel(0, "north", "Region=North"));
        commit.CalculatedItems[1].Should().Be(new PivotCalculatedItemModel(1, "North", "Product=North"));
        commit.Status.Should().Be("saved:north");
    }

    [Fact]
    public void DeleteCommit_UsesSelectedFieldAndOwnsMissingItemError()
    {
        var pivot = PivotWithLayout();
        pivot.CalculatedItems.AddRange([
            new PivotCalculatedItemModel(0, "North", "Region=North"),
            new PivotCalculatedItemModel(1, "North", "Product=North")
        ]);
        var text = PivotCalculatedItemSessionText.Default with
        {
            NoItemToDeleteMessage = "choose item",
            DeletedStatusFormat = "deleted:{0}"
        };
        var session = PivotCalculatedItemSession.Create(pivot, ["Region", "Product"], text);

        session.PlanDelete(" ").Issue!.Message.Should().Be("choose item");
        var removed = session.Commit(session.PlanDelete("North").Submission!);
        removed.CalculatedItems.Should().ContainSingle(item => item.SourceFieldIndex == 1);
        removed.Status.Should().Be("deleted:North");

        session.SelectSourceField(1);
        var missing = session.Commit(session.PlanDelete("Other").Submission!);
        missing.Success.Should().BeFalse();
        missing.Issue!.Message.Should().Be("choose item");
    }

    private static PivotTableModel PivotWithLayout()
    {
        var pivot = new PivotTableModel { Name = "P" };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(0));
        return pivot;
    }
}
