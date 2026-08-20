using System.IO;
using Free.Shared.IO;
using Free.Shared.Shell;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public delegate byte[] PresentationSlideImageRenderer(
    Presentation presentation,
    int slideIndex,
    int widthPx,
    int heightPx);

public sealed record PresentationImageExportRequest(
    string OutputDirectory,
    string? BaseFileName = null,
    PresentationSlideRangeRequest? SlideRange = null,
    int WidthPx = PresentationImageExportExecutor.DefaultWidthPx,
    // R153: nullable so the default omits an explicit height and lets Export derive it from the
    // presentation's real SlideSizeCxEmu/CyEmu aspect ratio (PresentationPdfScenePlanner.ResolveRasterSize),
    // the same contract PresentationRasterPdfExportRequest.HeightPx already uses for the sibling raster-PDF
    // path. A caller that still wants a fixed height (e.g. a thumbnail grid) can pass one explicitly.
    int? HeightPx = null);

public sealed record PresentationImageExportedSlide(
    int SlideNumber,
    int SlideIndex,
    string FileName,
    string Path,
    long ByteCount);

public sealed record PresentationImageExportResult(
    PresentationImageExportPlan Plan,
    string OutputDirectory,
    IReadOnlyList<PresentationImageExportedSlide> ExportedSlides)
{
    public bool Succeeded => ExportedSlides.Count == Plan.SlideRange.SlideNumbers.Count;
}

public sealed record PresentationImageExportArtifact(
    PresentationImageExportResult Result,
    IReadOnlyList<string> ImageDiagnostics);

/// <summary>
/// Shared image export execution for FreeP. Hosts provide only the native render callback;
/// slide-range policy, naming, and atomic output writes live here.
/// </summary>
public static class PresentationImageExportExecutor
{
    public const int DefaultWidthPx = PresentationPdfScenePlanner.DefaultRasterWidthPx;
    public const int DefaultHeightPx = 720;

    private const string FallbackBaseFileName = "Presentation";

    public static PresentationImageExportArtifact ExportWithDiagnostics(
        Presentation presentation,
        PresentationImageExportRequest request,
        PresentationSlideImageRenderer renderSlideToPng)
    {
        var imageDiagnostics = new List<string>();
        using var capture = SlideImageRenderDiagnostics.Capture(imageDiagnostics);
        var result = Export(presentation, request, renderSlideToPng);
        return new PresentationImageExportArtifact(result, imageDiagnostics);
    }

    public static PresentationImageExportResult Export(
        Presentation presentation,
        PresentationImageExportRequest request,
        PresentationSlideImageRenderer renderSlideToPng)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(renderSlideToPng);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);

        // R153: mirror PresentationRasterPdfExporter.BuildRenderPlan, which resolves height from the
        // deck's real SlideSizeCxEmu/CyEmu aspect ratio instead of a hardcoded 16:9 box -- otherwise a
        // non-16:9 deck (e.g. legacy 4:3) gets exported at a fixed 1280x720 with the slide content
        // pillarboxed/letterboxed into transparent bars, because request.HeightPx defaulted to a literal.
        var rasterSize = PresentationPdfScenePlanner.ResolveRasterSize(
            presentation.SlideSizeCxEmu,
            presentation.SlideSizeCyEmu,
            request.WidthPx,
            request.HeightPx);
        var widthPx = rasterSize.WidthPx;
        var heightPx = rasterSize.HeightPx;
        var plan = PresentationExportPlanner.BuildImageExportPlan(
            request.SlideRange,
            presentation,
            widthPx,
            heightPx);

        Directory.CreateDirectory(request.OutputDirectory);

        var baseFileName = SanitizeBaseFileName(request.BaseFileName);
        var digits = Math.Max(2, presentation.Slides.Count.ToString().Length);
        var exported = new List<PresentationImageExportedSlide>(plan.SlideRange.SlideNumbers.Count);

        foreach (var slideNumber in plan.SlideRange.SlideNumbers)
        {
            var slideIndex = slideNumber - 1;
            var fileName = $"{baseFileName}-slide-{slideNumber.ToString($"D{digits}")}{PresentationExportPlanner.ImageExportExtension}";
            var path = System.IO.Path.Combine(request.OutputDirectory, fileName);
            var bytes = renderSlideToPng(presentation, slideIndex, plan.WidthPx, plan.HeightPx);
            if (bytes.Length == 0)
                throw new InvalidOperationException($"Image export renderer returned no bytes for slide {slideNumber}.");

            AtomicFileWriter.WriteAllBytes(path, bytes);
            exported.Add(new PresentationImageExportedSlide(slideNumber, slideIndex, fileName, path, bytes.Length));
        }

        return new PresentationImageExportResult(plan, request.OutputDirectory, exported);
    }

    private static string SanitizeBaseFileName(string? baseFileName)
        => OutputFileNameStemPolicy.Normalize(
            baseFileName,
            FallbackBaseFileName,
            invalidCharacterReplacement: '_');
}
