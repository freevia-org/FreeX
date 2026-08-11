using System.IO;
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
    int HeightPx = PresentationImageExportExecutor.DefaultHeightPx);

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

        var widthPx = Math.Max(1, request.WidthPx);
        var heightPx = Math.Max(1, request.HeightPx);
        var plan = PresentationExportPlanner.BuildImageExportPlan(
            request.SlideRange,
            presentation.Slides.Count,
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

            ExportAtomicWriter.WriteAllBytes(path, bytes);
            exported.Add(new PresentationImageExportedSlide(slideNumber, slideIndex, fileName, path, bytes.Length));
        }

        return new PresentationImageExportResult(plan, request.OutputDirectory, exported);
    }

    private static string SanitizeBaseFileName(string? baseFileName)
    {
        var name = string.IsNullOrWhiteSpace(baseFileName)
            ? FallbackBaseFileName
            : System.IO.Path.GetFileNameWithoutExtension(baseFileName.Trim());
        if (string.IsNullOrWhiteSpace(name))
            name = FallbackBaseFileName;

        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name;
    }
}
