namespace FreeX.App.UI;

public static class ZoomLevelMapper
{
    public const double MinZoomPercent = FreeX.App.Services.ZoomLevelMapper.MinZoomPercent;
    public const double DefaultZoomPercent = FreeX.App.Services.ZoomLevelMapper.DefaultZoomPercent;
    public const double MaxZoomPercent = FreeX.App.Services.ZoomLevelMapper.MaxZoomPercent;

    public static double ClampZoomPercent(double zoomPercent) =>
        FreeX.App.Services.ZoomLevelMapper.ClampZoomPercent(zoomPercent);

    public static double SliderToZoomPercent(double sliderValue) =>
        FreeX.App.Services.ZoomLevelMapper.SliderToZoomPercent(sliderValue);

    public static double ZoomPercentToSlider(double zoomPercent) =>
        FreeX.App.Services.ZoomLevelMapper.ZoomPercentToSlider(zoomPercent);

    public static bool TryParseZoomPercent(string? text, out double zoomPercent) =>
        FreeX.App.Services.ZoomLevelMapper.TryParseZoomPercent(text, out zoomPercent);
}
