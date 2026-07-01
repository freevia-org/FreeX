using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Host;

public sealed partial class SymbolPickerDialog
{
    private static readonly IReadOnlyList<string> SubsetChoices = SymbolPickerCatalogPlanner.GetSubsetNames();

    public static IReadOnlyList<string> GetSymbolsForSubset(string subset) =>
        SymbolPickerCatalogPlanner.GetSymbolsForSubset(subset);

    public static IReadOnlyList<SymbolCatalogEntry> GetSymbolEntriesForSubset(string subset) =>
        SymbolPickerCatalogPlanner.GetSymbolEntriesForSubset(subset)
            .Select(SymbolCatalogEntry.FromPresentation)
            .ToArray();

    public static IReadOnlyList<SymbolCatalogEntry> SearchSymbolEntries(string searchText) =>
        SymbolPickerCatalogPlanner.SearchSymbolEntries(searchText)
            .Select(SymbolCatalogEntry.FromPresentation)
            .ToArray();

    public static SymbolCatalogEntry CreateSymbolEntry(string symbol, string fallbackSubset) =>
        SymbolCatalogEntry.FromPresentation(SymbolPickerCatalogPlanner.CreateSymbolEntry(symbol, fallbackSubset));

    public static SymbolCatalogEntry? FindSymbolEntry(string symbol)
    {
        var entry = SymbolPickerCatalogPlanner.FindSymbolEntry(symbol);
        return entry is null
            ? null
            : SymbolCatalogEntry.FromPresentation(entry.Value);
    }

    public static IReadOnlyList<string> GetSubsetNames() => SymbolPickerCatalogPlanner.GetSubsetNames();

    public static IReadOnlyList<SpecialCharacter> GetSpecialCharacters() =>
        SymbolPickerCatalogPlanner.GetSpecialCharacters()
            .Select(SpecialCharacter.FromPresentation)
            .ToArray();

    public static bool TryParseCharacterCode(string text, out string symbol) =>
        SymbolPickerCatalogPlanner.TryParseCharacterCode(text, out symbol);

    public static IReadOnlyList<string> PromoteRecentSymbol(
        IEnumerable<string> currentSymbols,
        string selectedSymbol,
        int capacity = SymbolPickerCatalogPlanner.DefaultRecentSymbolCapacity) =>
        SymbolPickerCatalogPlanner.PromoteRecentSymbol(currentSymbols, selectedSymbol, capacity);
}
