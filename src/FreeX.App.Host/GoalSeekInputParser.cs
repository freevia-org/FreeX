using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record GoalSeekDialogInput(CellAddress SetCell, double TargetValue, CellAddress ChangingCell);

public static class GoalSeekInputParser
{
    public static bool TryParse(
        SheetId sheetId,
        string setCellText,
        string targetValueText,
        string changingCellText,
        out GoalSeekDialogInput input,
        out string error)
    {
        var result = GoalSeekRequestParser.Parse(
            sheetId,
            setCellText,
            targetValueText,
            changingCellText);
        if (result.Request is { } request)
        {
            input = new GoalSeekDialogInput(request.SetCell, request.TargetValue, request.ChangingCell);
            error = "";
            return true;
        }

        input = default!;
        error = CreateDialogError(result);
        return false;
    }

    private static string CreateDialogError(GoalSeekRequestParseResult result) =>
        result.Error switch
        {
            GoalSeekRequestParseError.SetCellRequired => UiText.Get("GoalSeek_SetCellRequiredMessage"),
            GoalSeekRequestParseError.InvalidSetCellAddress => UiText.Format(
                "GoalSeek_InvalidCellAddressMessage",
                result.InvalidText),
            GoalSeekRequestParseError.InvalidTargetValue => UiText.Format(
                "GoalSeek_InvalidNumberMessage",
                result.InvalidText),
            GoalSeekRequestParseError.ChangingCellRequired => UiText.Get("GoalSeek_ByChangingCellRequiredMessage"),
            GoalSeekRequestParseError.InvalidChangingCellAddress => UiText.Format(
                "GoalSeek_InvalidCellAddressMessage",
                result.InvalidText),
            GoalSeekRequestParseError.CellsMustDiffer => UiText.Get("GoalSeek_CellsMustDifferMessage"),
            _ => ""
        };
}
