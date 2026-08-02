using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public sealed record PageBorderTextFrame(
    double X,
    double Y,
    double Width,
    double Height);

public static class PageBorderTextFramePlanner
{
    private const double DefaultHeaderFooterDistancePt = 36;

    public static PageBorderTextFrame Build(
        PageSettings page,
        PageBorder border,
        double pageWidth,
        double pageHeight,
        double unitsPerPoint,
        double strokeRegistration,
        bool doNotSurroundHeader,
        bool doNotSurroundFooter)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(border);

        var scale = Math.Max(0, unitsPerPoint);
        var registration = Math.Max(0, strokeRegistration);
        var space = Math.Max(0, border.SpacePt) * scale;
        var topReferencePt = doNotSurroundHeader
            ? page.MarginTopPt
            : EffectiveDistance(page.HeaderDistancePt);
        var bottomReferencePt = doNotSurroundFooter
            ? page.MarginBottomPt
            : EffectiveDistance(page.FooterDistancePt);

        return new PageBorderTextFrame(
            X: Math.Max(0, page.MarginLeftPt * scale - space - registration),
            Y: Math.Max(0, topReferencePt * scale - space - registration),
            Width: Math.Max(
                0,
                pageWidth
                - (page.MarginLeftPt + page.MarginRightPt) * scale
                + 2 * (space + registration)),
            Height: Math.Max(
                0,
                pageHeight
                - (topReferencePt + bottomReferencePt) * scale
                + 2 * (space + registration)));
    }

    private static double EffectiveDistance(double distancePt) =>
        distancePt > 0 ? distancePt : DefaultHeaderFooterDistancePt;
}
