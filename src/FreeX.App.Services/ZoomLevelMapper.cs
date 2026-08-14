namespace FreeX.App.Services;

public static class ZoomLevelMapper
{
    public const double MinZoomPercent = 10.0;
    public const double DefaultZoomPercent = 100.0;
    public const double MaxZoomPercent = 400.0;

    private static readonly ZoomPercentPolicy Policy = new(
        MinZoomPercent,
        DefaultZoomPercent,
        MaxZoomPercent);

    public static double ClampZoomPercent(double zoomPercent) =>
        Policy.ClampPercent(zoomPercent);

    public static double SliderToZoomPercent(double sliderValue) =>
        Policy.SliderToPercent(sliderValue);

    public static double ZoomPercentToSlider(double zoomPercent) =>
        Policy.PercentToSlider(zoomPercent);

    public static bool TryParseZoomPercent(string? text, out double zoomPercent) =>
        Policy.TryParsePercentInRange(text, out zoomPercent);

    public static bool TryNormalizeWholeZoomPercent(double zoomPercent, out int wholePercent) =>
        Policy.TryNormalizeWholePercent(zoomPercent, out wholePercent);

    /// <summary>
    /// Excel's Zoom-dialog custom-percent route: parse, reject anything outside 10..400%, and require
    /// a whole percent. The parse/classify decision itself lives in the shared
    /// <see cref="ZoomPercentPolicy"/> so FreeW's Zoom dialog runs the same code.
    /// </summary>
    public static bool TryResolveWholeZoomPercent(
        string? text,
        out int wholePercent,
        out ZoomPercentInputError error) =>
        Policy.TryResolveWholePercent(text, ZoomPercentRangeMode.Reject, out wholePercent, out error);

    public static string FormatWholeZoomPercentLabel(int zoomPercent) =>
        Policy.FormatPercentLabel(zoomPercent);

    public static string FormatZoomPercent(double zoomPercent) =>
        Policy.FormatPercentLabel(zoomPercent);

    public static bool IsPresetZoomPercent(int zoomPercent, IEnumerable<int> presets) =>
        Policy.IsPresetPercent(zoomPercent, presets);
}
