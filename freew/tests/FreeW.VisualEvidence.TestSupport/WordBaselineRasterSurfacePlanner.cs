namespace FreeW.App.Presentation.DocumentView;

public sealed record WordBaselineRasterSurfacePlan(
    int PixelWidth,
    int PixelHeight,
    double Scale)
{
    public bool IsIdentity => Scale == 1d;
}

/// <summary>
/// Defines the bounded PNG surface used by the Word COM baseline capture.
/// </summary>
public static class WordBaselineRasterSurfacePlanner
{
    public const int MaximumPixelWidth = 816;
    public const int MaximumPixelHeight = 1056;

    public static WordBaselineRasterSurfacePlan Build(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));

        var scale = Math.Min(
            1d,
            Math.Min(
                (double)MaximumPixelWidth / pixelWidth,
                (double)MaximumPixelHeight / pixelHeight));

        return new WordBaselineRasterSurfacePlan(
            Math.Max(1, (int)Math.Floor(pixelWidth * scale)),
            Math.Max(1, (int)Math.Floor(pixelHeight * scale)),
            scale);
    }
}
