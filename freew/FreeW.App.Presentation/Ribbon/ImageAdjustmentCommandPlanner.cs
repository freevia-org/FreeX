using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public enum ImageAdjustmentChannel
{
    Brightness,
    Contrast,
    Saturation,
    Transparency
}

public enum ImageEffectChannel
{
    Shadow,
    Reflection,
    Glow,
    SoftEdge,
    Bevel
}

public sealed record ImageAdjustmentPresetDescriptor(
    FreeWRibbonCommandAction Action,
    ImageAdjustmentChannel Channel,
    double Value);

public sealed record ImageRecolorPresetDescriptor(
    FreeWRibbonCommandAction Action,
    ImageRecolorMode Mode,
    double? ColorTemperature = null);

public sealed record ImageEffectPresetDescriptor(
    ImageEffectChannel Channel,
    double Value,
    FreeWRibbonCommandAction? Action = null,
    string? CommandId = null);

public sealed record ImageArtisticEffectPresetDescriptor(
    string CommandId,
    ImageArtisticEffect Effect);

public static class ImageAdjustmentCommandPlanner
{
    public static IReadOnlyList<ImageAdjustmentPresetDescriptor> AdjustmentPresets { get; } =
    [
        new(FreeWRibbonCommandAction.ImageBrightnessPlus20, ImageAdjustmentChannel.Brightness, 20),
        new(FreeWRibbonCommandAction.ImageBrightnessPlus40, ImageAdjustmentChannel.Brightness, 40),
        new(FreeWRibbonCommandAction.ImageBrightnessMinus20, ImageAdjustmentChannel.Brightness, -20),
        new(FreeWRibbonCommandAction.ImageBrightnessMinus40, ImageAdjustmentChannel.Brightness, -40),
        new(FreeWRibbonCommandAction.ImageContrastPlus20, ImageAdjustmentChannel.Contrast, 20),
        new(FreeWRibbonCommandAction.ImageContrastMinus20, ImageAdjustmentChannel.Contrast, -20),
        new(FreeWRibbonCommandAction.ImageSaturation0, ImageAdjustmentChannel.Saturation, 0),
        new(FreeWRibbonCommandAction.ImageSaturation50, ImageAdjustmentChannel.Saturation, 50),
        new(FreeWRibbonCommandAction.ImageSaturation200, ImageAdjustmentChannel.Saturation, 200),
        new(FreeWRibbonCommandAction.ImageTransparency25, ImageAdjustmentChannel.Transparency, 25),
        new(FreeWRibbonCommandAction.ImageTransparency50, ImageAdjustmentChannel.Transparency, 50),
        new(FreeWRibbonCommandAction.ImageTransparency75, ImageAdjustmentChannel.Transparency, 75),
    ];

    public static IReadOnlyList<ImageRecolorPresetDescriptor> RecolorPresets { get; } =
    [
        new(FreeWRibbonCommandAction.ImageRecolorGrayscale, ImageRecolorMode.Grayscale),
        new(FreeWRibbonCommandAction.ImageRecolorSepia, ImageRecolorMode.Sepia),
        new(FreeWRibbonCommandAction.ImageRecolorWashout, ImageRecolorMode.Washout),
        new(FreeWRibbonCommandAction.ImageRecolorBlackwhite, ImageRecolorMode.BlackWhite),
        new(FreeWRibbonCommandAction.ImageRecolorNone, ImageRecolorMode.None),
        new(FreeWRibbonCommandAction.ImageColortempWarm, ImageRecolorMode.None, 60),
        new(FreeWRibbonCommandAction.ImageColortempCool, ImageRecolorMode.None, -60),
        new(FreeWRibbonCommandAction.ImageColortempNeutral, ImageRecolorMode.None, 0),
    ];

    public static IReadOnlyList<ImageEffectPresetDescriptor> EffectPresets { get; } =
        BuildEffectPresets();

    public static IReadOnlyList<ImageArtisticEffectPresetDescriptor> ArtisticEffectPresets { get; } =
    [
        new("freew.image-artistic-none", ImageArtisticEffect.None),
        new("freew.image-artistic-blur", ImageArtisticEffect.Blur),
        new("freew.image-artistic-glow-diffused", ImageArtisticEffect.GlowDiffused),
        new("freew.image-artistic-glow-edges", ImageArtisticEffect.GlowEdges),
        new("freew.image-artistic-pencil-gray", ImageArtisticEffect.PencilGrayscale),
        new("freew.image-artistic-pencil-sketch", ImageArtisticEffect.PencilSketch),
        new("freew.image-artistic-line-drawing", ImageArtisticEffect.LineDrawing),
        new("freew.image-artistic-paintbrush", ImageArtisticEffect.Paintbrush),
        new("freew.image-artistic-paint-strokes", ImageArtisticEffect.PaintStrokes),
        new("freew.image-artistic-photocopy", ImageArtisticEffect.Photocopy),
        new("freew.image-artistic-posterize", ImageArtisticEffect.Posterize),
        new("freew.image-artistic-pastels", ImageArtisticEffect.Pastels),
        new("freew.image-artistic-watercolor", ImageArtisticEffect.Watercolor),
        new("freew.image-artistic-film-grain", ImageArtisticEffect.FilmGrain),
        new("freew.image-artistic-mosaic", ImageArtisticEffect.Mosaic),
    ];

    private static IReadOnlyList<ImageEffectPresetDescriptor> BuildEffectPresets()
    {
        var presets = new List<ImageEffectPresetDescriptor>
        {
            new(ImageEffectChannel.Shadow, 0, Action: FreeWRibbonCommandAction.ImageShadowNone),
            new(ImageEffectChannel.Reflection, 0, Action: FreeWRibbonCommandAction.ImageReflectionNone),
            new(ImageEffectChannel.Bevel, 0, Action: FreeWRibbonCommandAction.ImageBevelNone),
        };
        presets.AddRange(Enumerable.Range(1, 5)
            .Select(value => new ImageEffectPresetDescriptor(
                ImageEffectChannel.Shadow, value, CommandId: $"freew.image-shadow-{value}")));
        presets.AddRange(Enumerable.Range(1, 5)
            .Select(value => new ImageEffectPresetDescriptor(
                ImageEffectChannel.Reflection, value, CommandId: $"freew.image-reflection-{value}")));
        presets.AddRange(new[] { 0d, 5d, 8d, 11d, 18d }
            .Select(value => new ImageEffectPresetDescriptor(
                ImageEffectChannel.Glow,
                value,
                CommandId: $"freew.image-glow-{(value == 0 ? "none" : value.ToString("0", System.Globalization.CultureInfo.InvariantCulture))}")));
        presets.AddRange(new[] { 0d, 1d, 2.5d, 5d, 10d }
            .Select(value => new ImageEffectPresetDescriptor(
                ImageEffectChannel.SoftEdge,
                value,
                CommandId: $"freew.image-softedge-{SoftEdgeSuffix(value)}")));
        presets.AddRange(Enumerable.Range(1, 4)
            .Select(value => new ImageEffectPresetDescriptor(
                ImageEffectChannel.Bevel, value, CommandId: $"freew.image-bevel-{value}")));
        return presets;
    }

    private static string SoftEdgeSuffix(double value) => value switch
    {
        0 => "none",
        2.5 => "2pt5",
        _ => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
    };
}
