using System.Globalization;

namespace Freexcel.App.UI;

public static class ZoomLevelMapper
{
    public const double MinZoomPercent = 10.0;
    public const double DefaultZoomPercent = 100.0;
    public const double MaxZoomPercent = 400.0;

    public static double ClampZoomPercent(double zoomPercent) =>
        Math.Max(MinZoomPercent, Math.Min(MaxZoomPercent, zoomPercent));

    public static double SliderToZoomPercent(double sliderValue)
    {
        sliderValue = Math.Max(0.0, Math.Min(200.0, sliderValue));
        return sliderValue <= 100.0
            ? MinZoomPercent + sliderValue / 100.0 * (DefaultZoomPercent - MinZoomPercent)
            : DefaultZoomPercent + (sliderValue - 100.0) / 100.0 * (MaxZoomPercent - DefaultZoomPercent);
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
}
