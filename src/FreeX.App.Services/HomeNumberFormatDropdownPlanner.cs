namespace FreeX.App.Services;

public sealed record HomeNumberFormatDropdownOption(string Label, string? Code, bool OpensFormatCellsDialog = false);

public static class HomeNumberFormatDropdownPlanner
{
    public const string AccountingNumberFormatCode = "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)";
    public const string CommaStyleNumberFormatCode = "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)";
    public const string MoreNumberFormatsLabel = "More Number Formats...";

    public static IReadOnlyList<HomeNumberFormatDropdownOption> Options { get; } =
    [
        new("General", "General"),
        new("Number", "0.00"),
        new("Currency", "$#,##0.00"),
        new("Accounting", AccountingNumberFormatCode),
        new("Short Date", "m/d/yyyy"),
        new("Long Date", "[$-F800]"),
        new("Time", "h:mm AM/PM"),
        new("Percentage", "0%"),
        new("Fraction", "# ?/?"),
        new("Scientific", "0.00E+00"),
        new("Text", "@"),
        new(MoreNumberFormatsLabel, null, OpensFormatCellsDialog: true)
    ];

    public static int DefaultSelectionIndex => 0;
}
