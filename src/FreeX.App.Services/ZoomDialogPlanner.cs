namespace FreeX.App.Services;

public sealed record ZoomDialogSelection(int ZoomPercent, bool FitSelection = false);

public sealed record ZoomDialogValidationError(string ResourceKey, string FallbackText);

public static class ZoomDialogPlanner
{
    public const string ValidationFallbackResourceKey = "Zoom_EnterAValidZoomPercent";

    private static readonly int[] PresetValues = [400, 200, 100, 75, 50, 25];

    public static IReadOnlyList<int> Presets => PresetValues;

    public static bool IsPreset(int zoomPercent) =>
        ZoomLevelMapper.IsPresetZoomPercent(zoomPercent, PresetValues);

    public static string FormatPresetLabel(int zoomPercent) =>
        ZoomLevelMapper.FormatZoomPercent(zoomPercent);

    public static ZoomDialogSelection CreateFitSelectionResult(int currentZoomPercent) =>
        new(currentZoomPercent, FitSelection: true);

    public static bool TryCreateResult(
        string? input,
        out ZoomDialogSelection result,
        out ZoomDialogValidationError? error)
    {
        result = new ZoomDialogSelection((int)ZoomLevelMapper.DefaultZoomPercent);
        error = null;

        if (!ZoomLevelMapper.TryResolveWholeZoomPercent(input, out var roundedPercent, out var inputError))
        {
            error = MessageFor(inputError);
            return false;
        }

        result = new ZoomDialogSelection(roundedPercent);
        return true;
    }

    /// <summary>
    /// Projects the shared <see cref="ZoomPercentInputError"/> taxonomy onto Excel's two Zoom-dialog
    /// messages: everything that is not "in range but fractional" reads as the range message.
    /// </summary>
    private static ZoomDialogValidationError MessageFor(ZoomPercentInputError error) =>
        error == ZoomPercentInputError.NotWholePercent
            ? new ZoomDialogValidationError(
                "Zoom_MustBeWholePercentBetween10And400",
                "Zoom must be a whole percent between 10% and 400%.")
            : new ZoomDialogValidationError(
                "Zoom_MustBeBetween10And400",
                "Zoom must be between 10% and 400%.");
}
