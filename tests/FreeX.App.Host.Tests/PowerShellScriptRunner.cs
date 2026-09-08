using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

internal static class PowerShellScriptRunner
{
    public static PowerShellResult Run(string scriptPath, string workingDirectory)
    {
        return RunWithPowerShellArguments(workingDirectory, $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"");
    }

    public static PowerShellResult Run(string scriptPath, string workingDirectory, string arguments)
    {
        return RunWithPowerShellArguments(workingDirectory, $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}");
    }

    public static PowerShellResult RunToolScript(string scriptName, string workingDirectory)
    {
        return Run(WorkspaceFileLocator.FindToolScript(scriptName), workingDirectory);
    }

    public static PowerShellResult RunToolScript(string scriptName, string workingDirectory, string arguments)
    {
        return Run(WorkspaceFileLocator.FindToolScript(scriptName), workingDirectory, arguments);
    }

    public static PowerShellResult RunToolScriptWithPwsh(string scriptName, string workingDirectory, string arguments)
    {
        // Callers must be marked [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]:
        // pwsh is not part of the repository and is not on every machine, and a missing prerequisite
        // has to be skipped at discovery time (xunit 2.9.3 has no dynamic skip).
        var scriptPath = WorkspaceFileLocator.FindToolScript(scriptName);
        return TestProcessRunner.Run(
            "pwsh",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
            workingDirectory);
    }

    public static PowerShellResult RunToolScriptFromTemporaryWorkingDirectory(string scriptName)
    {
        using var workingDirectory = new TestTemporaryDirectory();
        return RunToolScript(scriptName, workingDirectory.Path);
    }

    public static PowerShellResult RunToolScriptFromTemporaryWorkingDirectory(string scriptName, string arguments)
    {
        using var workingDirectory = new TestTemporaryDirectory();
        return RunToolScript(scriptName, workingDirectory.Path, arguments);
    }

    private static PowerShellResult RunWithPowerShellArguments(string workingDirectory, string powerShellArguments)
    {
        return TestProcessRunner.Run("powershell.exe", powerShellArguments, workingDirectory);
    }
}

internal static class TestProcessRunner
{
    /// <summary>
    /// How long a tool script may run before the test gives up on it.
    /// </summary>
    /// <remarks>
    /// r164 remediation, tests tier: this waited on the child with no bound at all, so a script that
    /// blocks -- on an unexpected prompt, a lock held by a parallel session, a hung child of its own
    /// -- blocked the test, its suite, and the whole gate behind it. Measured: a script sleeping 30s
    /// still had the runner blocked at the 10s mark with no timeout in sight. That is the mechanism
    /// behind a full DefaultTests run stalling indefinitely with the test host alive but idle.
    ///
    /// Five minutes is far beyond the slowest script here (they run in seconds; the entire
    /// 5,300-test host suite takes ~16 minutes), so this cannot turn a slow machine into a failure --
    /// it only converts an unbounded stall into a legible one, the same way
    /// LinuxInteractionRunnerSessionBindingTests already bounds its own probe.
    /// </remarks>
    public static readonly TimeSpan ScriptTimeout = TimeSpan.FromMinutes(5);

    public static PowerShellResult Run(
        string fileName,
        string arguments,
        string workingDirectory,
        TimeSpan? timeout = null)
    {
        // Injectable so the timeout-and-kill path itself is covered by a test with a short bound.
        // An untested timeout path is the one that turns out to be broken the day it first fires.
        var effectiveTimeout = timeout ?? ScriptTimeout;
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start().Should().BeTrue();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)effectiveTimeout.TotalMilliseconds))
        {
            // Kill the whole tree: the script itself may be waiting on a child of its own, and
            // leaving either behind would keep the next run's working directory locked.
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
            }
            catch (InvalidOperationException)
            {
                // Raced with the process exiting on its own; nothing left to kill.
            }

            throw new TimeoutException(
                $"'{fileName} {arguments}' did not exit within {effectiveTimeout.TotalSeconds:N0}s and was killed. " +
                $"Partial output: {Truncate(SafeResult(outputTask))} Partial error: {Truncate(SafeResult(errorTask))}");
        }

        Task.WaitAll(outputTask, errorTask);

        return new PowerShellResult(process.ExitCode, outputTask.Result, errorTask.Result);
    }

    /// <summary>Whatever the reader captured before the kill, without waiting on it further.</summary>
    private static string SafeResult(Task<string> readerTask)
    {
        try
        {
            return readerTask.Wait(TimeSpan.FromSeconds(5)) ? readerTask.Result : "(unavailable)";
        }
        catch (Exception)
        {
            return "(unavailable)";
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 2000 ? value : value[..2000] + "... (truncated)";
}

internal sealed record PowerShellResult(int ExitCode, string Output, string Error)
{
    public string CombinedOutput => Output + Error;

    public string NormalizedCombinedOutput => Regex.Replace(CombinedOutput, "\\s+", " ");
}
