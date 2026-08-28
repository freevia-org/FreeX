using System.Diagnostics;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// r164 remediation, tests tier. <see cref="TestProcessRunner"/> waited on its child process with no
/// bound at all, so a tool script that blocks -- on an unexpected prompt, on a lock a parallel
/// session holds, or on a hung child of its own -- blocked the test, its suite, and the whole gate
/// behind it. Measured before the fix: a script sleeping 30s still had the runner blocked at the 10s
/// mark with no timeout in sight. That is the mechanism behind a full DefaultTests run stalling
/// indefinitely with the test host alive but idle, which this repository has seen.
///
/// The timeout is injectable purely so this test can exercise the kill path with a short bound: an
/// untested timeout path is the one that turns out to be broken the day it first fires.
/// </summary>
public sealed class R164_ScriptRunnerTimeoutTests
{
    [Fact]
    public void Run_ScriptThatNeverFinishes_FailsQuicklyAndKillsIt()
    {
        using var directory = new TestTemporaryDirectory();
        var scriptPath = Path.Combine(directory.Path, "hangs-forever.ps1");
        File.WriteAllText(scriptPath, "Start-Sleep -Seconds 600\r\n");

        var stopwatch = Stopwatch.StartNew();
        var act = () => TestProcessRunner.Run(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            directory.Path,
            TimeSpan.FromSeconds(3));

        act.Should().Throw<TimeoutException>().WithMessage("*did not exit*was killed*");
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(60),
            "the runner must give up on a hung script instead of blocking the gate behind it");
    }

    [Fact]
    public void Run_AnOrdinaryScript_IsUnaffectedByTheTimeout()
    {
        // Sibling/no-regression: the bound only converts an unbounded stall into a legible failure;
        // the scripts this suite actually runs finish in well under a second.
        using var directory = new TestTemporaryDirectory();
        var scriptPath = Path.Combine(directory.Path, "finishes.ps1");
        File.WriteAllText(scriptPath, "Write-Output 'done'\r\nexit 0\r\n");

        var result = PowerShellScriptRunner.Run(scriptPath, directory.Path);

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("done");
    }

    [Fact]
    public void ScriptTimeout_StaysFarAboveWhatTheseScriptsNeed()
    {
        // Pins the intent: this bound exists to catch a stall, not to police slow machines.
        TestProcessRunner.ScriptTimeout.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(5));
    }
}

/// <summary>
/// r164 remediation, tests tier. Every test that shells out to PowerShell 7 must be marked
/// <c>[RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]</c> so a machine without
/// pwsh SKIPS it rather than reporting a missing prerequisite as a failure.
///
/// This contract exists because marking them by hand went wrong once: the first pass marked the
/// tests that were OBSERVED failing rather than every test that CALLS the pwsh runner, and missed
/// InstallerPackagingContractTests.ReleaseSbom_RemovesItsPayloadStagingDirectoryAfterGeneration --
/// which then surfaced as the single red test in an otherwise green 5,342-test run. A source check
/// enforces what a memory of "which ones were red that day" cannot.
/// </summary>
public sealed class R164_PwshCallerMarkingContractTests
{
    [Fact]
    public void EveryPwshCallingTest_IsMarkedWithTheExternalToolAttribute()
    {
        var testDirectory = Path.GetDirectoryName(
            WorkspaceFileLocator.Find("tests", "FreeX.App.Host.Tests", "PowerShellScriptRunner.cs"))!;

        var unmarked = new List<string>();
        foreach (var file in Directory.GetFiles(testDirectory, "*.cs"))
        {
            // The runner itself declares the method, and this file names it in the scan below --
            // neither is a caller.
            var fileName = Path.GetFileName(file);
            if (fileName is "PowerShellScriptRunner.cs" or "R164_ScriptRunnerTimeoutTests.cs")
                continue;

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains("RunToolScriptWithPwsh", StringComparison.Ordinal))
                    continue;

                // Walk back to the attribute that introduces the enclosing test.
                for (var back = index; back >= 0; back--)
                {
                    var trimmed = lines[back].TrimStart();
                    if (trimmed.StartsWith("[RequiresExternalToolFact", StringComparison.Ordinal))
                        break;

                    if (trimmed.StartsWith("[Fact]", StringComparison.Ordinal) ||
                        trimmed.StartsWith("[Theory]", StringComparison.Ordinal))
                    {
                        unmarked.Add($"{Path.GetFileName(file)}:{back + 1}");
                        break;
                    }
                }
            }
        }

        unmarked.Should().BeEmpty(
            "a test that shells out to pwsh must be skipped, not failed, on a machine without it -- " +
            "mark it [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]");
    }
}
