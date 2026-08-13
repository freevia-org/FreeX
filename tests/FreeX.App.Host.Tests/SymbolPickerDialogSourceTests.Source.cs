using FluentAssertions;

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

        source.Should().Contain("AddLabeledControl(grid, 0, UiText.Get(\"SymbolPicker_FontLabel\"), fontBox);");
        source.Should().Contain("AddLabeledControl(grid, 2, UiText.Get(\"SymbolPicker_SubsetLabel\"), subsetBox);");
        source.Should().Contain("UiText.Get(\"SymbolPicker_RecentlyUsedSymbols\")");
        source.Should().Contain("Content = UiText.Get(\"SymbolPicker_CharacterCodeLabel\")");
        source.Should().Contain("Target = selectedCode");
        source.Should().Contain("UiText.Get(\"SymbolPicker_FromUnicodeHex\")");
        source.Should().Contain("UiText.Get(\"SymbolPicker_SearchLabel\")");
        source.Should().Contain("ListBox");
        source.Should().Contain("WrapPanel");
        source.Should().Contain("GridViewColumn");
        source.Should().Contain("Width = SymbolPickerCatalogPlanner.DialogWidth");
        source.Should().Contain("Height = SymbolPickerCatalogPlanner.DialogHeight");
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

        source.Should().Contain("void SelectCatalogEntry(SymbolPickerCatalogEntry entry)");
        source.Should().Contain("SymbolPickerCatalogPlanner.CreateSelection(value)");
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
        source.Should().Contain("symbolList.MouseDoubleClick += (_, e) =>");
        source.Should().Contain("recentList.MouseDoubleClick += (_, e) =>");
        source.Should().Contain("AcceptSelectedSymbol();");
        source.Should().Contain("specialList.MouseDoubleClick += (_, e) =>");
        source.Should().Contain("acceptSelectedSymbol();");
        source.Split("e.Handled = true;").Length.Should().BeGreaterThanOrEqualTo(3);
        source.Should().Contain("insert.Click += (_, _) => acceptSelectedSymbol();");
    }

    [Fact]
    public void Dialog_UsesDenseSelectableListsInsteadOfSymbolCommandButtons()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("CreateSymbolList(symbolItems");
        source.Should().Contain("SelectionMode = SelectionMode.Single");
        source.Should().Contain("ItemContainerStyle = CreateSymbolItemStyle(cellSize)");
        source.Should().Contain("KeyboardNavigationMode.Contained");
        source.Should().NotContain("Button CreateSymbolButton");
    }

    [Fact]
    public void MainWindow_InsertsSelectedSymbolStringIntoTheActiveCell()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.InsertCommands.cs");

        source.Should().Contain("string.IsNullOrEmpty(dlg.SelectedSymbol)");
        source.Should().Contain("var selectedSymbol = dlg.SelectedSymbol;");
        source.Should().Contain("var currentText = (currentExisting?.Value ?? \"\") + selectedSymbol;");
        source.Should().Contain("TryExecuteRepeatableCurrentRangeCommand(");
        source.Should().Contain("CreateSingleCellEditCommand(currentAddress, Cell.FromValue(new TextValue(currentText)))");
        source.Should().NotContain("dlg.SelectedChar == '\\0'");
        source.Should().NotContain("+ selectedChar");
    }
}
