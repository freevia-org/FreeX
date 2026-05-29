using FluentAssertions;
using FreeX.Core.Model;
using System.IO;
using System.Reflection;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

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

    [Fact]
    public void BuildScenarioItems_IncludesChangingCellsAndCommentForEditing()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var first = new CellAddress(sheet.Id, 2, 2);
        var second = new CellAddress(sheet.Id, 4, 3);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [
                new ScenarioCellValue(first, new NumberValue(10)),
                new ScenarioCellValue(second, new NumberValue(20))
            ],
            "Revenue lift",
            Hidden: true,
            Locked: true));

        var item = ScenarioManagerDialog.BuildScenarioItems(workbook).Single();

        item.Name.Should().Be("Best Case");
        item.ChangingCellsText.Should().Be("B2:C4");
        item.Comment.Should().Be("Revenue lift");
        item.Hidden.Should().BeTrue();
        item.Locked.Should().BeTrue();
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

    [Fact]
    public void DialogSource_UsesExcelLikeScenarioListAndSideButtons()
    {
        var source = ReadScenarioManagerDialogSources();

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
        var source = ReadScenarioManagerDialogSources();

        source.Should().Contain("\"_Scenarios:\"");
        source.Should().Contain("\"Scenario _name:\"");
        source.Should().Contain("\"Changing _cells:\"");
        source.Should().Contain("\"_Result cells:\"");
        source.Should().Contain("\"_Comment:\"");
        source.Should().Contain("\"_Prevent changes\"");
        source.Should().Contain("\"_Hide\"");
        source.Should().Contain("\"_Add...\"");
        source.Should().Contain("\"_Edit...\"");
        source.Should().Contain("\"_Delete\"");
        source.Should().Contain("\"_List...\"");
        source.Should().Contain("\"_Show\"");
        source.Should().Contain("\"S_ummary...\"");
        source.Should().Contain("Content = \"_Close\"");
    }

    [Fact]
    public void DialogSource_ScenarioListExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ScenarioManagerDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_scenarioList, \"Scenarios\");");
    }

    [Fact]
    public void DialogSource_FramesAddEditFieldsLikeExcel()
    {
        var source = ReadScenarioManagerDialogSources();

        source.Should().Contain("Scenario _name:");
        source.Should().Contain("Changing _cells:");
        source.Should().Contain("Comment:");
        source.Should().Contain("Add/Edit Scenario");
        source.Should().Contain("ProjectSelectionFields(selected, _newNameBox.Text, _defaultScenarioName)");
        source.Should().Contain("ApplySelectionFields(fields)");
        source.Should().Contain("selected.Name");
        source.Should().Contain("selected.ChangingCellsText");
        source.Should().Contain("ResultCellsText: \"\"");
        source.Should().Contain("selected.Comment ?? \"\"");
        source.Should().Contain("selected.Locked");
        source.Should().Contain("selected.Hidden");
    }

    [Fact]
    public void SelectingScenario_PopulatesEditFields()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("test");
            var sheet = workbook.AddSheet("Sheet1");
            workbook.Scenarios.Add(new WorkbookScenario(
                "Best Case",
                [
                    new ScenarioCellValue(new CellAddress(sheet.Id, 2, 2), new NumberValue(10)),
                    new ScenarioCellValue(new CellAddress(sheet.Id, 3, 4), new NumberValue(20))
                ],
                "Use growth plan"));

            var dialog = new ScenarioManagerDialog(workbook, sheet.Id, name => name == sheet.Name ? sheet.Id : null);
            try
            {
                GetField<TextBox>(dialog, "_newNameBox").Text.Should().Be("Best Case");
                GetField<TextBox>(dialog, "_changingCellsBox").Text.Should().Be("B2:D3");
                GetField<TextBox>(dialog, "_commentBox").Text.Should().Be("Use growth plan");
                GetField<CheckBox>(dialog, "_lockedBox").IsChecked.Should().BeFalse();
                GetField<CheckBox>(dialog, "_hiddenBox").IsChecked.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogSource_ReturnsChangingCellsAndCommentFields()
    {
        var source = ReadScenarioManagerDialogSources();
        var handlerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ScenarioCommands.cs"));

        source.Should().Contain("public string? ChangingCellsText");
        source.Should().Contain("public string? ResultCellsText");
        source.Should().Contain("public string? CommentText");
        source.Should().Contain("public bool ScenarioHidden");
        source.Should().Contain("public bool ScenarioLocked");
        source.Should().Contain("ProjectAcceptResult(");
        source.Should().Contain("_changingCellsBox.Text");
        source.Should().Contain("_resultCellsBox.Text");
        source.Should().Contain("_commentBox.Text");
        source.Should().Contain("_lockedBox.IsChecked == true");
        source.Should().Contain("_hiddenBox.IsChecked == true");
        source.Should().Contain("ChangingCellsText = result.ChangingCellsText");
        source.Should().Contain("ResultCellsText = result.ResultCellsText");
        source.Should().Contain("CommentText = result.CommentText");
        source.Should().Contain("ScenarioLocked = result.Locked");
        source.Should().Contain("ScenarioHidden = result.Hidden");
        source.Should().Contain("ValidateAcceptRequest(");
        source.Should().Contain("!TryValidateScenarioName(scenarioName, out var error)");
        source.Should().Contain("new ScenarioManagerValidationFailure(error ?? \"Enter scenario details.\", ScenarioManagerValidationField.ScenarioName)");
        source.Should().Contain("!TryValidateChangingCells(changingCellsText, currentSheetId, resolveSheetIdByName, out error)");
        source.Should().Contain("new ScenarioManagerValidationFailure(error ?? \"Enter scenario details.\", ScenarioManagerValidationField.ChangingCells)");
        source.Should().Contain("WorkbookRangeTextCodec.TryParseMany(currentSheetId.Value, resultCellsText, resolveSheetIdByName, out _)");
        source.Should().Contain("!TryValidateResultCells(resultCellsText, currentSheetId, resolveSheetIdByName, out error)");
        source.Should().Contain("new ScenarioManagerValidationFailure(error ?? \"Enter scenario result cells.\", ScenarioManagerValidationField.ResultCells)");
        source.Should().Contain("GetValidationTarget(failure.Field)");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title);");
        source.Should().Contain("target.SelectAll();");
        handlerSource.Should().Contain("new ScenarioManagerDialog(_workbook, _currentSheetId, ResolveSheetIdByName)");
        handlerSource.Should().Contain("dialog.ScenarioHidden");
        handlerSource.Should().Contain("dialog.ScenarioLocked");
        handlerSource.Should().Contain("dialog.ResultCellsText");
        handlerSource.Should().Contain("new ScenarioSummaryReportCommand(");
        handlerSource.Should().Contain("ParseScenarioResultCells(resultCellsText)");
        handlerSource.Should().Contain("WorkbookRangeTextCodec.TryParseMany(_currentSheetId, resultCellsText, ResolveSheetIdByName, out var ranges)");
        handlerSource.Should().Contain("ranges.SelectMany(range => range.AllCells()).Distinct().ToList()");
        handlerSource.Should().Contain("if (workbook.CalculationMode == WorkbookCalculationMode.Automatic)");
        handlerSource.Should().Contain("_recalcEngine.Recalculate(workbook, changedCells)");
        handlerSource.Should().Contain("dialog.SelectedAction == ScenarioManagerAction.Edit ? dialog.SelectedScenarioName : null");
        handlerSource.Should().Contain("new SaveScenarioCommand(name, changes, comment, hidden, locked, replaceScenarioName)");
        handlerSource.Should().Contain("TryParseScenarioChangingCells");
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesScenarioListOrNewNameField()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ScenarioManagerDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_scenarioList.Items.Count > 0 ? _scenarioList : _newNameBox");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void DialogSource_MakesShowTheDefaultSelectedScenarioAction()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ScenarioManagerDialog.cs"));

        source.Should().Contain("_scenarioList.MouseDoubleClick += (_, _) => AcceptSelectedScenario();");
        source.Should().Contain("_showButton = AddActionButton(sideButtons, \"_Show\", ScenarioManagerAction.Show, isEnabled: _scenarioList.SelectedItem is not null, isDefault: _scenarioList.SelectedItem is not null);");
        source.Should().Contain("private void AcceptSelectedScenario()");
        source.Should().Contain("Accept(ScenarioManagerAction.Show);");
        source.Should().Contain("_showButton.IsDefault = hasSelection;");
    }

    [Fact]
    public void DialogSource_MakesAddTheDefaultActionWhenNoScenariosExist()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ScenarioManagerDialog.cs"));

        source.Should().Contain("private Button? _addButton;");
        source.Should().Contain("_addButton = AddActionButton(sideButtons, \"_Add...\", ScenarioManagerAction.Add, isDefault: _scenarioList.Items.Count == 0);");
        source.Should().Contain("_addButton.IsDefault = !hasSelection;");
        source.Should().Contain("_showButton.IsDefault = hasSelection;");
    }

    private static T GetField<T>(ScenarioManagerDialog dialog, string fieldName)
        where T : class
    {
        var field = typeof(ScenarioManagerDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static string ReadScenarioManagerDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ScenarioManagerDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ScenarioManagerDialog.Planning.cs")));
}
