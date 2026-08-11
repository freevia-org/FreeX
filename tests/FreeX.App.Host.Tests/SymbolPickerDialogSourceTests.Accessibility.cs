using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class SymbolPickerDialogSourceTests
{
    [Fact]
    public void Dialog_ExposesAccessKeysForSymbolTabsAndFocusesSymbolGridOnOpen()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("Header = UiText.Get(\"SymbolPicker_SymbolsTab\")");
        source.Should().Contain("Header = UiText.Get(\"SymbolPicker_SpecialCharactersTab\")");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget(symbolList);");
        source.Should().Contain("private static void FocusInitialKeyboardTarget(ListBox symbolList)");
        source.Should().Contain("Keyboard.Focus(symbolList);");
    }

    [Fact]
    public void Dialog_DoesNotLetHiddenSpecialCharactersTabOverrideInitialSymbolSelection()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("ApplySelection(SymbolPickerCatalogPlanner.CreateDefaultSelection())");
        source.Should().NotContain("specialList.SelectedIndex = 0;");
    }

    [Fact]
    public void Dialog_NamesSymbolGridAndSpecialCharacterListForAccessibility()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("AutomationProperties.SetName(symbolList, UiText.Get(\"SymbolPicker_SymbolsAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetName(specialList, UiText.Get(\"SymbolPicker_SpecialCharactersAutomationName\"));");
    }

    [Fact]
    public void Dialog_NamesSymbolPickerControlsAndActionsForAccessibility()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("AutomationProperties.SetName(fontBox, UiText.Get(\"SymbolPicker_FontAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(fontBox, UiText.Get(\"SymbolPicker_FontHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(subsetBox, UiText.Get(\"SymbolPicker_SubsetAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(subsetBox, UiText.Get(\"SymbolPicker_SubsetHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(searchBox, UiText.Get(\"SymbolPicker_SearchAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(searchBox, UiText.Get(\"SymbolPicker_SearchHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(selectedCode, UiText.Get(\"SymbolPicker_CharacterCodeAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(selectedCode, UiText.Get(\"SymbolPicker_CharacterCodeHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(preview, UiText.Get(\"SymbolPicker_SelectedSymbolPreviewAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(preview, UiText.Get(\"SymbolPicker_SelectedSymbolPreviewHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(codeSelect, UiText.Get(\"SymbolPicker_GoToCharacterCodeAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(codeSelect, UiText.Get(\"SymbolPicker_GoToCharacterCodeHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(insert, UiText.Get(\"SymbolPicker_InsertSelectedSymbolAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(insert, UiText.Get(\"SymbolPicker_InsertSelectedSymbolHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(cancel, UiText.Get(\"SymbolPicker_CancelAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(cancel, UiText.Get(\"SymbolPicker_CancelHelpText\"));");
    }

    [Fact]
    public void Dialog_NamesSymbolButtonsAndSpecialCharacterItemsForAccessibility()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("Converter = SymbolAutomationNameConverter.Instance");
        source.Should().Contain("private static string CreateSymbolAutomationName(string value)");
        source.Should().Contain("Converter = SpecialCharacterAutomationNameConverter.Instance");
    }
}
