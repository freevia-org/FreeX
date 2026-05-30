using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class SparklineDialogPlanner
{
    public static SparklineDialogResult CreateResult(
        string dataRangeText,
        string locationText,
        SparklineKindChoice kind) =>
        new(dataRangeText.Trim(), locationText.Trim(), kind);

    public static SparklineRangeSelectionRequest CreateRangeSelectionRequest(
        SparklineRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    public static string GetKindLabel(SparklineKindChoice kind) =>
        kind == SparklineKindChoice.WinLoss ? UiText.Get("Sparkline_KindWinLoss") : kind.ToString();

    public static SparklineDialogValidationResult ValidateInputs(
        string dataRangeText,
        string locationText,
        SheetId sheetId)
    {
        if (!SparklineInputParser.TryParseDataRange(dataRangeText, sheetId, out _))
            return SparklineDialogValidationResult.InvalidDataRange;

        return SparklineInputParser.TryParseLocation(locationText, sheetId, out _)
            ? SparklineDialogValidationResult.Valid
            : SparklineDialogValidationResult.InvalidLocation;
    }
}

public enum SparklineDialogValidationResult
{
    Valid,
    InvalidDataRange,
    InvalidLocation
}
