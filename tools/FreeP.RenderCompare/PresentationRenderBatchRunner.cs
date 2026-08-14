using System.IO;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.RenderCompare;

internal static class PresentationRenderBatchRunner
{
    internal static int Render(
        string rendererName,
        string pptxPath,
        string outputDirectory,
        int width,
        int height,
        Action<Presentation, int, int, int, string> renderSlide,
        Func<string, Presentation>? loadPresentation = null)
    {
        Presentation presentation;
        try
        {
            presentation = (loadPresentation ?? PptxPackageReader.Read)(pptxPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load {pptxPath}: {ex.Message}");
            return 1;
        }

        Directory.CreateDirectory(outputDirectory);
        Console.WriteLine(rendererName);
        Console.WriteLine($"  input    : {pptxPath}");
        Console.WriteLine($"  outDir   : {outputDirectory}");
        Console.WriteLine($"  size     : {width}x{height}");
        Console.WriteLine($"  slides   : {presentation.Slides.Count}");

        var failCount = 0;
        for (var slideIndex = 0; slideIndex < presentation.Slides.Count; slideIndex++)
        {
            var slideName = $"slide-{slideIndex + 1:D2}";
            var outputPath = Path.Combine(outputDirectory, slideName + ".png");
            try
            {
                renderSlide(presentation, slideIndex, width, height, outputPath);
                Console.WriteLine($"  {slideName} -> {outputPath}");
                var diversity = PixelDiversity.Analyze(outputPath);
                Console.WriteLine($"    {diversity}");
                if (!diversity.IsTrustworthy)
                {
                    Console.Error.WriteLine($"    UNTRUSTWORTHY: {diversity.FailureReason}");
                    failCount++;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  {slideName} FAILED: {ex.Message}");
                failCount++;
            }
        }

        return ClassifyExitCode(failCount, presentation.Slides.Count);
    }

    internal static int ClassifyExitCode(int failCount, int slideCount)
    {
        if (failCount == 0)
            return 0;
        if (failCount == slideCount)
            return 1;
        return 2;
    }
}
