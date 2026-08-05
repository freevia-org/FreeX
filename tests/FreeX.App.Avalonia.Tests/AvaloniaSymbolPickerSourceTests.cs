using System.IO;

using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaSymbolPickerSourceTests
{
    [Fact]
    public void DialogPolicy_DelegatesSymbolCatalogToSharedPresentationPlanner()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Symbol.cs"));

        source.Should().Contain("using FreeX.App.Presentation.Dialogs;");
        source.Should().Contain("SymbolPickerCatalogPlanner.GetPreferredFontChoices()");
        source.Should().Contain("SymbolPickerCatalogPlanner.DefaultRecentSymbols");
        source.Should().Contain("SymbolPickerCatalogPlanner.GetSubsetNames()");
        source.Should().Contain("SymbolPickerCatalogPlanner.PlanSymbolList(");
        source.Should().Contain("SymbolPickerCatalogPlanner.CreateSymbolEntry(");
        source.Should().Contain("SymbolPickerCatalogPlanner.GetSpecialCharacters()");
        source.Should().Contain("SymbolPickerCatalogPlanner.TryParseCharacterCode(");
        source.Should().Contain("SymbolPickerCatalogPlanner.CreateSelection(selectedSymbol)");
        source.Should().Contain("Width = SymbolPickerCatalogPlanner.DialogWidth");
        source.Should().Contain("Height = SymbolPickerCatalogPlanner.DialogHeight");

        source.Should().NotContain("SymbolPickerFontChoices");
        source.Should().NotContain("SymbolPickerRecentSymbols");
        source.Should().NotContain("SymbolPickerNames");
        source.Should().NotContain("CreateLatinSupplementSymbols");
        source.Should().NotContain("TryParseSymbolCode");
        source.Should().NotContain("SymbolPickerSelectionPlanner");
    }

    [Fact]
    public void SharedPlanner_ProvidesAvaloniaSymbolDialogDescriptors()
    {
        SymbolPickerCatalogPlanner.GetPreferredFontChoices().Should().Contain([
            "Segoe UI Symbol",
            "Segoe UI Historic",
            "Cambria Math",
            "Wingdings"]);

        SymbolPickerCatalogPlanner.GetSubsetNames().Should().Contain([
            "Latin-1 Supplement",
            "Greek and Coptic",
            "Arrows",
            "Mathematical Operators"]);

        SymbolPickerCatalogPlanner.DefaultRecentSymbols.Should().Contain(["\u20ac", "\u03c0", "\u2713"]);
        SymbolPickerCatalogPlanner.GetSpecialCharacters().Should().HaveCountGreaterThan(35);
        SymbolPickerCatalogPlanner.TryParseCharacterCode("U+2192", out var symbol).Should().BeTrue();
        symbol.Should().Be("\u2192");
        SymbolPickerCatalogPlanner.DialogWidth.Should().Be(840);
        SymbolPickerCatalogPlanner.DialogHeight.Should().Be(620);
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
