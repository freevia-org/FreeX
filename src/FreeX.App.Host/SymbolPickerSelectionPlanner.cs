using System.Text;

namespace FreeX.App.Host;

public sealed record SymbolPickerSelection(string Symbol, char SelectedChar, string CodeText);

public static class SymbolPickerSelectionPlanner
{
    public static SymbolPickerSelection CreateSelection(string symbol)
    {
        var safeSymbol = NormalizeSymbol(symbol);
        return new SymbolPickerSelection(
            safeSymbol,
            GetSelectedChar(safeSymbol),
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

        return GetFirstRuneCodeText(value);
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

    private static string NormalizeSymbol(string? symbol) => symbol ?? "";

    private static char GetSelectedChar(string symbol) =>
        symbol.Length == 1 ? symbol[0] : '\0';

    private static string GetFirstRuneCodeText(string value)
    {
        var rune = value.EnumerateRunes().FirstOrDefault();
        return rune == default ? "" : rune.Value.ToString("X4");
    }
}
