using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
            error = "Please enter the Set cell address.";
            return false;
        }

        if (!CellAddress.TryParse(setCellInput, sheetId, out var setCell))
        {
            error = $"'{setCellInput}' is not a valid cell address.";
            return false;
        }

        var targetInput = targetValueText.Trim();
        if ((!double.TryParse(targetInput, NumberStyles.Any, CultureInfo.CurrentCulture, out var targetValue) &&
             !double.TryParse(targetInput, NumberStyles.Any, CultureInfo.InvariantCulture, out targetValue)) ||
            !double.IsFinite(targetValue))
        {
            error = $"'{targetInput}' is not a valid number.";
            return false;
        }

        var changingCellInput = changingCellText.Trim();
        if (string.IsNullOrWhiteSpace(changingCellInput))
        {
            error = "Please enter the By changing cell address.";
            return false;
        }

        if (!CellAddress.TryParse(changingCellInput, sheetId, out var changingCell))
        {
            error = $"'{changingCellInput}' is not a valid cell address.";
            return false;
        }

        if (setCell == changingCell)
        {
            error = "The Set cell and the By changing cell must be different.";
            return false;
        }

        input = new GoalSeekDialogInput(setCell, targetValue, changingCell);
        return true;
    }
}
