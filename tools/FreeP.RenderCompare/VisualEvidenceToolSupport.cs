using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace FreeP.RenderCompare;

internal static class VisualEvidenceToolSupport
{
    internal static string RunScenario(
        string executable,
        string outputArgument,
        string outputRoot,
        string scenarioArgument,
        string scenarioId,
        TimeSpan timeout,
        string timedOutProcessTreeDescription)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            Arguments = $"{Quote(outputArgument)} {Quote(outputRoot)} {Quote(scenarioArgument)} {Quote(scenarioId)}",
        });
        if (process is null)
            return "The process did not start.";
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return $"PID {process.Id} timed out after {timeout.TotalSeconds:0} seconds and its {timedOutProcessTreeDescription} was stopped.";
        }
        return $"PID {process.Id} exited with code {process.ExitCode}.";
    }

    internal static T ReadManifest<T>(
        string path,
        JsonSerializerOptions options,
        string missingMessage,
        string invalidMessage)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(missingMessage, path);
        var manifest = JsonSerializer.Deserialize<T>(File.ReadAllText(path), options);
        if (manifest is null)
            throw new InvalidDataException(invalidMessage);
        return manifest;
    }

    internal static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"") + '"';
}
