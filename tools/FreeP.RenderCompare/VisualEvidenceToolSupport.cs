using System.Diagnostics;
using System.Text.Json;
using FreeP.VisualEvidence;
using Free.ToolsShared;

namespace FreeP.RenderCompare;

internal static class VisualEvidenceToolSupport
{
    internal static string RunScenario(VisualEvidenceProcessPlan plan)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = plan.Executable,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            Arguments = plan.Arguments,
        });
        if (process is null)
            return "The process did not start.";
        if (!process.WaitForExit(plan.TimeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return $"PID {process.Id} timed out after {plan.Timeout.TotalSeconds:0} seconds and its {plan.TimedOutProcessTreeDescription} was stopped.";
        }
        return $"PID {process.Id} exited with code {process.ExitCode}.";
    }

    internal static T ReadManifest<T>(
        string path,
        JsonSerializerOptions options,
        string missingMessage,
        string invalidMessage)
        where T : class =>
        FreePVisualEvidenceCaptureOrchestration.ReadManifest<T>(
            path,
            options,
            missingMessage,
            invalidMessage);

    internal static string Sha256(string path)
        => FreePVisualEvidenceCaptureOrchestration.Sha256(path);
}
