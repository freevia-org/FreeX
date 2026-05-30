namespace FreeX.App.Host;

internal sealed record HomeNumberFormatDropdownOption(string Label, string? Code, bool OpensFormatCellsDialog = false);

internal static class HomeNumberFormatDropdownPlanner
{
    public const string MoreNumberFormatsLabel = "More Number Formats...";

    public static IReadOnlyList<HomeNumberFormatDropdownOption> Options { get; } =
        FormatCellsNumberFormatPlanner.Options
            .GroupBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(option => new HomeNumberFormatDropdownOption(option.Label, option.Code))
            .Append(new HomeNumberFormatDropdownOption(MoreNumberFormatsLabel, null, OpensFormatCellsDialog: true))
            .ToArray();

    public static int DefaultSelectionIndex => 0;
}
