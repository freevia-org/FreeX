namespace FreeP.App.Compositor;

/// <summary>One renderer-neutral pass used to approximate DrawingML reflection blur.</summary>
public readonly record struct PictureReflectionBlurPass(
    double OffsetXDip,
    double OffsetYDip,
    double Opacity);

public static class PictureReflectionRenderPlanner
{
    public static IReadOnlyList<PictureReflectionBlurPass> PlanBlurPasses(double blurDip)
    {
        if (!double.IsFinite(blurDip) || blurDip <= 0.5)
            return [new PictureReflectionBlurPass(0, 0, 1)];

        int rings = Math.Min(3, (int)Math.Ceiling(blurDip / 2));
        double ringOpacity = 0.6 / (rings * 8);
        var passes = new List<PictureReflectionBlurPass>(rings * 8 + 1);
        for (int ring = rings; ring >= 1; ring--)
        {
            double radius = blurDip * ring / rings;
            double diagonal = radius * 0.7071067811865476;
            foreach (var (x, y) in new[]
            {
                (radius, 0d), (diagonal, diagonal), (0d, radius),
                (-diagonal, diagonal), (-radius, 0d), (-diagonal, -diagonal),
                (0d, -radius), (diagonal, -diagonal),
            })
                passes.Add(new PictureReflectionBlurPass(x, y, ringOpacity));
        }

        passes.Add(new PictureReflectionBlurPass(0, 0, 0.4));
        return passes;
    }
}
