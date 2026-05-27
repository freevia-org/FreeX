using System.Text;

namespace Freexcel.App.Host;

public sealed record SymbolPickerSelection(string Symbol, char SelectedChar, string CodeText);

public static class SymbolPickerSelectionPlanner
{
    public static SymbolPickerSelection CreateSelection(string symbol)
    {
        var safeSymbol = symbol ?? "";
        return new SymbolPickerSelection(
            safeSymbol,
            safeSymbol.Length == 1 ? safeSymbol[0] : '\0',
            FormatCodeText(safeSymbol));
    }

    public static SymbolPickerSelection CreateSelection(char symbol) =>
        CreateSelection(symbol.ToString());

    public static SymbolPickerSelection CreateInitialSelection(IReadOnlyList<char> subsetSymbols) =>
        CreateSelection(subsetSymbols.Count > 0 ? subsetSymbols[0] : '\0');

    public static string FormatCodeText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var rune = value.EnumerateRunes().FirstOrDefault();
        return rune == default ? "" : rune.Value.ToString("X4");
    }

    public static IReadOnlyList<string> PromoteRecentSymbol(
        IEnumerable<string> currentSymbols,
        string selectedSymbol,
        int capacity = 8)
    {
        if (string.IsNullOrEmpty(selectedSymbol) || capacity <= 0)
            return [];

        return currentSymbols
            .Where(symbol => !string.Equals(symbol, selectedSymbol, StringComparison.Ordinal))
            .Prepend(selectedSymbol)
            .Take(capacity)
            .ToArray();
    }
}
