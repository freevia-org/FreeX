using System.Diagnostics;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Marks a test that shells out to a tool the repository does not ship -- PowerShell 7
/// (<c>pwsh</c>) or <c>python</c> -- so it is SKIPPED, not failed, on a machine without it.
///
/// Without this, a machine lacking those tools reported 19 of this suite's tests as ordinary
/// failures ("An error occurred trying to start process 'pwsh'", or an exit code carrying
/// "The term 'pwsh' is not recognized"). That is indistinguishable at a glance from a real
/// regression and made the suite's red count useless as a signal for the tests that matter.
///
/// The skip is decided at discovery time, the same way <see cref="UiE2eFactAttribute"/> and
/// HeavyWorkbookRetestFactAttribute decide theirs. It cannot be done from inside the test body:
/// xUnit's dynamic <c>SkipException</c> is a v3 feature, and this suite is on xunit 2.9.3, where
/// throwing it merely surfaces a failure whose message carries the raw <c>$XunitDynamicSkip$</c>
/// marker.
///
/// Deliberately narrow: a test is skipped only when the tool is genuinely absent. A script that
/// fails for its own reasons still fails, and so does one that cannot find a tool that IS
/// installed -- that would be a real bug in the script's own path resolution.
/// </summary>
internal sealed class RequiresExternalToolFactAttribute : FactAttribute
{
    public RequiresExternalToolFactAttribute(params string[] toolNames)
    {
        foreach (var toolName in toolNames)
        {
            if (!ExternalToolPreconditions.IsAvailable(toolName))
            {
                Skip = ExternalToolPreconditions.SkipReason(toolName);
                return;
            }
        }
    }
}

internal static class ExternalToolPreconditions
{
    public const string PowerShell7 = "pwsh";
    public const string Python = "python";

    private static readonly Dictionary<string, bool> AvailabilityCache = new(StringComparer.OrdinalIgnoreCase);

    public static string SkipReason(string toolName) =>
        $"'{toolName}' is not installed on this machine, so this test cannot run. Install it (PowerShell 7 for 'pwsh') to exercise this check.";

    public static bool IsAvailable(string toolName)
    {
        lock (AvailabilityCache)
        {
            if (AvailabilityCache.TryGetValue(toolName, out var cached))
                return cached;

            var available = Probe(toolName);
            AvailabilityCache[toolName] = available;
            return available;
        }
    }

    private static bool Probe(string toolName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = toolName,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return false;

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(milliseconds: 15_000);

            // The Windows App Execution Alias stub Microsoft ships for an uninstalled interpreter
            // starts happily and then advertises the Store, so the exit code alone is not enough.
            return !output.Contains("was not found", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Win32Exception ("the system cannot find the file specified") when it is not on PATH.
            return false;
        }
    }
}
