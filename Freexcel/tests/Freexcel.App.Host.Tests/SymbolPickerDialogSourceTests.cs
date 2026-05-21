using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class SymbolPickerDialogSourceTests
{
    [Fact]
    public void Dialog_ExposesKeyboardAccessKeysForInsertAndCancel()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SymbolPickerDialog.cs"));

        source.Should().Contain("Content = \"_Insert\"");
        source.Should().Contain("Content = \"_Cancel\"");
    }

    [Fact]
    public void Dialog_ExposesExcelLikeSymbolSelectionAffordances()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SymbolPickerDialog.cs"));

        source.Should().Contain("Content = \"_Font:\"");
        source.Should().Contain("Content = \"_Subset:\"");
        source.Should().Contain("Recently used symbols");
        source.Should().Contain("Character code:");
        source.Should().Contain("from: Unicode (hex)");
        source.Should().Contain("UniformGrid");
    }

    [Fact]
    public void Dialog_SelectsSymbolsBeforeExplicitInsert()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SymbolPickerDialog.cs"));

        source.Should().Contain("void SelectSymbol(char value)");
        source.Should().Contain("SelectedChar = value");
        source.Should().Contain("insert.Click += (_, _) => DialogResult = true");
        source.Should().NotContain("SelectedChar = c;\r\n                    DialogResult = true");
    }

    [Fact]
    public void Dialog_RebuildsSymbolsForSelectedSubset()
    {
        SymbolPickerDialog.GetSymbolsForSubset("Currency Symbols").Should().Contain('\u20ac');
        SymbolPickerDialog.GetSymbolsForSubset("Greek and Coptic").Should().Contain('\u03c0');
        SymbolPickerDialog.GetSymbolsForSubset("Arrows").Should().Contain('\u2192');

        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SymbolPickerDialog.cs"));

        source.Should().Contain("SymbolsBySubset");
        source.Should().Contain("subsetBox.SelectionChanged");
        source.Should().Contain("PopulateGrid(subset)");
    }
}
