namespace FreeW.App.Presentation.DocumentView;

public sealed record ImageResetSize(double WidthPt, double HeightPt);

/// <summary>
/// Resolves the natural picture size used by Reset Picture in both desktop hosts.
/// Office drawing dimensions are expressed in points while inserted bitmap metadata is pixels.
/// </summary>
public static class ImageResetCommandPlanner
{
    public const double DefaultImageDpi = 96.0;
    public const double PointsPerInch = 72.0;

    public static ImageResetSize BuildNaturalSize(
        int originalPixelWidth,
        int originalPixelHeight,
        double currentWidthPt,
        double currentHeightPt)
    {
        if (originalPixelWidth <= 0 || originalPixelHeight <= 0)
            return new ImageResetSize(currentWidthPt, currentHeightPt);

        return new ImageResetSize(
            originalPixelWidth / DefaultImageDpi * PointsPerInch,
            originalPixelHeight / DefaultImageDpi * PointsPerInch);
    }
}
