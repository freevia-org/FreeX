using Free.Shared.AppServices;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.Validation.Avalonia;

internal static class PackagingSmokeCommand
{
    internal static bool TryRun(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!SisterAppPackagingSmoke.HasArgument(args))
        {
            exitCode = 0;
            return false;
        }

        var reportPath = SisterAppPackagingSmoke.FindReportPath(args);
        try
        {
            var presentation = Presentation.CreateEmpty();
            var expectedCount = presentation.Slides.Count;

            using var stream = new MemoryStream();
            PptxPackageWriter.Write(presentation, stream);
            stream.Position = 0;

            var reopened = PptxPackageReader.Read(stream);
            var passed = reopened.Slides.Count == expectedCount && expectedCount >= 1;
            var report =
                $"freep_packaging_smoke={(passed ? "passed" : "failed")}\n" +
                $"slides={reopened.Slides.Count}\n";

            SisterAppPackagingSmoke.WriteReport(reportPath, report, error);
            output.Write(report);
            output.Flush();
            exitCode = passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            var report = $"freep_packaging_smoke=failed\nerror={exception.Message}\n";
            SisterAppPackagingSmoke.WriteReport(reportPath, report, error);
            error.WriteLine(report);
            exitCode = 1;
        }

        return true;
    }
}
