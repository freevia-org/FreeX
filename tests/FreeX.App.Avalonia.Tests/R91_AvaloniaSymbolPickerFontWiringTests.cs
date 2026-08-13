using System.IO;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R91-commands-insert-object-5-3: the Avalonia Insert Symbol dialog's Font combo box
/// (<c>fontBox</c>) had no <c>SelectionChanged</c> handler at all -- unlike <c>subsetBox</c> and
/// <c>searchBox</c> -- so choosing a different font (e.g. Wingdings) was a complete no-op: the
/// catalog grid stayed on the fixed Unicode table. Pins that the handler now exists and that
/// <c>RefreshSymbols</c> threads the chosen font into the shared planner.
/// </summary>
public sealed class R91_AvaloniaSymbolPickerFontWiringTests
{
    private static string ReadSource() =>
        File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Symbol.cs"))
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

    [Fact]
    public void FontBox_HasASelectionChangedHandlerThatRefreshesTheCatalog()
    {
        var source = ReadSource();

        source.Should().Contain("fontBox.SelectionChanged += (_, _) => RefreshSymbols();");
    }

    [Fact]
    public void RefreshSymbols_PassesTheSelectedFontIntoThePlanner()
    {
        var source = ReadSource();

        source.Should().Contain(
            "SymbolPickerCatalogPlanner.PlanSymbolList(\n" +
            "                subsetBox.SelectedItem as string,\n" +
            "                searchBox.Text,\n" +
            "                selectedSymbol,\n" +
            "                selectedFontName);");
        source.Should().Contain("var selectedFontName = fontBox.SelectedItem as string;");
    }

    // No-regression sibling: the subset and search boxes must still refresh the catalog the same
    // way they always did (this fix must not have disturbed their existing wiring).
    [Fact]
    public void SubsetAndSearchSelectionChanged_StillRefreshTheCatalog()
    {
        var source = ReadSource();

        source.Should().Contain("subsetBox.SelectionChanged += (_, _) => RefreshSymbols();");
        source.Should().Contain("searchBox.TextChanged += (_, _) => RefreshSymbols();");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
