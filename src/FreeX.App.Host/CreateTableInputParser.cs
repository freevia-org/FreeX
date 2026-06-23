using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class CreateTableInputParser
{
    public static bool TryParse(
        SheetId sheetId,
        string rangeText,
        bool firstRowHasHeaders,
        string tableStyleName,
        out CreateTableDialogResult result,
        out string? error)
    {
        var parsed = CreateTableDialogPlanner.TryParse(
            sheetId,
            rangeText,
            firstRowHasHeaders,
            tableStyleName,
            out var plan,
            out var errorKey);
        if (parsed)
        {
            result = new CreateTableDialogResult(plan.Range, plan.FirstRowHasHeaders, plan.TableStyleName);
            error = null;
            return true;
        }

        result = default!;
        error = errorKey is null ? null : UiText.Get(errorKey);
        return false;
    }
}
