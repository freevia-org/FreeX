using FreeX.App.Presentation.Dialogs;
using FreeX.App.Presentation.Localization;
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
        var success = TryParseWithPresentation(
            sheetId,
            setCellText,
            targetValueText,
            changingCellText,
            out input,
            out var presentation);
        error = presentation?.Message.Resolve(UiText.Get, UiText.Format) ?? "";
        return success;
    }

    public static bool TryParseWithPresentation(
        SheetId sheetId,
        string setCellText,
        string targetValueText,
        string changingCellText,
        out GoalSeekDialogInput input,
        out ValidationPresentationDescriptor<GoalSeekValidationFocusTarget>? presentation)
    {
        var result = GoalSeekRequestParser.Parse(
            sheetId,
            setCellText,
            targetValueText,
            changingCellText);
        if (result.Request is { } request)
        {
            input = new GoalSeekDialogInput(request.SetCell, request.TargetValue, request.ChangingCell);
            presentation = null;
            return true;
        }

        input = default!;
        presentation = GoalSeekStatusDialogPlanner.DescribeValidationError(result, GoalSeekPresentationProfile.Wpf);
        return false;
    }
}
