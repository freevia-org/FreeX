using FluentAssertions;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class ScenarioManagerDialogTests
{
    [Fact]
    public void DialogSource_UsesExcelLikeScenarioListAndSideButtons()
    {
        var source = ReadScenarioManagerDialogSources();

        source.Should().Contain("ListBox");
        source.Should().Contain("UiText.Get(\"ScenarioManager_Add\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_Edit\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_Delete\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_List\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_Show\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_Summary\")");
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

        source.Should().Contain("UiText.Get(\"ScenarioManager_Scenarios\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_ScenarioName\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_ChangingCells\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_ResultCells\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_Comment\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_PreventChanges\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_Hide\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_Add\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_Edit\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_Delete\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_List\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_Show\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_Summary\")");
        source.Should().Contain("Content = UiText.Get(\"ScenarioManager_Close\")");
    }

    [Fact]
    public void DialogSource_ScenarioListExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ScenarioManagerDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_scenarioList, UiText.Get(\"ScenarioManager_Scenarios2\"));");
    }

    [Fact]
    public void DialogSource_FramesAddEditFieldsLikeExcel()
    {
        var source = ReadScenarioManagerDialogSources();

        source.Should().Contain("UiText.Get(\"ScenarioManager_ScenarioName\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_ChangingCells\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_Comment\")");
        source.Should().Contain("UiText.Get(\"ScenarioManager_AddEditScenario\")");
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
        source.Should().Contain("new ScenarioManagerValidationFailure(error ?? UiText.Get(\"ScenarioManager_EnterScenarioDetails\"), ScenarioManagerValidationField.ScenarioName)");
        source.Should().Contain("!TryValidateChangingCells(changingCellsText, currentSheetId, resolveSheetIdByName, out error)");
        source.Should().Contain("new ScenarioManagerValidationFailure(error ?? UiText.Get(\"ScenarioManager_EnterScenarioDetails\"), ScenarioManagerValidationField.ChangingCells)");
        source.Should().Contain("WorkbookRangeTextCodec.TryParseMany(currentSheetId.Value, resultCellsText, resolveSheetIdByName, out _)");
        source.Should().Contain("!TryValidateResultCells(resultCellsText, currentSheetId, resolveSheetIdByName, out error)");
        source.Should().Contain("new ScenarioManagerValidationFailure(error ?? UiText.Get(\"ScenarioManager_EnterScenarioResultCells\"), ScenarioManagerValidationField.ResultCells)");
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

        source.Should().Contain("_scenarioList.MouseDoubleClick += ScenarioList_MouseDoubleClick;");
        source.Should().Contain("_showButton = AddActionButton(sideButtons, UiText.Get(\"ScenarioManager_Show\"), ScenarioManagerAction.Show, isEnabled: _scenarioList.SelectedItem is not null, isDefault: _scenarioList.SelectedItem is not null);");
        source.Should().Contain("private bool AcceptSelectedScenario()");
        source.Should().Contain("Accept(ScenarioManagerAction.Show);");
        source.Should().Contain("private void ScenarioList_MouseDoubleClick(object sender, MouseButtonEventArgs e)");
        source.Should().Contain("e.Handled = true;");
        source.Should().Contain("_showButton.IsDefault = hasSelection;");
    }

    [Fact]
    public void DialogSource_MakesAddTheDefaultActionWhenNoScenariosExist()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ScenarioManagerDialog.cs"));

        source.Should().Contain("private Button? _addButton;");
        source.Should().Contain("_addButton = AddActionButton(sideButtons, UiText.Get(\"ScenarioManager_Add\"), ScenarioManagerAction.Add, isDefault: _scenarioList.Items.Count == 0);");
        source.Should().Contain("_addButton.IsDefault = !hasSelection;");
        source.Should().Contain("_showButton.IsDefault = hasSelection;");
    }
}
