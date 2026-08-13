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
        out int exitCode) =>
        SisterAppPackagingSmoke.TryRun(args, output, error, Execute, HandleException, out exitCode);

    private static SisterAppPackagingSmokeResult Execute(IReadOnlyList<string> _)
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

        return new SisterAppPackagingSmokeResult(
            passed ? 0 : 1,
            SisterAppPackagingSmokeOutputTarget.StandardOutput,
            report,
            report);
    }

    private static SisterAppPackagingSmokeResult HandleException(Exception exception)
    {
        var report = $"freep_packaging_smoke=failed\nerror={exception.Message}\n";
        return new SisterAppPackagingSmokeResult(
            1,
            SisterAppPackagingSmokeOutputTarget.StandardError,
            report + Environment.NewLine,
            report);
    }
}
