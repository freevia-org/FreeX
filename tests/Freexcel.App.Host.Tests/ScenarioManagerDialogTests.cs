using FluentAssertions;
using Freexcel.Core.Model;
using System.IO;
using System.Reflection;
using System.Windows.Controls;

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
    public void DialogSource_FramesAddEditFieldsLikeExcel()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ScenarioManagerDialog.cs"));

        source.Should().Contain("Scenario _name:");
        source.Should().Contain("Changing _cells:");
        source.Should().Contain("Comment:");
        source.Should().Contain("Add/Edit Scenario");
        source.Should().Contain("_newNameBox.Text = selected.Name");
        source.Should().Contain("_changingCellsBox.Text = selected.ChangingCellsText");
        source.Should().Contain("_commentBox.Text = selected.Comment ?? \"\"");
        source.Should().Contain("_lockedBox.IsChecked = selected.Locked");
        source.Should().Contain("_hiddenBox.IsChecked = selected.Hidden");
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ScenarioManagerDialog.cs"));
        var handlerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.ScenarioCommands.cs"));

        source.Should().Contain("public string? ChangingCellsText");
        source.Should().Contain("public string? CommentText");
        source.Should().Contain("public bool ScenarioHidden");
        source.Should().Contain("public bool ScenarioLocked");
        source.Should().Contain("ChangingCellsText = _changingCellsBox.Text");
        source.Should().Contain("CommentText = _commentBox.Text");
        source.Should().Contain("ScenarioLocked = _lockedBox.IsChecked == true");
        source.Should().Contain("ScenarioHidden = _hiddenBox.IsChecked == true");
        source.Should().Contain("if (RequiresScenarioName(action) && !TryValidateScenarioName(_newNameBox.Text, out var error))");
        source.Should().Contain("ShowInvalidInputWarning(error ?? \"Enter scenario details.\", _newNameBox);");
        source.Should().Contain("!TryValidateChangingCells(_changingCellsBox.Text, _currentSheetId, _resolveSheetIdByName, out error)");
        source.Should().Contain("ShowInvalidInputWarning(error ?? \"Enter scenario details.\", _changingCellsBox);");
        source.Should().Contain("MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);");
        source.Should().Contain("target.SelectAll();");
        handlerSource.Should().Contain("new ScenarioManagerDialog(_workbook, _currentSheetId, ResolveSheetIdByName)");
        handlerSource.Should().Contain("dialog.ScenarioHidden");
        handlerSource.Should().Contain("dialog.ScenarioLocked");
        handlerSource.Should().Contain("new SaveScenarioCommand(name, changes, comment, hidden, locked)");
        handlerSource.Should().Contain("TryParseScenarioChangingCells");
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesScenarioListOrNewNameField()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ScenarioManagerDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_scenarioList.Items.Count > 0 ? _scenarioList : _newNameBox");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    private static T GetField<T>(ScenarioManagerDialog dialog, string fieldName)
        where T : class
    {
        var field = typeof(ScenarioManagerDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }
}
