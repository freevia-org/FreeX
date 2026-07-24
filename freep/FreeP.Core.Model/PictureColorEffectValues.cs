namespace FreeP.Core.Model;

/// <summary>
/// Authoring values for the color effects on a picture. Null numeric values mean that the
/// corresponding DrawingML effect is absent rather than set to zero.
/// </summary>
public readonly record struct PictureColorEffectValues(
    bool Grayscale,
    double? BiLevelThreshold,
    double? Brightness,
    double? Contrast,
    double? AlphaModPct)
{
    public static PictureColorEffectValues Reset => new(false, null, null, null, null);
}
