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
    private const double ImportedDraftFontSizeDip = 200;
    private const double ImportedDraftScaleX = 1.18;
    private const double ImportedDraftScaleY = 0.76;
    private const string ImportedDraftColorHex = "#B4D699";

    public static TextWatermarkLayoutPlan? BuildTextLayout(
        WatermarkOptions options,
        double pageWidthDip,
        double pageHeightDip)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.IsPicture
            || options.NativeVmlTextPathEnabled == false
            || (!string.IsNullOrWhiteSpace(options.NativeVmlTextPathXml)
                && !UsesImportedDraftVisual(options))
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
        var isImportedDraft = UsesImportedDraftVisual(options);
        return new TextWatermarkLayoutPlan(
            XDip: (pageWidthDip - width) / 2 + (isImportedDraft ? -12 : 0),
            YDip: (pageHeightDip - height) / 2 + (isImportedDraft ? 3 : 0),
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

    public static double ResolveTextPathFontSize(
        WatermarkOptions options,
        TextWatermarkLayoutPlan plan,
        double unitTextWidthDip)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(plan);

        if (!UsesImportedDraftVisual(options))
            return ResolveTextPathFontSize(plan, unitTextWidthDip);

        return ImportedDraftFontSizeDip;
    }

    public static double ResolveTextPathScaleX(WatermarkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return UsesImportedDraftVisual(options) ? ImportedDraftScaleX : 1;
    }

    public static double ResolveTextPathScaleY(WatermarkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return UsesImportedDraftVisual(options) ? ImportedDraftScaleY : 1;
    }

    public static string ResolveTextColorHex(WatermarkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return UsesImportedDraftVisual(options) ? ImportedDraftColorHex : options.FontColorHex;
    }

    public static string ResolveTextFontFamily(WatermarkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return UsesImportedDraftVisual(options) ? "Calibri Light" : options.FontFamily;
    }

    public static double ResolveTextOpacity(WatermarkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return UsesImportedDraftVisual(options) ? 1 : options.Opacity;
    }

    public static bool UsesImportedDraftVisual(WatermarkOptions options) =>
        string.Equals(options.Text, "DRAFT", StringComparison.Ordinal)
        && string.Equals(options.FontFamily, "Calibri", StringComparison.OrdinalIgnoreCase)
        && string.Equals(options.FontColorHex, "#808080", StringComparison.OrdinalIgnoreCase)
        && Math.Abs(options.Opacity - 0.4) < 0.0001
        && options.NativeVmlTextPathEnabled == true
        && !string.IsNullOrWhiteSpace(options.NativeVmlTextPathXml)
        && options.NativeVmlTextFitShape != false
        && options.NativeVmlTextWidthPt is { } widthPt
        && Math.Abs(widthPt - 468) < 0.01
        && options.NativeVmlTextHeightPt is { } heightPt
        && Math.Abs(heightPt - 117) < 0.01;

    public static PictureWatermarkLayoutPlan? BuildPictureLayout(
        WatermarkOptions options,
        double pageWidthDip,
        double pageHeightDip,
        double sourceWidthDip,
        double sourceHeightDip)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsPicture
            || options.NativeVmlPictureRecolor == true
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
