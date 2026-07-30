using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace FreeP.RenderCompare;

/// <summary>
/// Runs one PowerPoint deck in a separate process so a blocked COM call cannot
/// prevent the rest of the corpus from producing bounded evidence.
/// </summary>
internal static class PowerPointCorpusProcessExporter
{
    internal static readonly TimeSpan DefaultDeckTimeout = TimeSpan.FromMinutes(3);
    private const string PowerPointProcessName = "POWERPNT";

    internal static PowerPointExportResult Export(
        string pptxPath,
        string outDir,
        int width,
        int height,
        TimeSpan timeout)
    {
        Directory.CreateDirectory(outDir);
        var resultPath = Path.Combine(
            outDir,
            $".freep-powerpoint-export-{Guid.NewGuid():N}.json");
        var beforePowerPointPids = GetPowerPointProcessIds();

        try
        {
            using var process = Process.Start(BuildStartInfo(
                pptxPath,
                outDir,
                width,
                height,
                resultPath));
            if (process is null)
            {
                return PowerPointExportResult.Failed(
                    PowerPointExportFailureKind.ExportFailed,
                    CountGeneratedSlides(outDir),
                    0);
            }

            if (!process.WaitForExit(timeout))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Failed to stop timed-out PowerPoint deck worker: {ex.Message}");
                }

                // COM can launch POWERPNT outside the worker's process tree.
                // Reap only instances created after this deck began so a timed-out
                // deck cannot leak an automation-owned PowerPoint into the next one.
                KillNewPowerPointProcesses(beforePowerPointPids);

                Console.Error.WriteLine(
                    $"  PowerPoint deck timed out after {timeout.TotalSeconds:0}s: {Path.GetFileName(pptxPath)}");
                return PowerPointExportResult.Failed(
                    PowerPointExportFailureKind.TimedOut,
                    CountGeneratedSlides(outDir),
                    0);
            }

            if (TryReadResult(resultPath, out var result))
                return result;

            Console.Error.WriteLine(
                $"  PowerPoint deck worker exited without a result ({process.ExitCode}): {Path.GetFileName(pptxPath)}");
            return PowerPointExportResult.Failed(
                PowerPointExportFailureKind.ExportFailed,
                CountGeneratedSlides(outDir),
                0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  PowerPoint deck worker failed: {ex.GetType().Name}: {ex.Message}");
            return PowerPointExportResult.Failed(
                PowerPointExportFailureKind.ExportFailed,
                CountGeneratedSlides(outDir),
                0);
        }
        finally
        {
            try
            {
                if (File.Exists(resultPath))
                    File.Delete(resultPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Failed to remove deck-worker result file: {ex.Message}");
            }
        }
    }

    internal static ProcessStartInfo BuildStartInfo(
        string pptxPath,
        string outDir,
        int width,
        int height,
        string resultPath,
        string? processPath = null,
        string? entryAssemblyPath = null)
    {
        processPath ??= Environment.ProcessPath
            ?? throw new InvalidOperationException("The current process path is unavailable.");
        entryAssemblyPath ??= Assembly.GetEntryAssembly()?.Location;

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };

        // `dotnet run` hosts the application inside dotnet.exe; a compiled
        // RenderCompare.exe already knows its entry assembly and needs no DLL
        // prefix. ArgumentList keeps deck paths with spaces unambiguous.
        if (IsDotnetHost(processPath))
        {
            if (string.IsNullOrWhiteSpace(entryAssemblyPath))
                throw new InvalidOperationException("The entry assembly path is unavailable for the dotnet host.");
            startInfo.ArgumentList.Add(entryAssemblyPath);
        }

        startInfo.ArgumentList.Add("--powerpoint-export-one");
        startInfo.ArgumentList.Add(pptxPath);
        startInfo.ArgumentList.Add(outDir);
        startInfo.ArgumentList.Add("--width");
        startInfo.ArgumentList.Add(width.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--height");
        startInfo.ArgumentList.Add(height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--result");
        startInfo.ArgumentList.Add(resultPath);
        return startInfo;
    }

    internal static bool IsDotnetHost(string processPath) =>
        string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadResult(string resultPath, out PowerPointExportResult result)
    {
        result = default!;
        if (!File.Exists(resultPath))
            return false;

        try
        {
            var payload = JsonSerializer.Deserialize<ChildResult>(File.ReadAllText(resultPath));
            if (payload is null || !Enum.IsDefined(typeof(PowerPointExportFailureKind), payload.FailureKind))
                return false;

            result = new PowerPointExportResult(
                payload.ExitCode,
                payload.FailureKind,
                payload.ExportedSlides,
                payload.TotalSlides);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Failed to read deck-worker result: {ex.Message}");
            return false;
        }
    }

    private static int CountGeneratedSlides(string outDir) =>
        Directory.Exists(outDir)
            ? Directory.GetFiles(outDir, "slide-*.png", SearchOption.TopDirectoryOnly).Length
            : 0;

    private static HashSet<int> GetPowerPointProcessIds() =>
        Process.GetProcessesByName(PowerPointProcessName)
            .Select(process => process.Id)
            .ToHashSet();

    private static void KillNewPowerPointProcesses(IReadOnlySet<int> beforePids)
    {
        var processes = Process.GetProcessesByName(PowerPointProcessName);
        var ownedPids = SelectOwnedPowerPointProcessIds(
            processes.Select(process => process.Id),
            beforePids);

        foreach (var process in processes)
        {
            if (!ownedPids.Contains(process.Id))
            {
                process.Dispose();
                continue;
            }

            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
                Console.Error.WriteLine($"  Killed orphan POWERPNT PID {process.Id} from timed-out deck worker.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Failed to stop orphan POWERPNT PID {process.Id}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    internal static IReadOnlySet<int> SelectOwnedPowerPointProcessIds(
        IEnumerable<int> currentPids,
        IReadOnlySet<int> beforePids) =>
        currentPids.Where(pid => !beforePids.Contains(pid)).ToHashSet();

    internal sealed record ChildResult(
        int ExitCode,
        PowerPointExportFailureKind FailureKind,
        int ExportedSlides,
        int TotalSlides);
}
