using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class SymbolPickerDialogSourceTests
{
    [Fact]
    public void Dialog_ExposesKeyboardAccessKeysForInsertAndCancel()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("Content = \"_Insert\"");
        source.Should().Contain("Content = \"_Cancel\"");
    }

    [Fact]
    public void Dialog_ExposesExcelLikeSymbolSelectionAffordances()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("Content = \"_Font:\"");
        source.Should().Contain("Content = \"_Subset:\"");
        source.Should().Contain("Recently used symbols");
        source.Should().Contain("Content = \"Character _code:\"");
        source.Should().Contain("Target = selectedCode");
        source.Should().Contain("from: Unicode (hex)");
        source.Should().Contain("UniformGrid");
    }

    [Fact]
    public void Dialog_CharacterCodeGoAction_FocusesAndSelectsCodeEntry()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("ShowInvalidCharacterCodeWarning(selectedCode);");
        source.Should().Contain("MessageBox.Show(this, \"Enter a valid Unicode character code.\", Title, MessageBoxButton.OK, MessageBoxImage.Warning);");
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
        source.Should().Contain("button.MouseDoubleClick += (_, _) => AcceptSelectedSymbol();");
        source.Should().Contain("specialList.MouseDoubleClick += (_, _) => acceptSelectedSymbol();");
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

        source.Should().Contain("Header = \"_Symbols\"");
        source.Should().Contain("Header = \"Special _Characters\"");
    }

    [Fact]
    public void Dialog_ExposesAccessKeysForSymbolTabsAndFocusesSymbolGridOnOpen()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("Header = \"_Symbols\"");
        source.Should().Contain("Header = \"Special _Characters\"");
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

        source.Should().Contain("AutomationProperties.SetName(grid, \"Symbols\");");
        source.Should().Contain("AutomationProperties.SetName(specialList, \"Special characters\");");
    }

    [Fact]
    public void Dialog_NamesSymbolPickerControlsAndActionsForAccessibility()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("AutomationProperties.SetName(fontBox, \"Symbol font\");");
        source.Should().Contain("AutomationProperties.SetHelpText(fontBox, \"Choose the font used to preview and insert symbols.\");");
        source.Should().Contain("AutomationProperties.SetName(subsetBox, \"Symbol subset\");");
        source.Should().Contain("AutomationProperties.SetHelpText(subsetBox, \"Choose the Unicode subset shown in the symbol grid.\");");
        source.Should().Contain("AutomationProperties.SetName(selectedCode, \"Character code\");");
        source.Should().Contain("AutomationProperties.SetHelpText(selectedCode, \"Enter a Unicode hexadecimal character code.\");");
        source.Should().Contain("AutomationProperties.SetName(preview, \"Selected symbol preview\");");
        source.Should().Contain("AutomationProperties.SetHelpText(preview, \"Shows the currently selected symbol.\");");
        source.Should().Contain("AutomationProperties.SetName(codeSelect, \"Go to character code\");");
        source.Should().Contain("AutomationProperties.SetHelpText(codeSelect, \"Select the symbol for the entered Unicode character code.\");");
        source.Should().Contain("AutomationProperties.SetName(insert, \"Insert selected symbol\");");
        source.Should().Contain("AutomationProperties.SetHelpText(insert, \"Insert the selected symbol or special character.\");");
        source.Should().Contain("AutomationProperties.SetName(cancel, \"Cancel symbol insertion\");");
        source.Should().Contain("AutomationProperties.SetHelpText(cancel, \"Close the Symbol dialog without inserting a symbol.\");");
    }

    [Fact]
    public void Dialog_NamesSymbolButtonsAndSpecialCharacterItemsForAccessibility()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("AutomationProperties.SetName(button, CreateSymbolAutomationName(value));");
        source.Should().Contain("private static string CreateSymbolAutomationName(string value)");
        source.Should().Contain("AutomationProperties.SetName(item, $\"{special.Name}, {CreateSymbolAutomationName(special.Symbol)}\");");
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.InsertCommands.cs"));

        source.Should().Contain("string.IsNullOrEmpty(dlg.SelectedSymbol)");
        source.Should().Contain("var selectedSymbol = dlg.SelectedSymbol;");
        source.Should().Contain("var currentText = (currentExisting?.Value ?? \"\") + selectedSymbol;");
        source.Should().Contain("TryExecuteRepeatableCurrentRangeCommand(");
        source.Should().Contain("CreateSingleCellEditCommand(currentAddress, Cell.FromValue(new TextValue(currentText)))");
        source.Should().NotContain("dlg.SelectedChar == '\\0'");
        source.Should().NotContain("+ selectedChar");
    }

    private static string ReadSymbolPickerDialogSources() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SymbolPickerDialog.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SymbolPickerDialog.Layout.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SymbolPickerDialog.Catalog.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SymbolPickerSelectionPlanner.cs"));
}
