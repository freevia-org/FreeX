using System.IO;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.RenderCompare;

/// <summary>Creates a deterministic planner-generated presenter-ink package for COM validation.</summary>
internal static class PresenterInkProbe
{
    internal static int Generate(string outputPath)
    {
        var presentation = Presentation.CreateEmpty();
        var state = SlideShowInkExecutionPlanner.CreateState(
            committedStrokes: new[]
            {
                new SlideShowInkStroke(
                    "powerpoint-open-probe",
                    0,
                    SlideShowPresenterPointerMode.Pen,
                    new SlideShowInkState("#336699", 5, 1),
                    new[]
                    {
                        new SlideShowInkPoint(100, 100),
                        new SlideShowInkPoint(300, 200),
                        new SlideShowInkPoint(500, 100),
                    }),
            });

        SlideShowInkPersistencePlanner.ApplyRetentionOnExit(presentation, state);
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var stream = File.Create(fullPath);
        PptxPackageWriter.Write(presentation, stream);
        Console.WriteLine($"Presenter ink probe -> {fullPath}");
        return 0;
    }
}
