using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public static class PictureColorEffectAuthoringPlanner
{
    public const string GrayscaleCommandId = "freep.picture.grayscale";
    public const string ResetCommandId = "freep.picture.effects-reset";

    public static PictureColorEffectValues Grayscale() =>
        new(true, null, null, null, null);

    public static PictureColorEffectValues Reset() => PictureColorEffectValues.Reset;
}
