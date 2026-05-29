using System.Globalization;

namespace FreeX.App.UI;

public static class ZoomLevelMapper
{
    public const double MinZoomPercent = 10.0;
    public const double DefaultZoomPercent = 100.0;
    public const double MaxZoomPercent = 400.0;
    private const double MinSliderValue = 0.0;
    private const double MidSliderValue = 100.0;
    private const double MaxSliderValue = 200.0;

    public static double ClampZoomPercent(double zoomPercent) =>
        Clamp(zoomPercent, MinZoomPercent, MaxZoomPercent);

    public static double SliderToZoomPercent(double sliderValue)
    {
        sliderValue = Clamp(sliderValue, MinSliderValue, MaxSliderValue);
        return sliderValue <= MidSliderValue
            ? MinZoomPercent + sliderValue / MidSliderValue * (DefaultZoomPercent - MinZoomPercent)
            : DefaultZoomPercent + (sliderValue - MidSliderValue) / MidSliderValue * (MaxZoomPercent - DefaultZoomPercent);
    }

    public static double ZoomPercentToSlider(double zoomPercent)
    {
        zoomPercent = ClampZoomPercent(zoomPercent);
        return zoomPercent <= DefaultZoomPercent
            ? (zoomPercent - MinZoomPercent) / (DefaultZoomPercent - MinZoomPercent) * 100.0
            : 100.0 + (zoomPercent - DefaultZoomPercent) / (MaxZoomPercent - DefaultZoomPercent) * 100.0;
    }

    public static bool TryParseZoomPercent(string? text, out double zoomPercent)
    {
        zoomPercent = DefaultZoomPercent;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim().TrimEnd('%').Trim();
        if (!double.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) &&
            !double.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
            return false;

        if (parsed < MinZoomPercent || parsed > MaxZoomPercent)
            return false;

        zoomPercent = parsed;
        return true;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));
}
