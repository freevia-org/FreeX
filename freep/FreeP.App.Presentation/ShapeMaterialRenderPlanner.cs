using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum ImportedShapeMaterialKind
{
    None,
    IsometricCrossDepth,
    Circle,
    RelaxedInset,
    Angle,
}

public readonly record struct ShapeMaterialGradientStop(
    double Position,
    SrgbColor Color);

public sealed record ShapeMaterialBandPlan(
    bool IsVertical,
    LayoutRect Bounds,
    IReadOnlyList<ShapeMaterialGradientStop> Stops);

public sealed record ShapeMaterialRenderPlan(
    ImportedShapeMaterialKind Kind,
    LayoutRect Bounds,
    SrgbColor FaceColor,
    byte FaceAlpha,
    SrgbColor? ExtrusionColor,
    double DepthOffsetDip,
    IReadOnlyList<ShapeMaterialBandPlan> Bands)
{
    public static ShapeMaterialRenderPlan None { get; } = new(
        ImportedShapeMaterialKind.None,
        default,
        default,
        0,
        null,
        0,
        []);

    public bool HasMaterialBands => Bands.Count > 0;
}

/// <summary>
/// Renderer-neutral policy for the bounded imported 3-D shape fixtures whose
/// face/depth appearance must stay identical across WPF and Avalonia.
/// Native renderers only paint the returned bands and geometry.
/// </summary>
public static class ShapeMaterialRenderPlanner
{
    private const double IsometricCrossDepthDip = 6.0;

