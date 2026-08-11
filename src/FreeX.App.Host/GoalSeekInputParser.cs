using FreeX.App.Presentation.Dialogs;
using Free.Shared.Localization;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class GoalSeekInputParser
{
    public static bool TryParse(
        SheetId sheetId,
        string setCellText,
        string targetValueText,
        string changingCellText,
        out GoalSeekRequest input,
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
        out GoalSeekRequest input,
        out ValidationPresentationDescriptor<GoalSeekValidationFocusTarget>? presentation)
    {
        var result = GoalSeekRequestParser.Parse(
            sheetId,
            setCellText,
            targetValueText,
            changingCellText);
        if (result.Request is { } request)
        {
            input = request;
            presentation = null;
            return true;
        }

        input = default!;
        presentation = GoalSeekStatusDialogPlanner.DescribeValidationError(result, GoalSeekPresentationProfile.Wpf);
        return false;
    }
}
