using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed record PasteNamesDialogItem(string Name, string RefersTo);

/// <summary>
/// Host-side, localized adapter over the portable <see cref="PasteNamesPlanner"/> (in
/// <c>FreeX.App.Presentation.DefinedNames</c>): it projects defined names into the host dialog DTO and converts
/// the planner's <see cref="PasteNamesListError"/> outcome into a resource-localized message. All Paste Names
/// projection and edit-planning math lives in the shared portable planner; only the DTO shape and the
/// user-facing error strings are host concerns (the <see cref="AutoFilterCriteriaLabels"/> pattern).
/// </summary>
internal static class PasteNamesPlanner
{
    public static IReadOnlyList<PasteNamesDialogItem> BuildItems(
        Workbook workbook,
        Func<GridRange, string> formatRange)
    {
        return FreeX.App.Presentation.DefinedNames.PasteNamesPlanner
            .BuildItems(workbook, formatRange)
            .Select(item => new PasteNamesDialogItem(item.Name, item.RefersTo))
            .ToList();
    }

    public static bool TryBuildPasteListEdits(
        CellAddress start,
        IReadOnlyList<PasteNamesDialogItem> items,
        out IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(items);

        var portableItems = items
            .Select(item => new PasteNamesItem(item.Name, item.RefersTo))
            .ToList();

        if (FreeX.App.Presentation.DefinedNames.PasteNamesPlanner.TryBuildPasteListEdits(
                start, portableItems, out edits, out var listError))
        {
            error = null;
            return true;
        }

        error = DescribeError(listError);
        return false;
    }

    private static string DescribeError(PasteNamesListError error) => error switch
    {
        PasteNamesListError.NotEnoughColumns => UiText.Get("PasteNames_NotEnoughColumnsMessage"),
        PasteNamesListError.NotEnoughRows => UiText.Get("PasteNames_NotEnoughRowsMessage"),
        _ => UiText.Get("PasteNames_NoNamesMessage"),
    };
}
