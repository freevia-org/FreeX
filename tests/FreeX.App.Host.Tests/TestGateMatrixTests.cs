using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class TestGateMatrixTests
{
    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void Matrix_CoversEveryManifestGatePlatformPairAndLimitsFullHistoryCheckout()
    {
        var repositoryRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Get-TestGateMatrix.ps1",
            repositoryRoot,
            "-Gate all");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var document = JsonDocument.Parse(result.Output.Trim());
        var entries = document.RootElement.GetProperty("include").EnumerateArray().ToArray();

        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repositoryRoot, "eng", "test-gates.json")));
        var expectedEntries = manifest.RootElement.GetProperty("gates")
            .EnumerateArray()
            .SelectMany(gate => gate.GetProperty("platforms").EnumerateArray()
                .Select(platform => new
                {
                    GateId = gate.GetProperty("id").GetString(),
                    App = gate.GetProperty("app").GetString(),
                    Platform = platform.GetString(),
                    FetchDepth = gate.TryGetProperty("requiresFullHistory", out var property) && property.GetBoolean()
                        ? 0
                        : 1,
                }))
            .ToArray();

        entries.Should().HaveCount(expectedEntries.Length);
        entries.Select(entry => new
            {
                GateId = entry.GetProperty("gateId").GetString(),
                App = entry.GetProperty("app").GetString(),
                Platform = entry.GetProperty("platform").GetString(),
                FetchDepth = entry.GetProperty("fetchDepth").GetInt32(),
            })
            .Should().BeEquivalentTo(expectedEntries);
        entries.Select(entry => entry.GetProperty("app").GetString())
            .Should().Contain(new[] { "FreeX", "FreeW", "FreeP" });
        entries.Select(entry => entry.GetProperty("platform").GetString())
            .Should().Contain(new[] { "windows", "linux", "macos" });
    }

    [Fact]
    public void CanonicalCi_RetriesOnlyTheFailedProjectOnceAndRetainsAttemptEvidence()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "ci.yml");
        var runner = WorkspaceFileLocator.ReadAllText("tools", "Invoke-TestGate.ps1");

        workflow.Should().Contain("-RetryFailedProjectCount 1");
        runner.Should().Contain("[int]$RetryFailedProjectCount = 0");
        runner.Should().Contain("$attemptSuffix = if ($attempt -eq 0)");
        runner.Should().Contain("the initial TRX is retained");
        runner.Should().Contain("retrying only this project");
    }
}
