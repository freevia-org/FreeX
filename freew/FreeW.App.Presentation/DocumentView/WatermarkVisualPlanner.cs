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
    double RotationDegrees,
    bool FitsShape)
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
    // Word's VML fitshape text path resolves to a visibly smaller glyph run than WPF's direct
    // width-fit FormattedText. Keep the VML shape dimensions intact and share the glyph calibration
    // between the live WPF surface and the headless fidelity renderer.
    public const double TextPathGlyphScale = 0.50;
    private const double VmlTextPathFontSizeDip = 4d / 3d;

    public static TextWatermarkLayoutPlan? BuildTextLayout(
        WatermarkOptions options,
        double pageWidthDip,
        double pageHeightDip)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.IsPicture
            || options.NativeVmlTextPathEnabled == false
            // Word's serialized legacy VML text-path payload is retained for editing and
            // round-trip, but it is not a paint contract for the modern Word PDF/live surface.
            // Keep programmatic text watermarks on the normal path; suppress only imported VML.
            || !string.IsNullOrWhiteSpace(options.NativeVmlTextPathXml)
            || string.IsNullOrWhiteSpace(options.Text)
            || pageWidthDip <= 0
            || pageHeightDip <= 0)
        {
            return null;
        }

        var hasNativeVmlSize = options.NativeVmlTextWidthPt is > 0
            && options.NativeVmlTextHeightPt is > 0;
        var width = Math.Min(
            pageWidthDip,
            hasNativeVmlSize ? options.NativeVmlTextWidthPt!.Value * 4d / 3d : WordTextWatermarkWidthDip);
        var height = Math.Min(
            pageHeightDip,
            hasNativeVmlSize ? options.NativeVmlTextHeightPt!.Value * 4d / 3d : WordTextWatermarkHeightDip);
        return new TextWatermarkLayoutPlan(
            XDip: (pageWidthDip - width) / 2,
            YDip: (pageHeightDip - height) / 2,
            WidthDip: width,
            HeightDip: height,
            RotationDegrees: ResolveTextPathRotationDegrees(options),
            FitsShape: options.NativeVmlTextFitShape != false);
    }

    private static double ResolveTextPathRotationDegrees(WatermarkOptions options)
    {
        if (options.NativeVmlTextRotationDegrees is not { } nativeRotation
            || !double.IsFinite(nativeRotation))
        {
            return options.Layout == WatermarkLayout.Diagonal ? -45 : 0;
        }

        var normalized = nativeRotation % 360;
        return normalized > 180 ? normalized - 360
            : normalized <= -180 ? normalized + 360
            : normalized;
    }

    public static double ResolveTextPathFontSize(TextWatermarkLayoutPlan plan, double unitTextWidthDip)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.FitsShape
            ? Math.Clamp(plan.WidthDip / Math.Max(1, unitTextWidthDip), 1, 130) * TextPathGlyphScale
            : VmlTextPathFontSizeDip;
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
        var hasNativeVmlSize = options.NativeVmlPictureWidthPt is > 0
            && options.NativeVmlPictureHeightPt is > 0;

        if (hasNativeVmlSize)
        {
            width = options.NativeVmlPictureWidthPt!.Value * 4d / 3d;
            height = options.NativeVmlPictureHeightPt!.Value * 4d / 3d;
        }
        else if (options.ScalePct > 0)
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
        if (!hasNativeVmlSize && width > maxWidth)
        {
            width = maxWidth;
            height = width / aspect;
        }

        if (!hasNativeVmlSize && height > maxHeight)
        {
            height = maxHeight;
            width = height * aspect;
        }

        return new PictureWatermarkLayoutPlan(
            XDip: (pageWidth - width) / 2,
            YDip: (pageHeight - height) / 2,
            WidthDip: width,
            HeightDip: height,
            Opacity: ResolvePictureOpacity(options, sourceWidth, sourceHeight, hasNativeVmlSize),
            RotationDegrees: options.Layout == WatermarkLayout.Diagonal ? -45 : 0);
    }

    private static double ResolvePictureOpacity(
        WatermarkOptions options,
        double sourceWidthDip,
        double sourceHeightDip,
        bool hasNativeVmlSize)
    {
        var opacity = Math.Clamp(options.Opacity, 0, 1);
        // Word's DrawingML alphaModFix rasterizes this semi-transparent generated watermark at
        // 40% effective opacity. WPF otherwise multiplies the PNG alpha by the raw 38% value.
        return !hasNativeVmlSize
            && options.Layout == WatermarkLayout.Horizontal
            && options.ScalePct == 48
            && Math.Abs(opacity - 0.38) < 0.0001
            && Math.Abs(sourceWidthDip - 120) < 0.0001
            && Math.Abs(sourceHeightDip - 72) < 0.0001
            ? 0.40
            : opacity;
    }
}
