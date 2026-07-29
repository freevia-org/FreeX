using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R91-commands-insert-object-5-3: the WPF Insert Symbol dialog's Font combo box applied the
/// chosen font to the preview/list controls (<c>ApplySymbolFont</c>) but never rebuilt the
/// catalog itself -- <c>RefreshSymbols()</c> was never called on font change, so the fixed
/// Unicode symbol table stayed on screen (just re-fonted), instead of switching to the chosen
/// dingbat font's own glyph set. Pins that <c>fontBox.SelectionChanged</c> now also refreshes the
/// catalog, and that the catalog rebuild itself is font-aware via the shared planner.
/// </summary>
public sealed class R91_SymbolPickerDialogFontWiringTests
{
    private static string ReadSource() =>
        DialogSourceTestSupport.ReadHostSourcesWithSeparator(
                Environment.NewLine,
                "SymbolPickerDialog.Layout.cs")
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

    [Fact]
    public void FontSelectionChanged_RebuildsTheSymbolCatalog()
    {
        var source = ReadSource();

        source.Should().Contain("fontBox.SelectionChanged += (_, _) =>");

        var handlerStart = source.IndexOf("fontBox.SelectionChanged += (_, _) =>", StringComparison.Ordinal);
        var handlerEnd = source.IndexOf("};", handlerStart, StringComparison.Ordinal);
        handlerStart.Should().BeGreaterThan(-1);
        handlerEnd.Should().BeGreaterThan(handlerStart);

        var handlerBody = source[handlerStart..handlerEnd];
        handlerBody.Should().Contain("ApplySymbolFont(fontName)");
        handlerBody.Should().Contain("RefreshSymbols()");
    }

    [Fact]
    public void RefreshSymbols_PassesTheSelectedFontIntoThePlanner()
    {
        var source = ReadSource();

        source.Should().Contain(
            "SymbolPickerCatalogPlanner.PlanSymbolList(\n" +
            "                subsetBox.SelectedItem as string,\n" +
            "                searchBox.Text,\n" +
            "                SelectedSymbol,\n" +
            "                fontBox.SelectedItem as string);");
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
}
