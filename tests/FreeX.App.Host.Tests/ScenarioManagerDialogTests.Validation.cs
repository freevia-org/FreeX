using FluentAssertions;
using FreeX.App.Presentation.ScenarioManager;
using FreeX.App.Services;
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
    [InlineData("merge", ScenarioManagerAction.Merge)]
    public void TryParseAction_MapsLegacyPromptWords(string text, ScenarioManagerAction expected)
    {
        ScenarioManagerPlanner.TryParseAction(text, out var action).Should().BeTrue();

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
    [InlineData(ScenarioManagerAction.Merge, false)]
    public void RequiresScenarioName_OnlyRequiresNamesForSaveActions(ScenarioManagerAction action, bool expected)
    {
        ScenarioManagerDialogPlanner.RequiresScenarioName(action).Should().Be(expected);
    }

    [Fact]
    public void ValidateScenarioName_RejectsBlankName()
    {
        ScenarioManagerDialogPlanner.ValidateScenarioName(" ")
            .Should()
            .Be(ScenarioManagerDialogValidation.Fail(ScenarioManagerDialogValidationError.EnterScenarioName));
    }

    [Fact]
    public void ValidateScenarioName_AcceptsNonBlankName()
    {
        ScenarioManagerDialogPlanner.ValidateScenarioName(" Best Case ")
            .Should()
            .Be(ScenarioManagerDialogValidation.Ok);
    }

    [Fact]
    public void ValidateChangingCells_AllowsBlankToUseCurrentSelectionFallback()
    {
        ScenarioManagerDialogPlanner.ValidateChangingCells(" ", SheetId.New(), _ => null)
            .Should()
            .Be(ScenarioManagerDialogValidation.Ok);
    }

    [Fact]
    public void ValidateChangingCells_RejectsInvalidTypedReference()
    {
        var sheetId = SheetId.New();

        ScenarioManagerDialogPlanner.ValidateChangingCells("not a range", sheetId, _ => null)
            .Should()
            .Be(ScenarioManagerDialogValidation.Fail(
                ScenarioManagerDialogValidationError.EnterValidChangingCellsReference));
    }

    [Fact]
    public void ValidateChangingCells_AcceptsValidTypedReference()
    {
        var sheetId = SheetId.New();

        ScenarioManagerDialogPlanner.ValidateChangingCells(
                "Sheet1!A1:B2",
                sheetId,
                name => name == "Sheet1" ? sheetId : null)
            .Should()
            .Be(ScenarioManagerDialogValidation.Ok);
    }

    [Fact]
    public void ValidateResultCells_AllowsBlankForPlainScenarioSummary()
    {
        ScenarioManagerDialogPlanner.ValidateResultCells(" ", SheetId.New(), _ => null)
            .Should()
            .Be(ScenarioManagerDialogValidation.Ok);
    }

    [Fact]
    public void ValidateResultCells_RejectsInvalidTypedReference()
    {
        var sheetId = SheetId.New();

        ScenarioManagerDialogPlanner.ValidateResultCells("not a range", sheetId, _ => null)
            .Should()
            .Be(ScenarioManagerDialogValidation.Fail(
                ScenarioManagerDialogValidationError.EnterValidResultCellsReference));
    }

    [Fact]
    public void ValidateResultCells_AcceptsValidTypedReference()
    {
        var sheetId = SheetId.New();

        ScenarioManagerDialogPlanner.ValidateResultCells(
                "Sheet1!C1:C2",
                sheetId,
                name => name == "Sheet1" ? sheetId : null)
            .Should()
            .Be(ScenarioManagerDialogValidation.Ok);
    }

    [Fact]
    public void ValidateResultCells_AcceptsCommaSeparatedTypedReferences()
    {
        var sheetId = SheetId.New();
        var resultsSheetId = SheetId.New();

        ScenarioManagerDialogPlanner.ValidateResultCells(
                "B2,Results!D5:E5",
                sheetId,
                name => name == "Results" ? resultsSheetId : null)
            .Should()
            .Be(ScenarioManagerDialogValidation.Ok);
    }
}
