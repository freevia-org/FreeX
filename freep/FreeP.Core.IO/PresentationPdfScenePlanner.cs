using Free.Shared.Drawing;
using Free.Shared.Pdf;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// Shared FreeP PDF scene decisions used by both vector slide pages and raster-backed pages.
/// Backends receive the resulting dimensions and metadata but remain responsible for painting,
/// image encoding, fonts, and platform handles.
/// </summary>
public static class PresentationPdfScenePlanner
{
    public const double DefaultSlideWidthPoints = 960.0;
    public const double DefaultSlideHeightPoints = 540.0;
    public const int DefaultRasterWidthPx = 1280;

    public static PresentationPdfSlideSize ResolveSlideSize(long widthEmu, long heightEmu) =>
        new(
            ResolvePositivePoints(widthEmu, DefaultSlideWidthPoints),
            ResolvePositivePoints(heightEmu, DefaultSlideHeightPoints));

    public static PresentationPdfRasterSize ResolveRasterSize(
        long widthEmu,
        long heightEmu,
        int requestedWidthPx,
        int? requestedHeightPx)
    {
        var slideSize = ResolveSlideSize(widthEmu, heightEmu);
        var widthPx = Math.Max(1, requestedWidthPx);
        var heightPx = Math.Max(
            1,
            requestedHeightPx ?? (int)Math.Round(widthPx * (slideSize.HeightPoints / slideSize.WidthPoints)));
        return new PresentationPdfRasterSize(slideSize, widthPx, heightPx);
    }

    public static PdfDocumentProperties BuildDocumentProperties(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        var properties = presentation.Properties;
        return new PdfDocumentProperties(
            Title: NullIfBlank(properties.Title),
            Author: NullIfBlank(properties.Author),
            Subject: NullIfBlank(properties.Subject),
            Keywords: NullIfBlank(properties.Keywords),
            Creator: "FreeP");
    }

    public static double EmuToPoints(long emu) => emu / 12700.0;

    public static (double X, double Y) ToPdfPoint(
        (long X, long Y) point,
        double slideHeightPoints) =>
        (EmuToPoints(point.X), slideHeightPoints - EmuToPoints(point.Y));

    private static double ResolvePositivePoints(long emu, double fallbackPoints)
    {
        if (emu <= 0)
            return fallbackPoints;

        var points = DrawingMlCoordinateUnits.EmuToPoints(emu);
        return points > 0 ? points : fallbackPoints;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

public readonly record struct PresentationPdfSlideSize(double WidthPoints, double HeightPoints);

public readonly record struct PresentationPdfRasterSize(
    PresentationPdfSlideSize SlideSize,
    int WidthPx,
    int HeightPx);
