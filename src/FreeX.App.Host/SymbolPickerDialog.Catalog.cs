using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Host;

public sealed partial class SymbolPickerDialog
{
    private static readonly IReadOnlyList<string> SubsetChoices = SymbolPickerCatalogPlanner.GetSubsetNames();

    public static IReadOnlyList<string> GetSymbolsForSubset(string subset) =>
        SymbolPickerCatalogPlanner.GetSymbolsForSubset(subset);

    public static IReadOnlyList<SymbolPickerCatalogEntry> GetSymbolEntriesForSubset(string subset) =>
        SymbolPickerCatalogPlanner.GetSymbolEntriesForSubset(subset);

    public static IReadOnlyList<SymbolPickerCatalogEntry> SearchSymbolEntries(string searchText) =>
        SymbolPickerCatalogPlanner.SearchSymbolEntries(searchText);

    public static SymbolPickerCatalogEntry CreateSymbolEntry(string symbol, string fallbackSubset) =>
        SymbolPickerCatalogPlanner.CreateSymbolEntry(symbol, fallbackSubset);

    public static SymbolPickerCatalogEntry? FindSymbolEntry(string symbol) =>
        SymbolPickerCatalogPlanner.FindSymbolEntry(symbol);

    public static IReadOnlyList<string> GetSubsetNames() => SymbolPickerCatalogPlanner.GetSubsetNames();

    public static IReadOnlyList<SymbolPickerSpecialCharacter> GetSpecialCharacters() =>
        SymbolPickerCatalogPlanner.GetSpecialCharacters();

    public static bool TryParseCharacterCode(string text, out string symbol) =>
        SymbolPickerCatalogPlanner.TryParseCharacterCode(text, out symbol);

    public static IReadOnlyList<string> PromoteRecentSymbol(
        IEnumerable<string> currentSymbols,
        string selectedSymbol,
        int capacity = SymbolPickerCatalogPlanner.DefaultRecentSymbolCapacity) =>
        SymbolPickerCatalogPlanner.PromoteRecentSymbol(currentSymbols, selectedSymbol, capacity);
}
