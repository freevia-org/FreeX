using System.Globalization;
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
        input = default!;
        error = "";

        var setCellInput = setCellText.Trim();
        if (string.IsNullOrWhiteSpace(setCellInput))
        {
            error = UiText.Get("GoalSeek_SetCellRequiredMessage");
            return false;
        }

        if (!CellAddress.TryParse(setCellInput, sheetId, out var setCell))
        {
            error = UiText.Format("GoalSeek_InvalidCellAddressMessage", setCellInput);
            return false;
        }

        var targetInput = targetValueText.Trim();
        if ((!double.TryParse(targetInput, NumberStyles.Any, CultureInfo.CurrentCulture, out var targetValue) &&
             !double.TryParse(targetInput, NumberStyles.Any, CultureInfo.InvariantCulture, out targetValue)) ||
            !double.IsFinite(targetValue))
        {
            error = UiText.Format("GoalSeek_InvalidNumberMessage", targetInput);
            return false;
        }

        var changingCellInput = changingCellText.Trim();
        if (string.IsNullOrWhiteSpace(changingCellInput))
        {
            error = UiText.Get("GoalSeek_ByChangingCellRequiredMessage");
            return false;
        }

        if (!CellAddress.TryParse(changingCellInput, sheetId, out var changingCell))
        {
            error = UiText.Format("GoalSeek_InvalidCellAddressMessage", changingCellInput);
            return false;
        }

        if (setCell == changingCell)
        {
            error = UiText.Get("GoalSeek_CellsMustDifferMessage");
            return false;
        }

        input = new GoalSeekDialogInput(setCell, targetValue, changingCell);
        return true;
    }
}
