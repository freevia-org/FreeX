namespace FreeX.App.Services;

public sealed record HomeNumberFormatDropdownOption(
    string Value,
    string Label,
    string? Code,
    bool OpensFormatCellsDialog = false);

public sealed record HomeAccountingSymbolDropdownOption(
    string CommandId,
    string Label,
    string Symbol,
    string NumberFormatCode);

public static class HomeNumberFormatDropdownPlanner
{
    public const string AccountingNumberFormatCode = "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)";
    public const string CommaStyleNumberFormatCode = "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)";
    public const string MoreNumberFormatsLabel = "More Number Formats...";

    public static IReadOnlyList<HomeNumberFormatDropdownOption> Options { get; } =
    [
        new("General", "General", "General"),
        new("0.00", "Number", "0.00"),
        new("$#,##0.00", "Currency", "$#,##0.00"),
        new(AccountingNumberFormatCode, "Accounting", AccountingNumberFormatCode),
        new("m/d/yyyy", "Short Date", "m/d/yyyy"),
        new("[$-F800]", "Long Date", "[$-F800]"),
        new("h:mm AM/PM", "Time", "h:mm AM/PM"),
        new("0%", "Percentage", "0%"),
        new("# ?/?", "Fraction", "# ?/?"),
        new("0.00E+00", "Scientific", "0.00E+00"),
        new("@", "Text", "@"),
        new("number-format.more", MoreNumberFormatsLabel, null, OpensFormatCellsDialog: true)
    ];

    public static HomeNumberFormatDropdownOption? FindByValueOrLegacyLabel(string? value) =>
        Options.FirstOrDefault(option =>
            string.Equals(option.Value, value, StringComparison.Ordinal) ||
            string.Equals(option.Label, value, StringComparison.Ordinal));

    public static IReadOnlyList<HomeAccountingSymbolDropdownOption> AccountingSymbolOptions { get; } =
    [
        AccountingSymbol("Accounting Number Format US Dollar", "US Dollar ($)", "$"),
        AccountingSymbol("Accounting Number Format Euro", "Euro (EUR)", "\u20AC"),
        AccountingSymbol("Accounting Number Format British Pound", "British Pound (GBP)", "\u00A3"),
        AccountingSymbol("Accounting Number Format Japanese Yen", "Japanese Yen (JPY)", "\u00A5"),
    ];

    public static int DefaultSelectionIndex => 0;

    public static string ResolveAccountingNumberFormatCode(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            symbol = "$";

        return AccountingSymbolOptions
            .FirstOrDefault(option => string.Equals(option.Symbol, symbol, StringComparison.Ordinal))
            ?.NumberFormatCode
            ?? FormatCellsNumberFormatPlanner.BuildAccountingFormatFor(2, symbol);
    }

    private static HomeAccountingSymbolDropdownOption AccountingSymbol(
        string commandId,
        string label,
        string symbol) =>
        new(commandId, label, symbol, FormatCellsNumberFormatPlanner.BuildAccountingFormatFor(2, symbol));
}
