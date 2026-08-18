using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class GoToDialog
{
    public static bool TryParseAddress(string text, SheetId sheetId, out CellAddress address)
        => WorkbookReferenceNavigator.TryParseAddress(text, sheetId, out address);

    public static IReadOnlyList<string> BuildReferenceChoices(
        string defaultAddress,
        IEnumerable<string>? recentReferences,
        IEnumerable<string>? definedNames)
        => GoToDialogPlanner.BuildReferenceChoices(defaultAddress, recentReferences, definedNames);

    public static bool TryParseReference(
        string text,
        SheetId sheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        out CellAddress address)
        => WorkbookReferenceNavigator.TryParseReference(text, sheetId, definedNames, out address);

    public static bool TryParseReferenceRange(
        string text,
        SheetId sheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        out GridRange range)
        => GoToDialogPlanner.TryParseReferenceRange(text, sheetId, definedNames, out range);

    public static bool TryParseReferenceRange(
        string text,
        SheetId sheetId,
        Func<string, SheetId?> resolveSheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        Func<string, SheetId, GridRange?>? resolveScopedName,
        out GridRange range)
        => GoToDialogPlanner.TryParseReferenceRange(text, sheetId, resolveSheetId, definedNames, resolveScopedName, out range);
}
