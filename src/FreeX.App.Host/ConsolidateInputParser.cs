using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class ConsolidateInputParser
{
    public static bool TryParseSourceRanges(
        string input,
        SheetId sheetId,
        out IReadOnlyList<GridRange> ranges,
        out string? invalidPart) =>
        FreeX.App.Presentation.Consolidate.ConsolidateInputParser.TryParseSourceRanges(
            input,
            sheetId,
            out ranges,
            out invalidPart);

    public static bool TryParseSourceRanges(
        string input,
        SheetId defaultSheetId,
        Func<string, SheetId?> resolveSheetId,
        out IReadOnlyList<GridRange> ranges,
        out string? invalidPart) =>
        FreeX.App.Presentation.Consolidate.ConsolidateInputParser.TryParseSourceRanges(
            input,
            defaultSheetId,
            resolveSheetId,
            out ranges,
            out invalidPart);

    public static bool TryParseDestination(string input, SheetId sheetId, out CellAddress destination) =>
        FreeX.App.Presentation.Consolidate.ConsolidateInputParser.TryParseDestination(
            input,
            sheetId,
            out destination);

    public static bool TryParseDestination(
        string input,
        SheetId defaultSheetId,
        Func<string, SheetId?> resolveSheetId,
        out CellAddress destination) =>
        FreeX.App.Presentation.Consolidate.ConsolidateInputParser.TryParseDestination(
            input,
            defaultSheetId,
            resolveSheetId,
            out destination);
}
