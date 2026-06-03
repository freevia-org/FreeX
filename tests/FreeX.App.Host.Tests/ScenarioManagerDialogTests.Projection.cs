using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class ScenarioManagerDialogTests
{
    [Fact]
    public void ProjectSelectionFields_UsesSelectedScenarioFields()
    {
        var item = new ScenarioManagerItem(
            "Best Case",
            [],
            "Revenue lift",
            "B2:C4",
            Hidden: true,
            Locked: true);

        var state = ScenarioManagerDialog.ProjectSelectionFields(item, currentScenarioNameText: "", defaultScenarioName: "Scenario 2");

        state.Should().NotBeNull();
        state!.ScenarioName.Should().Be("Best Case");
        state.ChangingCellsText.Should().Be("B2:C4");
        state.ResultCellsText.Should().Be("");
        state.CommentText.Should().Be("Revenue lift");
        state.Locked.Should().BeTrue();
        state.Hidden.Should().BeTrue();
    }

    [Fact]
    public void ProjectSelectionFields_ResetsToDefaultWhenSelectionClearedAndNameBlank()
    {
        var state = ScenarioManagerDialog.ProjectSelectionFields(selected: null, currentScenarioNameText: " ", defaultScenarioName: "Scenario 1");

        state.Should().NotBeNull();
        state!.ScenarioName.Should().Be("Scenario 1");
        state.ChangingCellsText.Should().Be("");
        state.ResultCellsText.Should().Be("");
        state.CommentText.Should().Be("");
        state.Locked.Should().BeFalse();
        state.Hidden.Should().BeFalse();
    }

    [Fact]
    public void ProjectSelectionFields_PreservesTypedFieldsWhenSelectionClearedAndNamePresent()
    {
        ScenarioManagerDialog.ProjectSelectionFields(selected: null, currentScenarioNameText: "Draft", defaultScenarioName: "Scenario 1")
            .Should()
            .BeNull();
    }

    [Fact]
    public void ProjectAcceptResult_CapturesSelectedAndEditedFieldValues()
    {
        var selected = new ScenarioManagerItem("Best Case", [], null, "B2", Hidden: false, Locked: false);

        var result = ScenarioManagerDialog.ProjectAcceptResult(
            ScenarioManagerAction.Edit,
            selected,
            newScenarioName: "Better Case",
            changingCellsText: "C3",
            resultCellsText: "D4",
            commentText: "Updated",
            locked: true,
            hidden: true);

        result.Action.Should().Be(ScenarioManagerAction.Edit);
        result.SelectedScenarioName.Should().Be("Best Case");
        result.NewScenarioName.Should().Be("Better Case");
        result.ChangingCellsText.Should().Be("C3");
        result.ResultCellsText.Should().Be("D4");
        result.CommentText.Should().Be("Updated");
        result.Locked.Should().BeTrue();
        result.Hidden.Should().BeTrue();
    }
}
