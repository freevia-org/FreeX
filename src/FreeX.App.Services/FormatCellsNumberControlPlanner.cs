namespace FreeX.App.Services;

public sealed record FormatCellsNumberControlAvailability(
    bool UsesDecimals,
    bool UsesSymbol,
    bool UsesNegativeOptions,
    bool GeneratesFormat);

public static class FormatCellsNumberControlPlanner
{
    public static FormatCellsNumberControlAvailability Plan(string? category)
    {
        var generatesFormat = category is "Number" or "Currency" or "Accounting" or "Percentage" or "Scientific";
        return new FormatCellsNumberControlAvailability(
            UsesDecimals: generatesFormat,
            UsesSymbol: category is "Currency" or "Accounting",
            UsesNegativeOptions: category is "Number" or "Currency",
            GeneratesFormat: generatesFormat);
    }
}
