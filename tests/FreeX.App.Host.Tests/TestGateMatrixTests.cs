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
    public void CanonicalCi_FailsVisibleProjectsAndCapturesBoundedHangDiagnostics()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "ci.yml");
        var runner = WorkspaceFileLocator.ReadAllText("tools", "Invoke-TestGate.ps1");

        workflow.Should().NotContain("-RetryFailedProjectCount");
        workflow.Should().Contain("-HangTimeout 15m");
        workflow.Should().Contain("\"**/TestResults/**\"");
        runner.Should().Contain("[int]$RetryFailedProjectCount = 0");
        runner.Should().Contain("[string]$HangTimeout = \"15m\"");
        runner.Should().Contain("\"--blame-hang-timeout\", $HangTimeout");
    }

    [Fact]
    public void Manifest_ReservesUiHostsForReleaseAndShardsEveryWpfBatch()
    {
        using var manifest = JsonDocument.Parse(WorkspaceFileLocator.ReadAllText("eng", "test-gates.json"));
        var gates = manifest.RootElement.GetProperty("gates").EnumerateArray().ToArray();

        gates.Single(gate => gate.GetProperty("id").GetString() == "freex-wpf-shell")
            .GetProperty("gate").GetString().Should().Be("release");
        var batches = gates.Where(gate => gate.GetProperty("id").GetString()!.StartsWith("freex-wpf-host-batch", StringComparison.Ordinal)).ToArray();
        batches.Should().HaveCount(7);
        batches.Should().OnlyContain(gate =>
            gate.GetProperty("gate").GetString() == "release" &&
            gate.GetProperty("projects").GetArrayLength() == 1);
    }

    [Fact]
    public void Manifest_RunsPlatformNeutralSuitesOnceAndKeepsPortableSuitesCrossPlatform()
    {
        using var manifest = JsonDocument.Parse(WorkspaceFileLocator.ReadAllText("eng", "test-gates.json"));
        var gates = manifest.RootElement.GetProperty("gates").EnumerateArray().ToArray();

        var neutral = gates.Where(gate =>
            gate.GetProperty("id").GetString()!.EndsWith("-neutral", StringComparison.Ordinal)).ToArray();
        neutral.Should().HaveCount(3);
        neutral.Should().OnlyContain(gate =>
            gate.GetProperty("gate").GetString() == "commit" &&
            gate.GetProperty("platforms").GetArrayLength() == 1 &&
            gate.GetProperty("platforms").EnumerateArray().Single().GetString() == "linux");

        var portable = gates.Where(gate =>
            gate.GetProperty("id").GetString()!.EndsWith("-portable", StringComparison.Ordinal)).ToArray();
        portable.Should().HaveCount(3);
        portable.Should().OnlyContain(gate =>
            gate.GetProperty("platforms").EnumerateArray().Select(value => value.GetString())
                .SequenceEqual(new[] { "windows", "linux", "macos" }));
    }

    [Fact]
    public void CodeQl_UsesProductionOnlyNoBuildScope()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "codeql.yml");
        var config = WorkspaceFileLocator.ReadAllText(".github", "codeql", "codeql-config.yml");

        workflow.Should().Contain("build-mode: none");
        workflow.Should().Contain("config-file: ./.github/codeql/codeql-config.yml");
        config.Should().Contain("paths-ignore:");
        config.Should().Contain("- tests/**");
        config.Should().Contain("- freew/**/*Tests/**");
        config.Should().Contain("- freep/**/*Tests/**");
        config.Should().Contain("- tools/**");
    }
}
