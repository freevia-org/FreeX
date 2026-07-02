namespace FreeX.App.Services;

public sealed record FormatCellsNumberControlAvailability(
    bool ShowsType,
    bool ShowsGeneralDescription,
    bool UsesDecimals,
    bool UsesSymbol,
    bool UsesNegativeOptions,
    bool GeneratesFormat);

public static class FormatCellsNumberControlPlanner
{
    public static FormatCellsNumberControlAvailability Plan(string? category)
    {
        var isGeneral = category is "General";
        var generatesFormat = category is "Number" or "Currency" or "Accounting" or "Percentage" or "Scientific";
        return new FormatCellsNumberControlAvailability(
            ShowsType: !isGeneral && !string.IsNullOrWhiteSpace(category),
            ShowsGeneralDescription: isGeneral,
            UsesDecimals: generatesFormat,
            UsesSymbol: category is "Currency" or "Accounting",
            UsesNegativeOptions: category is "Number" or "Currency",
            GeneratesFormat: generatesFormat);
    }
}
