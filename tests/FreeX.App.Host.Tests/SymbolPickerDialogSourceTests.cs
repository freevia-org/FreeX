using FluentAssertions;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

public sealed class SymbolPickerDialogSourceTests
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
    public void Dialog_RebuildsSymbolsForSelectedSubset()
    {
        SymbolPickerDialog.GetSymbolsForSubset("Currency Symbols").Should().Contain('\u20ac');
        SymbolPickerDialog.GetSymbolsForSubset("Greek and Coptic").Should().Contain('\u03c0');
        SymbolPickerDialog.GetSymbolsForSubset("Arrows").Should().Contain('\u2192');

        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("SymbolsBySubset");
        source.Should().Contain("subsetBox.SelectionChanged");
        source.Should().Contain("PopulateGrid(subset)");
    }

    [Fact]
    public void Dialog_OffersBroaderExcelLikeUnicodeSubsets()
    {
        SymbolPickerDialog.GetSubsetNames().Should().Contain([
            "Latin-1 Supplement",
            "Greek and Coptic",
            "Cyrillic",
            "Currency Symbols",
            "Arrows",
            "Mathematical Operators",
            "Box Drawing",
            "Geometric Shapes"]);

        SymbolPickerDialog.GetSymbolsForSubset("Latin-1 Supplement").Should().Contain('\u00f1');
        SymbolPickerDialog.GetSymbolsForSubset("Cyrillic").Should().Contain('\u0416');
        SymbolPickerDialog.GetSymbolsForSubset("Box Drawing").Should().Contain('\u250c');
        SymbolPickerDialog.GetSymbolsForSubset("Geometric Shapes").Should().Contain('\u25c6');
    }

    [Fact]
    public void Dialog_OffersSpecialCharactersSurface()
    {
        SymbolPickerDialog.GetSpecialCharacters().Should().Contain([
            new SymbolPickerDialog.SpecialCharacter("Em Dash", "\u2014"),
            new SymbolPickerDialog.SpecialCharacter("Nonbreaking Space", "\u00a0"),
            new SymbolPickerDialog.SpecialCharacter("Copyright", "\u00a9"),
            new SymbolPickerDialog.SpecialCharacter("Registered", "\u00ae"),
            new SymbolPickerDialog.SpecialCharacter("Trademark", "\u2122")]);

        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("Header = UiText.Get(\"SymbolPicker_SymbolsTab\")");
        source.Should().Contain("Header = UiText.Get(\"SymbolPicker_SpecialCharactersTab\")");
    }

    [Fact]
    public void Dialog_ExposesAccessKeysForSymbolTabsAndFocusesSymbolGridOnOpen()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("Header = UiText.Get(\"SymbolPicker_SymbolsTab\")");
        source.Should().Contain("Header = UiText.Get(\"SymbolPicker_SpecialCharactersTab\")");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget(grid);");
        source.Should().Contain("private static void FocusInitialKeyboardTarget(UniformGrid grid)");
        source.Should().Contain("Keyboard.Focus(firstSymbol);");
    }

    [Fact]
    public void Dialog_DoesNotLetHiddenSpecialCharactersTabOverrideInitialSymbolSelection()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("ApplySelection(SymbolPickerSelectionPlanner.CreateInitialSelection(GetSymbolsForSubset(SubsetChoices[0])))");
        source.Should().NotContain("specialList.SelectedIndex = 0;");
    }

    [Fact]
    public void Dialog_NamesSymbolGridAndSpecialCharacterListForAccessibility()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("AutomationProperties.SetName(grid, UiText.Get(\"SymbolPicker_SymbolsAutomationName\"));");
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

        source.Should().Contain("AutomationProperties.SetName(button, CreateSymbolAutomationName(value));");
        source.Should().Contain("private static string CreateSymbolAutomationName(string value)");
        source.Should().Contain("AutomationProperties.SetName(item, UiText.Format(\"SymbolPicker_SpecialCharacterAutomationNameFormat\", special.Name, CreateSymbolAutomationName(special.Symbol)));");
    }

    [Fact]
    public void Dialog_AppliesSelectedFontToInitialAndRecentSymbols()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SymbolPickerDialog();
            try
            {
                var fontBox = FindLogicalChildren<ComboBox>(dialog)
                    .Single(box => AutomationProperties.GetName(box) == UiText.Get("SymbolPicker_FontAutomationName"));
                var preview = FindLogicalChildren<TextBlock>(dialog)
                    .Single(text => AutomationProperties.GetName(text) == UiText.Get("SymbolPicker_SelectedSymbolPreviewAutomationName"));
                var symbolButtons = FindLogicalChildren<Button>(dialog)
                    .Where(button => button.Tag is string)
                    .ToList();

                fontBox.SelectedItem.Should().Be("Segoe UI Symbol");
                preview.FontFamily.Source.Should().Be("Segoe UI Symbol");
                symbolButtons.Should().NotBeEmpty();
                symbolButtons.Should().AllSatisfy(button => button.FontFamily.Source.Should().Be("Segoe UI Symbol"));

                fontBox.SelectedItem = "Arial";

                preview.FontFamily.Source.Should().Be("Arial");
                symbolButtons.Should().AllSatisfy(button => button.FontFamily.Source.Should().Be("Arial"));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData("03C0", "\u03c0")]
    [InlineData("U+2192", "\u2192")]
    [InlineData("1F600", "\ud83d\ude00")]
    public void Dialog_ParsesUnicodeCharacterCodeEntries(string text, string expected)
    {
        SymbolPickerDialog.TryParseCharacterCode(text, out var symbol).Should().BeTrue();
        symbol.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("XYZ")]
    [InlineData("D800")]
    [InlineData("110000")]
    public void Dialog_RejectsInvalidUnicodeCharacterCodeEntries(string text)
    {
        SymbolPickerDialog.TryParseCharacterCode(text, out var symbol).Should().BeFalse();
        symbol.Should().BeEmpty();
    }

    [Fact]
    public void Dialog_PromotesSelectedSymbolsIntoRecentList()
    {
        var recent = SymbolPickerDialog.PromoteRecentSymbol(
            ["\u20ac", "\u00a3", "\u00a5"],
            "\u03c0",
            capacity: 3);

        recent.Should().Equal(["\u03c0", "\u20ac", "\u00a3"]);

        SymbolPickerDialog.PromoteRecentSymbol(recent, "\u20ac", capacity: 3)
            .Should().Equal(["\u20ac", "\u03c0", "\u00a3"]);
    }

    [Theory]
    [InlineData("\u03c0", '\u03c0', "03C0")]
    [InlineData("\ud83d\ude00", '\0', "1F600")]
    [InlineData("", '\0', "")]
    public void SelectionPlanner_FormatsSelectedSymbolState(string symbol, char selectedChar, string codeText)
    {
        SymbolPickerSelectionPlanner.CreateSelection(symbol)
            .Should()
            .Be(new SymbolPickerSelection(symbol, selectedChar, codeText));
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

    private static string ReadSymbolPickerDialogSources() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SymbolPickerDialog.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SymbolPickerDialog.Layout.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SymbolPickerDialog.Catalog.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SymbolPickerSelectionPlanner.cs"));

    private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;

            foreach (var descendant in FindLogicalChildren<T>(child))
                yield return descendant;
        }
    }
}
