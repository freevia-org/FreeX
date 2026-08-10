namespace FreeX.App.Services;

public sealed record ZoomDialogSelection(int ZoomPercent, bool FitSelection = false);

public sealed record ZoomDialogValidationError(string ResourceKey, string FallbackText);

public static class ZoomDialogPlanner
{
    public const string ValidationFallbackResourceKey = "Zoom_EnterAValidZoomPercent";
    public const double Width = 300;
    public const double Height = 240;
    public const double OuterPadding = 12;
    public const double MagnificationGroupPadding = 8;
    public const double MagnificationGroupBottomMargin = 12;
    public const double PresetColumnWidth = 96;
    public const double PresetItemBottomMargin = 4;
    public const double FitSelectionBottomMargin = 10;
    public const double CustomPercentBoxWidth = 72;
    public const double CustomPercentBoxHeight = 24;
    public const double ActionButtonWidth = 72;

    private static readonly int[] PresetValues = [400, 200, 100, 75, 50, 25];

    public static IReadOnlyList<int> Presets => PresetValues;

    public static bool IsPreset(int zoomPercent) =>
        ZoomLevelMapper.IsPresetZoomPercent(zoomPercent, PresetValues);

    public static ZoomDialogSelection CreateFitSelectionResult(int currentZoomPercent) =>
        new(currentZoomPercent, FitSelection: true);

    public static bool TryCreateResult(
        string? input,
        out ZoomDialogSelection result,
        out ZoomDialogValidationError? error)
    {
        result = new ZoomDialogSelection((int)ZoomLevelMapper.DefaultZoomPercent);
        error = null;

        if (!ZoomLevelMapper.TryParseZoomPercent(input, out var zoomPercent))
        {
            error = new ZoomDialogValidationError(
                "Zoom_MustBeBetween10And400",
                "Zoom must be between 10% and 400%.");
            return false;
        }

        if (!ZoomLevelMapper.TryNormalizeWholeZoomPercent(zoomPercent, out var roundedPercent))
        {
            error = new ZoomDialogValidationError(
                "Zoom_MustBeWholePercentBetween10And400",
                "Zoom must be a whole percent between 10% and 400%.");
            return false;
        }

        result = new ZoomDialogSelection(roundedPercent);
        return true;
    }
}
