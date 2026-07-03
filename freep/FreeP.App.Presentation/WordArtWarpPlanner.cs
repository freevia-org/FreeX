using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

public enum WordArtWarpFamily
{
    Arch,
    Circle,
    Wave,
    Triangle,
    Slant,
    Can,
    Button,
    Inflate,
    Deflate
}

public sealed record WordArtWarpTransform(
    WordArtWarpFamily Family,
    double SampleProgress,
    double AmplitudeScale,
    double OffsetYDip,
    double RotationDeg,
    double ScaleY)
{
    public bool HasAffineTransform =>
        Math.Abs(RotationDeg) > 0.001 ||
        Math.Abs(ScaleY - 1.0) > 0.001;

    public bool HasOffset => Math.Abs(OffsetYDip) > 0.001;
}

public static class WordArtWarpPlanner
{
    private const double DefaultAmplitudeFraction = 0.35;
    private const double MaxRotationDeg = 18.0;

    public static double? ComputeYOffset(string? preset, double horizontalPosition, LayoutRect shapeBounds) =>
        ComputeYOffset(preset, horizontalPosition, shapeBounds.Height);

    public static double? ComputeYOffset(string? preset, double horizontalPosition, double shapeHeightDip)
    {
        if (TryClassifyPreset(preset, out var family, out var direction) is false)
            return null;

        double t = Math.Clamp(horizontalPosition, 0.0, 1.0);
        double h = shapeHeightDip;
        return family switch
        {
            WordArtWarpFamily.Arch =>
                direction * h * DefaultAmplitudeFraction * 4 * t * (1 - t),
            WordArtWarpFamily.Circle =>
                -h * DefaultAmplitudeFraction * Math.Sin(t * Math.PI),
            WordArtWarpFamily.Wave =>
                Math.Sign(direction) * h * (Math.Abs(direction) > 1 ? 0.10 : 0.15)
                    * Math.Sin(t * 2 * Math.PI * (Math.Abs(direction) > 1 ? 2 : 1)),
            WordArtWarpFamily.Triangle =>
                direction * h * DefaultAmplitudeFraction * (0.5 - t),
            WordArtWarpFamily.Slant =>
                direction * h * 0.3 * t,
            WordArtWarpFamily.Can =>
                direction * h * DefaultAmplitudeFraction * Math.Sin(t * Math.PI),
            WordArtWarpFamily.Button =>
                direction * h * 0.22 * (1 - Math.Abs(2 * t - 1)),
            WordArtWarpFamily.Inflate =>
                -h * 0.18 * Math.Sin(t * Math.PI),
            WordArtWarpFamily.Deflate =>
                h * 0.18 * Math.Sin(t * Math.PI),
            _ => null
        };
    }

    public static WordArtWarpTransform? Plan(
        string? preset,
        LayoutRect runBoundsDip,
        LayoutRect shapeBoundsDip,
        IReadOnlyList<(string Name, string Formula)> warpAdjusts)
    {
        if (TryClassifyPreset(preset, out var family, out _) is false)
            return null;

        double progress = ComputeRunCenterProgress(runBoundsDip, shapeBoundsDip);
        double scale = GetAdjustAmplitudeScale(warpAdjusts);
        double offset = (ComputeYOffset(preset, progress, shapeBoundsDip.Height) ?? 0.0) * scale;
        double rotation = ComputeRotationDeg(preset, progress, shapeBoundsDip, scale);
        double scaleY = ComputeScaleY(family, progress, scale);

        return new WordArtWarpTransform(
            family,
            progress,
            scale,
            offset,
            rotation,
            scaleY);
    }

