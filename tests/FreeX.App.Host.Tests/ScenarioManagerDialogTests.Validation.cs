using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ScenarioManagerDialogTests
{
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

    [Theory]
    [InlineData(ScenarioManagerAction.Add, true)]
    [InlineData(ScenarioManagerAction.Edit, true)]
    [InlineData(ScenarioManagerAction.Save, true)]
    [InlineData(ScenarioManagerAction.Show, false)]
    [InlineData(ScenarioManagerAction.Delete, false)]
    [InlineData(ScenarioManagerAction.List, false)]
    [InlineData(ScenarioManagerAction.Report, false)]
    public void RequiresScenarioName_OnlyRequiresNamesForSaveActions(ScenarioManagerAction action, bool expected)
    {
        ScenarioManagerDialog.RequiresScenarioName(action).Should().Be(expected);
    }

    [Fact]
    public void TryValidateScenarioName_RejectsBlankName()
    {
        ScenarioManagerDialog.TryValidateScenarioName(" ", out var error)
            .Should()
            .BeFalse();

        error.Should().Be("Enter a scenario name.");
    }

    [Fact]
    public void TryValidateScenarioName_AcceptsNonBlankName()
    {
        ScenarioManagerDialog.TryValidateScenarioName(" Best Case ", out var error)
            .Should()
            .BeTrue(error);
    }

    [Fact]
    public void TryValidateChangingCells_AllowsBlankToUseCurrentSelectionFallback()
    {
        ScenarioManagerDialog.TryValidateChangingCells(" ", SheetId.New(), _ => null, out var error)
            .Should()
            .BeTrue(error);
    }

    [Fact]
    public void TryValidateChangingCells_RejectsInvalidTypedReference()
    {
        var sheetId = SheetId.New();

        ScenarioManagerDialog.TryValidateChangingCells("not a range", sheetId, _ => null, out var error)
            .Should()
            .BeFalse();

        error.Should().Be("Enter a valid changing cells reference.");
    }

    [Fact]
    public void TryValidateChangingCells_AcceptsValidTypedReference()
    {
        var sheetId = SheetId.New();

        ScenarioManagerDialog.TryValidateChangingCells("Sheet1!A1:B2", sheetId, name => name == "Sheet1" ? sheetId : null, out var error)
            .Should()
            .BeTrue(error);
    }

    [Fact]
    public void TryValidateResultCells_AllowsBlankForPlainScenarioSummary()
    {
        ScenarioManagerDialog.TryValidateResultCells(" ", SheetId.New(), _ => null, out var error)
            .Should()
            .BeTrue(error);
    }

    [Fact]
    public void TryValidateResultCells_RejectsInvalidTypedReference()
    {
        var sheetId = SheetId.New();

        ScenarioManagerDialog.TryValidateResultCells("not a range", sheetId, _ => null, out var error)
            .Should()
            .BeFalse();

        error.Should().Be("Enter a valid result cells reference.");
    }

    [Fact]
    public void TryValidateResultCells_AcceptsValidTypedReference()
    {
        var sheetId = SheetId.New();

        ScenarioManagerDialog.TryValidateResultCells("Sheet1!C1:C2", sheetId, name => name == "Sheet1" ? sheetId : null, out var error)
            .Should()
            .BeTrue(error);
    }

    [Fact]
    public void TryValidateResultCells_AcceptsCommaSeparatedTypedReferences()
    {
        var sheetId = SheetId.New();
        var resultsSheetId = SheetId.New();

        ScenarioManagerDialog.TryValidateResultCells(
                "B2,Results!D5:E5",
                sheetId,
                name => name == "Results" ? resultsSheetId : null,
                out var error)
            .Should()
            .BeTrue(error);
    }
}
