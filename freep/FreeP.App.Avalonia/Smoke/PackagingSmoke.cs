using FreeP.Core.IO;
using FreeP.Core.Model;
using Free.Shared.AppServices;

namespace FreeP.App.Avalonia.Smoke;

/// <summary>
/// Headless packaging smoke: exercises model creation and .pptx round-trip without any display.
/// Invoked via <c>--packaging-smoke &lt;report&gt;</c> from CI before the Avalonia host starts.
/// </summary>
internal static class PackagingSmoke
{
    public static bool TryRun(string[] args, TextWriter stdout, TextWriter stderr, out int exitCode)
    {
        exitCode = 0;
        var reportPath = SisterAppPackagingSmoke.FindReportPath(args);
        if (reportPath is null)
            return false;

        try
        {
            // Create a presentation with a known slide count and round-trip it through pptx I/O.
            var presentation = Presentation.CreateEmpty();
            var expectedCount = presentation.Slides.Count;

            using var stream = new MemoryStream();
            PptxPackageWriter.Write(presentation, stream);
            stream.Position = 0;

            var reopened = PptxPackageReader.Read(stream);

            bool passed = reopened.Slides.Count == expectedCount && expectedCount >= 1;
            string report =
                $"freep_packaging_smoke={(passed ? "passed" : "failed")}\n" +
                $"slides={reopened.Slides.Count}\n";

            SisterAppPackagingSmoke.WriteReport(reportPath, report, stderr);
            stdout.Write(report);
            stdout.Flush();
            exitCode = passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            string report = $"freep_packaging_smoke=failed\nerror={ex.Message}\n";
            SisterAppPackagingSmoke.WriteReport(reportPath, report, stderr);
            stderr.WriteLine(report);
            exitCode = 1;
        }

        return true;
    }
}
