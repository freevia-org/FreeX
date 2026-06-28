using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class AdvancedFilterInputParser
{
    public static bool TryParseRange(
        SheetId defaultSheetId,
        string? input,
        Func<string, SheetId?> resolveSheetId,
        out GridRange range) =>
        FreeX.App.Services.AdvancedFilterPlanner.TryParseRange(
            defaultSheetId,
            input,
            resolveSheetId,
            out range);

    public static bool TryParseCopyDestination(
        string? input,
        SheetId sheetId,
        out CellAddress? destination) =>
        FreeX.App.Services.AdvancedFilterPlanner.TryParseCopyDestination(
            input,
            sheetId,
            out destination);

    public static bool TryParseCopyDestinationRange(
        string? input,
        SheetId sheetId,
        out GridRange? destination) =>
        FreeX.App.Services.AdvancedFilterPlanner.TryParseCopyDestinationRange(
            input,
            sheetId,
            out destination);

    public static bool ParseUniqueOnly(string? input) =>
        FreeX.App.Services.AdvancedFilterPlanner.ParseUniqueRecordsOnly(input);
}
