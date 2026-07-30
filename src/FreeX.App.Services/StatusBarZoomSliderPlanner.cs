namespace FreeX.App.Services;

public sealed record StatusBarZoomSliderPlan(
    int ZoomPercent,
    string ZoomText,
    double MinimumSliderValue,
    double MaximumSliderValue,
    double SliderValue,
    double SmallChange,
    double LargeChange,
    double SliderWidth,
    double SliderHeight,
    IReadOnlyList<double> SliderTickValues,
    IReadOnlyList<double> VisualTickLefts);

public sealed record StatusBarZoomSliderInputPlan(
    double SliderValue,
    int ZoomPercent,
    bool SnappedToDefault);

public sealed record StatusBarZoomSliderThumbPlan(
    double Left,
    double Normalized);

public static class StatusBarZoomSliderPlanner
{
    public const int ZoomStepPercent = 10;
    public const double SliderWidth = 120d;
    public const double SliderHeight = 22d;
    public const double TrackHorizontalInset = 8d;
    public const double ThumbWidth = 9d;
    public const double ThumbHeight = 16d;
    public const double SmallChange = 5d;
    public const double LargeChange = 10d;
    public const double SnapToDefaultTolerance = 3d;

    private static readonly double[] SliderTickValues =
    [
        ZoomLevelMapper.ZoomPercentToSlider(ZoomLevelMapper.DefaultZoomPercent)
    ];

    private static readonly double[] VisualTickLefts =
    [
        TrackHorizontalInset,
        SliderWidth / 2d,
        SliderWidth - TrackHorizontalInset - 1d
    ];

    public static StatusBarZoomSliderPlan Build(int zoomPercent)
    {
        var clampedZoomPercent = ClampZoomPercent(zoomPercent);
        return new StatusBarZoomSliderPlan(
            clampedZoomPercent,
            FormatZoomPercent(clampedZoomPercent),
            ZoomLevelMapper.ZoomPercentToSlider(ZoomLevelMapper.MinZoomPercent),
            ZoomLevelMapper.ZoomPercentToSlider(ZoomLevelMapper.MaxZoomPercent),
            ZoomLevelMapper.ZoomPercentToSlider(clampedZoomPercent),
            SmallChange,
            LargeChange,
            SliderWidth,
            SliderHeight,
            SliderTickValues,
            VisualTickLefts);
    }

    public static StatusBarZoomSliderInputPlan BuildInput(double sliderValue)
    {
        var effectiveSliderValue = SnapSliderValue(sliderValue, out var snappedToDefault);
        return new StatusBarZoomSliderInputPlan(
            effectiveSliderValue,
            ClampZoomPercent((int)Math.Round(ZoomLevelMapper.SliderToZoomPercent(effectiveSliderValue))),
            snappedToDefault);
    }

    public static StatusBarZoomSliderThumbPlan BuildThumbPlan(
        double sliderValue,
        double hostWidth = SliderWidth,
        double thumbWidth = ThumbWidth)
    {
        if (!double.IsFinite(hostWidth) || hostWidth <= 0d)
            hostWidth = SliderWidth;
        if (!double.IsFinite(thumbWidth) || thumbWidth <= 0d)
            thumbWidth = ThumbWidth;

        var min = ZoomLevelMapper.ZoomPercentToSlider(ZoomLevelMapper.MinZoomPercent);
        var max = ZoomLevelMapper.ZoomPercentToSlider(ZoomLevelMapper.MaxZoomPercent);
        var clamped = double.IsFinite(sliderValue)
            ? Math.Clamp(sliderValue, min, max)
            : ZoomLevelMapper.ZoomPercentToSlider(ZoomLevelMapper.DefaultZoomPercent);
        var normalized = max <= min ? 0d : (clamped - min) / (max - min);
        var trackWidth = Math.Max(1d, hostWidth - (TrackHorizontalInset * 2d));
        var left = TrackHorizontalInset + (normalized * trackWidth) - (thumbWidth / 2d);
        var maxLeft = Math.Max(0d, hostWidth - thumbWidth);
        return new StatusBarZoomSliderThumbPlan(Math.Clamp(left, 0d, maxLeft), normalized);
    }

    public static int ClampZoomPercent(int zoomPercent) =>
        (int)Math.Round(ZoomLevelMapper.ClampZoomPercent(zoomPercent));

    public static string FormatZoomPercent(int zoomPercent) =>
        ZoomLevelMapper.FormatZoomPercent(zoomPercent);

    public static double SnapSliderValue(double sliderValue, out bool snappedToDefault)
    {
        var defaultSliderValue = SliderTickValues[0];
        snappedToDefault = Math.Abs(sliderValue - defaultSliderValue) < SnapToDefaultTolerance;
        return snappedToDefault ? defaultSliderValue : sliderValue;
    }
}