    public static bool TryClassifyPreset(
        string? preset,
        out WordArtWarpFamily family,
        out double direction)
    {
        family = WordArtWarpFamily.Arch;
        direction = 0;

        if (string.IsNullOrWhiteSpace(preset))
            return false;

        switch (preset.Trim().ToLowerInvariant())
        {
            case "textarchup":
            case "textcirclecurve":
                family = WordArtWarpFamily.Arch;
                direction = -1;
                return true;
            case "textarchdown":
            case "textarchdownpour":
                family = WordArtWarpFamily.Arch;
                direction = 1;
                return true;
            case "textcircle":
                family = WordArtWarpFamily.Circle;
                direction = -1;
                return true;
            case "textwaveup":
            case "textwave1":
            case "textwaves":
                family = WordArtWarpFamily.Wave;
                direction = -1;
                return true;
            case "textwavedown":
                family = WordArtWarpFamily.Wave;
                direction = 1;
                return true;
            case "textwave2":
                family = WordArtWarpFamily.Wave;
                direction = -2;
                return true;
            case "texttriangle":
            case "texttrianglepour":
                family = WordArtWarpFamily.Triangle;
                direction = 1;
                return true;
            case "textinversetriangle":
                family = WordArtWarpFamily.Triangle;
                direction = -1;
                return true;
            case "textslantup":
                family = WordArtWarpFamily.Slant;
                direction = -1;
                return true;
            case "textslantdown":
                family = WordArtWarpFamily.Slant;
                direction = 1;
                return true;
            case "textcantop":
            case "textcan":
                family = WordArtWarpFamily.Can;
                direction = -1;
                return true;
            case "textcanbottom":
                family = WordArtWarpFamily.Can;
                direction = 1;
                return true;
            case "textbutton":
            case "textbuttonpour":
                family = WordArtWarpFamily.Button;
                direction = 1;
                return true;
            case "textbuttoninvert":
                family = WordArtWarpFamily.Button;
                direction = -1;
                return true;
            case "textinflate":
            case "textinflatebottom":
            case "textinflateslanted":
                family = WordArtWarpFamily.Inflate;
                direction = -1;
                return true;
            case "textdeflate":
            case "textdeflatebottom":
            case "textdeflateslanted":
                family = WordArtWarpFamily.Deflate;
                direction = 1;
                return true;
            default:
                return false;
        }
    }

    public static double GetAdjustAmplitudeScale(
        IReadOnlyList<(string Name, string Formula)> warpAdjusts)
    {
        foreach (var adjust in warpAdjusts)
        {
            if (!adjust.Name.StartsWith("adj", StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryReadGuideValue(adjust.Formula, out double guideValue))
                return Math.Clamp(guideValue / 50000.0, 0.1, 2.0);
        }

        return 1.0;
    }

    private static double ComputeRunCenterProgress(LayoutRect runBoundsDip, LayoutRect shapeBoundsDip)
    {
        if (shapeBoundsDip.Width <= 0)
            return 0;

        double runCenterX = runBoundsDip.X + runBoundsDip.Width / 2.0;
        return Math.Clamp((runCenterX - shapeBoundsDip.X) / shapeBoundsDip.Width, 0.0, 1.0);
    }

    private static double ComputeRotationDeg(
        string? preset,
        double progress,
        LayoutRect shapeBoundsDip,
        double amplitudeScale)
    {
        if (shapeBoundsDip.Width <= 0)
            return 0;

        const double delta = 0.01;
        double left = Math.Clamp(progress - delta, 0.0, 1.0);
        double right = Math.Clamp(progress + delta, 0.0, 1.0);
        if (Math.Abs(right - left) < 0.0001)
            return 0;

        double yLeft = (ComputeYOffset(preset, left, shapeBoundsDip.Height) ?? 0.0) * amplitudeScale;
        double yRight = (ComputeYOffset(preset, right, shapeBoundsDip.Height) ?? 0.0) * amplitudeScale;
        double dx = (right - left) * shapeBoundsDip.Width;
        double angle = Math.Atan2(yRight - yLeft, dx) * 180.0 / Math.PI;
        return Math.Clamp(angle, -MaxRotationDeg, MaxRotationDeg);
    }

    private static double ComputeScaleY(
        WordArtWarpFamily family,
        double progress,
        double amplitudeScale)
    {
        double centerWeight = 1.0 - Math.Abs(progress * 2.0 - 1.0);
        double scaledWeight = centerWeight * Math.Clamp(amplitudeScale, 0.1, 2.0);
        return family switch
        {
            WordArtWarpFamily.Inflate => 1.0 + scaledWeight * 0.10,
            WordArtWarpFamily.Deflate => Math.Max(0.80, 1.0 - scaledWeight * 0.10),
            WordArtWarpFamily.Can => 1.0 + scaledWeight * 0.05,
            _ => 1.0
        };
    }

    private static bool TryReadGuideValue(string formula, out double value)
    {
        value = 0;
        var parts = formula.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !parts[0].Equals("val", StringComparison.OrdinalIgnoreCase))
            return false;

        return double.TryParse(
            parts[1],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }
}
