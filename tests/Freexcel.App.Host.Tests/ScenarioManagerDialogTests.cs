using FluentAssertions;
using Freexcel.Core.Model;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class ScenarioManagerDialogTests
{
    [Fact]
    public void BuildScenarioItems_ReturnsWorkbookScenarioNames()
    {
        var workbook = new Workbook("test");
        workbook.Scenarios.Add(new WorkbookScenario("Best Case", []));
        workbook.Scenarios.Add(new WorkbookScenario("Worst Case", []));

        var items = ScenarioManagerDialog.BuildScenarioItems(workbook);

        items.Select(item => item.Name).Should().Equal("Best Case", "Worst Case");
    }

    [Theory]
    [InlineData("save", ScenarioManagerAction.Save)]
    [InlineData("add", ScenarioManagerAction.Add)]
    [InlineData("edit", ScenarioManagerAction.Edit)]
    [InlineData("delete", ScenarioManagerAction.Delete)]
    [InlineData("show", ScenarioManagerAction.Show)]
    [InlineData("list", ScenarioManagerAction.List)]
    [InlineData("report", ScenarioManagerAction.Report)]
    public void TryParseAction_MapsLegacyPromptWords(string text, ScenarioManagerAction expected)
    {
        ScenarioManagerDialog.TryParseAction(text, out var action).Should().BeTrue();

        action.Should().Be(expected);
    }

    [Fact]
    public void DialogSource_UsesExcelLikeScenarioListAndSideButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ScenarioManagerDialog.cs"));

        source.Should().Contain("ListBox");
        source.Should().Contain("Add...");
        source.Should().Contain("Edit...");
        source.Should().Contain("Delete");
        source.Should().Contain("List...");
        source.Should().Contain("Show");
        source.Should().Contain("S_ummary...");
        source.Should().Contain("UpdateSelectionState");
        source.Should().Contain("ScenarioManagerAction.Delete");
        source.Should().Contain("ScenarioManagerAction.List");
        source.Should().NotContain("Merge...");
        source.Should().NotContain("ScenarioManagerAction.List, isEnabled: false");
    }

    [Fact]
    public void DialogSource_ExposesKeyboardAccessKeysForFieldsActionsAndClose()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ScenarioManagerDialog.cs"));

        source.Should().Contain("\"_Scenarios:\"");
        source.Should().Contain("\"Scenario _name:\"");
        source.Should().Contain("\"Changing _cells:\"");
        source.Should().Contain("\"_Comment:\"");
        source.Should().Contain("\"_Add...\"");
        source.Should().Contain("\"_Edit...\"");
        source.Should().Contain("\"_Delete\"");
        source.Should().Contain("\"_List...\"");
        source.Should().Contain("\"_Show\"");
        source.Should().Contain("\"S_ummary...\"");
        source.Should().Contain("Content = \"_Close\"");
    }

    [Fact]
    public void DialogSource_FramesAddEditFieldsLikeExcel()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ScenarioManagerDialog.cs"));

        source.Should().Contain("Scenario _name:");
        source.Should().Contain("Changing _cells:");
        source.Should().Contain("Comment:");
        source.Should().Contain("Add/Edit Scenario");
        source.Should().Contain("_newNameBox.Text = selected.Name");
    }

    [Fact]
    public void DialogSource_ReturnsChangingCellsAndCommentFields()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ScenarioManagerDialog.cs"));
        var handlerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("public string? ChangingCellsText");
        source.Should().Contain("public string? CommentText");
        source.Should().Contain("ChangingCellsText = _changingCellsBox.Text");
        source.Should().Contain("CommentText = _commentBox.Text");
        handlerSource.Should().Contain("SaveScenarioFromDialog(dialog.NewScenarioName, dialog.ChangingCellsText, dialog.CommentText)");
        handlerSource.Should().Contain("TryParseScenarioChangingCells");
    }
}
