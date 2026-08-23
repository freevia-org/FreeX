using System.Globalization;
using Free.Shared.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class GoalSeekRequestParser
{
    public static GoalSeekRequestParseResult Parse(
        SheetId sheetId,
        string? setCellText,
        string? targetValueText,
        string? changingCellText)
    {
        var setCellInput = NormalizeInput(setCellText);
        if (string.IsNullOrWhiteSpace(setCellInput))
            return GoalSeekRequestParseResult.Invalid(GoalSeekRequestParseError.SetCellRequired);

        if (!CellAddress.TryParse(setCellInput, sheetId, out var setCell))
            return GoalSeekRequestParseResult.Invalid(
                GoalSeekRequestParseError.InvalidSetCellAddress,
                setCellInput);

        var targetInput = NormalizeInput(targetValueText);
        if (!TryParseTargetValue(targetInput, out var targetValue))
            return GoalSeekRequestParseResult.Invalid(
                GoalSeekRequestParseError.InvalidTargetValue,
                targetInput);

        var changingCellInput = NormalizeInput(changingCellText);
        if (string.IsNullOrWhiteSpace(changingCellInput))
            return GoalSeekRequestParseResult.Invalid(GoalSeekRequestParseError.ChangingCellRequired);

        if (!CellAddress.TryParse(changingCellInput, sheetId, out var changingCell))
            return GoalSeekRequestParseResult.Invalid(
                GoalSeekRequestParseError.InvalidChangingCellAddress,
                changingCellInput);

        if (setCell == changingCell)
            return GoalSeekRequestParseResult.Invalid(GoalSeekRequestParseError.CellsMustDiffer);

        return GoalSeekRequestParseResult.Valid(new GoalSeekRequest(setCell, targetValue, changingCell));
    }

    public static bool TryParse(
        SheetId sheetId,
        string? setCellText,
        string? targetValueText,
        string? changingCellText,
        out GoalSeekRequest request,
        out GoalSeekRequestParseResult result)
    {
        result = Parse(sheetId, setCellText, targetValueText, changingCellText);
        if (result.Request is { } parsedRequest)
        {
            request = parsedRequest;
            return true;
        }

        request = default!;
        return false;
    }

    private static bool TryParseTargetValue(string targetInput, out double targetValue) =>
        (TryParseFiniteNumber(targetInput, CultureInfo.CurrentCulture, out targetValue) ||
         TryParseFiniteNumber(targetInput, CultureInfo.InvariantCulture, out targetValue)) &&
        double.IsFinite(targetValue);

    private static bool TryParseFiniteNumber(string targetInput, IFormatProvider formatProvider, out double value) =>
        double.TryParse(targetInput, NumberStyles.Any, formatProvider, out value) &&
        NumericTextGroupingValidator.HasValidGroupingShape(targetInput, formatProvider);

    private static string NormalizeInput(string? input) => input?.Trim() ?? "";
}
