using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public sealed record PictureWatermarkLayoutPlan(
    double XDip,
    double YDip,
    double WidthDip,
    double HeightDip,
    double Opacity,
    double RotationDegrees)
{
    public double CenterXDip => XDip + WidthDip / 2;
    public double CenterYDip => YDip + HeightDip / 2;
}

public sealed record TextWatermarkLayoutPlan(
    double XDip,
    double YDip,
    double WidthDip,
    double HeightDip,
    double RotationDegrees)
{
    public double CenterXDip => XDip + WidthDip / 2;
    public double CenterYDip => YDip + HeightDip / 2;
}

public static class WatermarkVisualPlanner
{
    // FreeW writes text watermarks as Word's conventional VML text-path shape. Its 468 x 117pt
    // extent, rather than a tiled label, determines the visible text scale and placement.
    private const double WordTextWatermarkWidthDip = 624;
    private const double WordTextWatermarkHeightDip = 156;
    public const double TextPathGlyphScale = 0.50;

    public static TextWatermarkLayoutPlan? BuildTextLayout(
        WatermarkOptions options,
        double pageWidthDip,
        double pageHeightDip)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.IsPicture
            || string.IsNullOrWhiteSpace(options.Text)
            || pageWidthDip <= 0
            || pageHeightDip <= 0)
        {
            return null;
        }

        var width = Math.Min(pageWidthDip, WordTextWatermarkWidthDip);
        var height = Math.Min(pageHeightDip, WordTextWatermarkHeightDip);
        return new TextWatermarkLayoutPlan(
            XDip: (pageWidthDip - width) / 2,
            YDip: (pageHeightDip - height) / 2,
            WidthDip: width,
            HeightDip: height,
            RotationDegrees: options.Layout == WatermarkLayout.Diagonal ? -45 : 0);
    }

    public static PictureWatermarkLayoutPlan? BuildPictureLayout(
        WatermarkOptions options,
        double pageWidthDip,
        double pageHeightDip,
        double sourceWidthDip,
        double sourceHeightDip)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsPicture
            || pageWidthDip <= 0
            || pageHeightDip <= 0
            || sourceWidthDip <= 0
            || sourceHeightDip <= 0)
        {
            return null;
        }

        var pageWidth = Math.Max(1, pageWidthDip);
        var pageHeight = Math.Max(1, pageHeightDip);
        var sourceWidth = Math.Max(1, sourceWidthDip);
        var sourceHeight = Math.Max(1, sourceHeightDip);
        var aspect = sourceWidth / sourceHeight;
        double width;
        double height;

        if (options.ScalePct > 0)
        {
            var scale = Math.Clamp(options.ScalePct, 1, 500) / 100.0;
            if (aspect >= 1)
            {
                width = pageWidth * scale;
                height = width / aspect;
            }
            else
            {
                height = pageHeight * scale;
                width = height * aspect;
            }
        }
        else
        {
            var fitScale = Math.Min(pageWidth * 0.65 / sourceWidth, pageHeight * 0.65 / sourceHeight);
            width = sourceWidth * Math.Max(0.01, fitScale);
            height = sourceHeight * Math.Max(0.01, fitScale);
        }

        var maxWidth = pageWidth * 0.95;
        var maxHeight = pageHeight * 0.95;
        if (width > maxWidth)
        {
            width = maxWidth;
            height = width / aspect;
        }

        if (height > maxHeight)
        {
            height = maxHeight;
            width = height * aspect;
        }

        return new PictureWatermarkLayoutPlan(
            XDip: (pageWidth - width) / 2,
            YDip: (pageHeight - height) / 2,
            WidthDip: width,
            HeightDip: height,
            Opacity: Math.Clamp(options.Opacity, 0, 1),
            RotationDegrees: options.Layout == WatermarkLayout.Diagonal ? -45 : 0);
    }
}