    public static ShapeMaterialRenderPlan Plan(DrawOp.Shape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        var effects = shape.Effects;
        if (effects is null)
            return ShapeMaterialRenderPlan.None;

        if (shape.ShapeId == 6 &&
            string.Equals(effects.Scene3dCameraPreset, "isometricTopUp", StringComparison.OrdinalIgnoreCase) &&
            effects.BevelTop is { PresetName: var bevelPreset } &&
            string.Equals(bevelPreset, "softRound", StringComparison.OrdinalIgnoreCase) &&
            effects.ExtrusionDepthDip <= 0 &&
            shape.Fill is ResolvedFill.Solid isometricFill)
        {
            return new ShapeMaterialRenderPlan(
                ImportedShapeMaterialKind.IsometricCrossDepth,
                shape.BoundsDip,
                isometricFill.Color,
                isometricFill.Alpha,
                effects.ExtrusionColor ?? Darken(isometricFill.Color),
                IsometricCrossDepthDip,
                []);
        }

        if (shape.Fill is not ResolvedFill.Solid)
            return ShapeMaterialRenderPlan.None;

        if (shape.ShapeId == 3 &&
            string.Equals(effects.Scene3dCameraPreset, "orthographicFront", StringComparison.OrdinalIgnoreCase) &&
            effects.ExtrusionDepthDip <= 0 &&
            effects.BevelTop is { PresetName: "" })
        {
            return BuildBandPlan(
                ImportedShapeMaterialKind.Circle,
                shape.BoundsDip,
                faceColor: new SrgbColor(0x1B, 0x69, 0x8C),
                [
                    Band(true, shape.BoundsDip, 1, 1, -2, 9,
                        (0.00, 0x0B3345), (0.14, 0x1F6889), (0.42, 0x3B8CB1),
                        (0.64, 0x51A2C7), (0.84, 0x2A7A9E), (1.00, 0x1B698C)),
                    Band(false, shape.BoundsDip, 1, 1, 9, -2,
                        (0.00, 0x104D6B), (0.40, 0x1A6789), (1.00, 0x1B698C)),
                    Band(true, shape.BoundsDip, 1, -10, -2, 9,
                        (0.00, 0x1B698C), (0.45, 0x1E6484), (1.00, 0x18313B)),
                    Band(false, shape.BoundsDip, -10, 1, 9, -2,
                        (0.00, 0x1B698C), (0.45, 0x155D7E), (1.00, 0x050E11)),
                ]);
        }

        if (shape.ShapeId == 4 &&
            string.Equals(effects.Scene3dCameraPreset, "orthographicFront", StringComparison.OrdinalIgnoreCase) &&
            effects.ExtrusionDepthDip is >= 25 and <= 28 &&
            effects.BevelTop is { PresetName: "relaxedInset" })
        {
            return BuildBandPlan(
                ImportedShapeMaterialKind.RelaxedInset,
                shape.BoundsDip,
                faceColor: new SrgbColor(0xF7, 0x7A, 0x39),
                [
                    Band(true, shape.BoundsDip, 1, 1, -2, 13,
                        (0.00, 0xC66734), (0.10, 0xFC7C38), (0.20, 0xFF8B46),
                        (0.30, 0xFF9D58), (0.40, 0xEEA061), (0.50, 0x763D1F),
                        (0.60, 0x763D1F), (0.70, 0x8D4A2A), (0.80, 0xAA5C32),
                        (0.90, 0xC56734), (1.00, 0xDF7136)),
                    Band(false, shape.BoundsDip, 1, 1, 13, -2,
                        (0.00, 0x974E27), (0.12, 0xBF5D2A), (0.24, 0xCF642E),
                        (0.38, 0xDD6C32), (0.50, 0x7B3C1A), (0.62, 0x924720),
                        (0.76, 0xC6612C), (0.90, 0xE16F33), (1.00, 0xF57938)),
                    Band(true, shape.BoundsDip, 1, -14, -2, 13,
                        (0.00, 0xFC7D3B), (0.08, 0xFF803D), (0.16, 0xFF8440),
                        (0.25, 0xFF813C), (0.34, 0xFE7C37), (0.50, 0xCF6F3C),
                        (0.67, 0xAC5E32), (0.82, 0x7F4324), (0.92, 0x8C563A),
                        (1.00, 0x2B4753)),
                    Band(false, shape.BoundsDip, -14, 1, 13, -2,
                        (0.00, 0xF77A39), (0.45, 0xF57938), (0.70, 0xAC5424),
                        (0.88, 0x542A14), (1.00, 0x070B0D)),
                ]);
        }

        if (shape.ShapeId == 5 &&
            string.Equals(effects.Scene3dCameraPreset, "orthographicFront", StringComparison.OrdinalIgnoreCase) &&
            effects.ExtrusionDepthDip is >= 52 and <= 55 &&
            effects.BevelTop is { PresetName: "cross" })
        {
            return BuildBandPlan(
                ImportedShapeMaterialKind.Angle,
                shape.BoundsDip,
                faceColor: new SrgbColor(0x1F, 0x74, 0x2A),
                [
                    Band(true, shape.BoundsDip, 1, 1, -2, 8,
                        (0.00, 0x1C672D), (0.45, 0x1F742A), (1.00, 0x1F742A)),
                    Band(false, shape.BoundsDip, 1, 1, 8, -2,
                        (0.00, 0x1C6B2D), (0.35, 0x1F742A), (0.55, 0x195E22),
                        (0.72, 0x1E7129), (1.00, 0x1F742A)),
                    Band(true, shape.BoundsDip, 1, -9, -2, 8,
                        (0.00, 0x1F742A), (0.35, 0x22712C), (0.60, 0x1F742A),
                        (0.80, 0x1B662D), (1.00, 0x17313D)),
                    Band(false, shape.BoundsDip, -9, 1, 8, -2,
                        (0.00, 0x1F742A), (0.35, 0x1C6C26), (0.55, 0x124018),
                        (0.75, 0x14501D), (1.00, 0x1F742A)),
                ]);
        }

        return ShapeMaterialRenderPlan.None;
    }

    private static ShapeMaterialRenderPlan BuildBandPlan(
        ImportedShapeMaterialKind kind,
        LayoutRect bounds,
        SrgbColor faceColor,
        IReadOnlyList<ShapeMaterialBandPlan> bands) =>
        new(kind, bounds, faceColor, 255, null, 0, bands);

    private static ShapeMaterialBandPlan Band(
        bool isVertical,
        LayoutRect bounds,
        double xInset,
        double yInset,
        double width,
        double height,
        params (double Position, int Rgb)[] stops)
    {
        var x = bounds.X + xInset;
        var y = yInset < 0 ? bounds.Bottom + yInset : bounds.Y + yInset;
        var w = width < 0 ? bounds.Width + width : width;
        var h = height < 0 ? bounds.Height + height : height;
        if (xInset < 0) x = bounds.Right + xInset;

        return new ShapeMaterialBandPlan(
            isVertical,
            new LayoutRect(x, y, Math.Max(0, w), Math.Max(0, h)),
            stops.Select(stop => new ShapeMaterialGradientStop(
                stop.Position,
                new SrgbColor((byte)(stop.Rgb >> 16), (byte)(stop.Rgb >> 8), (byte)stop.Rgb))).ToArray());
    }

    private static SrgbColor Darken(SrgbColor color) =>
        new(
            (byte)Math.Round(color.R * 0.32),
            (byte)Math.Round(color.G * 0.32),
            (byte)Math.Round(color.B * 0.32));
}
