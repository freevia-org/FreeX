using FluentAssertions;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class SymbolPickerDialogSourceTests
{
    [Fact]
    public void Dialog_ExposesKeyboardAccessKeysForInsertAndCancel()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("Content = UiText.Get(\"SymbolPicker_InsertButton\")");
        source.Should().Contain("Content = UiText.Cancel");
    }

    [Fact]
    public void Dialog_ExposesExcelLikeSymbolSelectionAffordances()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("Content = UiText.Get(\"SymbolPicker_FontLabel\")");
        source.Should().Contain("Content = UiText.Get(\"SymbolPicker_SubsetLabel\")");
        source.Should().Contain("UiText.Get(\"SymbolPicker_RecentlyUsedSymbols\")");
        source.Should().Contain("Content = UiText.Get(\"SymbolPicker_CharacterCodeLabel\")");
        source.Should().Contain("Target = selectedCode");
        source.Should().Contain("UiText.Get(\"SymbolPicker_FromUnicodeHex\")");
        source.Should().Contain("UniformGrid");
    }

    [Fact]
    public void Dialog_CharacterCodeGoAction_FocusesAndSelectsCodeEntry()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("ShowInvalidCharacterCodeWarning(selectedCode);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, UiText.Get(\"SymbolPicker_InvalidCharacterCodeMessage\"), Title);");
        source.Should().Contain("selectedCode.Focus();");
        source.Should().Contain("selectedCode.SelectAll();");
        source.Should().Contain("Keyboard.Focus(selectedCode);");
    }

    [Fact]
    public void Dialog_SelectsSymbolsBeforeExplicitInsert()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("void SelectSymbol(char value)");
        source.Should().Contain("SymbolPickerSelectionPlanner.CreateSelection(value)");
        source.Should().Contain("ApplySelection(selection)");
        source.Should().Contain("insert.Click += (_, _) =>");
        source.Should().Contain("DialogResult = true");
        source.Should().NotContain("SelectedChar = c;\r\n                    DialogResult = true");
    }

    [Fact]
    public void Dialog_DoubleClickInsertsSelectedSymbolOrSpecialCharacter()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("void AcceptSelectedSymbol()");
        source.Should().Contain("button.MouseDoubleClick += (_, e) =>");
        source.Should().Contain("AcceptSelectedSymbol();");
        source.Should().Contain("specialList.MouseDoubleClick += (_, e) =>");
        source.Should().Contain("acceptSelectedSymbol();");
        source.Split("e.Handled = true;").Length.Should().BeGreaterThanOrEqualTo(3);
        source.Should().Contain("insert.Click += (_, _) => acceptSelectedSymbol();");
    }

    [Fact]
    public void MainWindow_InsertsSelectedSymbolStringIntoTheActiveCell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.InsertCommands.cs"));

        source.Should().Contain("string.IsNullOrEmpty(dlg.SelectedSymbol)");
        source.Should().Contain("var selectedSymbol = dlg.SelectedSymbol;");
        source.Should().Contain("var currentText = (currentExisting?.Value ?? \"\") + selectedSymbol;");
        source.Should().Contain("TryExecuteRepeatableCurrentRangeCommand(");
        source.Should().Contain("CreateSingleCellEditCommand(currentAddress, Cell.FromValue(new TextValue(currentText)))");
        source.Should().NotContain("dlg.SelectedChar == '\\0'");
        source.Should().NotContain("+ selectedChar");
    }
}
