using System.IO;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MacOsPublicPreviewReadinessPreflightTests
{
    [Fact]
    public void PublicPreviewReadinessPreflight_DocumentsEvidenceContractAndToolUsage()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-MacOsPublicPreviewReadiness.ps1");
        var signingRunbook = WorkspaceFileLocator.ReadAllText("docs", "release", "macos-signing-notarization.md");
        var distributionPlan = WorkspaceFileLocator.ReadAllText("docs", "release", "test-distribution.md");

        script.Should().Contain("artifact_channel");
        script.Should().Contain("distribution_readiness");
        script.Should().Contain("codesign_mode");
        script.Should().Contain("notarization_status");
        script.Should().Contain("stapler_validated");
        script.Should().Contain("zip_sha256");
        script.Should().Contain("format_cells_style_roundtrip_count");
        script.Should().Contain("live_command_key_smoke");
        script.Should().Contain("macos_launch_smoke");
        script.Should().Contain("RequireSeparateDiagnosticsArtifact");
        script.Should().Contain("freex-$Runtime-macos-open-with-launch-smoke.txt");
        script.Should().Contain("macOS public-preview evidence preflight passed");

        signingRunbook.Should().Contain("tools/Test-MacOsPublicPreviewReadiness.ps1");
        signingRunbook.Should().Contain("-DistributionCandidate");
        signingRunbook.Should().Contain("-RequireSeparateDiagnosticsArtifact");
        distributionPlan.Should().Contain("tools/Test-MacOsPublicPreviewReadiness.ps1");
        distributionPlan.Should().Contain("Windows-runnable");
    }

    [Fact]
    public void ReadinessPreflight_PassesForSyntheticInternalPreviewBundles()
    {
        using var temp = new TestTemporaryDirectory();
        CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);

        var result = RunPreflight(temp.Path);

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("macOS public-preview evidence preflight passed");
        result.Output.Should().Contain("osx-arm64");
        result.Output.Should().Contain("osx-x64");
    }

    [Fact]
    public void ReadinessPreflight_PassesForSyntheticDistributionCandidateBundlesWithDiagnosticsArtifact()
    {
        using var temp = new TestTemporaryDirectory();
        CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true, includeDiagnosticsArtifact: true);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true, includeDiagnosticsArtifact: true);

        var result = RunPreflight(temp.Path, "-DistributionCandidate -RequireSeparateDiagnosticsArtifact");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("macOS public-preview evidence preflight passed");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenDistributionCandidateLacksSigningEvidence()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true);
        ReplaceInFile(arm64.EvidencePath, "codesign_mode=developer-id", "codesign_mode=ad-hoc");
        ReplaceInFile(arm64.EvidencePath, "notarization_status=accepted", "notarization_status=skipped_missing_credentials");
        ReplaceInFile(arm64.EvidencePath, "stapler_validated=true", "stapler_validated=false");

        var result = RunPreflight(temp.Path, "-DistributionCandidate");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("codesign_mode=developer-id");
        result.CombinedOutput.Should().Contain("notarization_status=accepted");
        result.CombinedOutput.Should().Contain("stapler_validated=true");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenFormatCellsRoundtripCountIsTooLow()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        ReplaceInFile(arm64.EvidencePath, "format_cells_style_roundtrip_count=2", "format_cells_style_roundtrip_count=1");
        File.WriteAllText(
            arm64.PackagingSmokePath,
            Lines(
                "Packaging smoke opened macOS Preview Workbook.",
                "edited, saved, and reopened.",
                "format_cells_style_roundtrip=true"));

        var result = RunPreflight(temp.Path);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("format_cells_style_roundtrip_count");
        result.CombinedOutput.Should().Contain("at least two Format Cells style roundtrip confirmations");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenChecksumDoesNotMatchZip()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        File.AppendAllText(arm64.ZipPath, "corrupt");

        var result = RunPreflight(temp.Path);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("checksum file hash must match");
        result.CombinedOutput.Should().Contain("zip_sha256");
    }

    private static PowerShellResult RunPreflight(string artifactRoot, string arguments = "")
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        return PowerShellScriptRunner.RunToolScript(
            "Test-MacOsPublicPreviewReadiness.ps1",
            repoRoot,
            $"-ArtifactRoot \"{artifactRoot}\" {arguments}");
    }

    private static SyntheticBundle CreateSyntheticBundle(
        string root,
        string runtime,
        bool distributionCandidate,
        bool includeDiagnosticsArtifact = false)
    {
        var names = RuntimeArtifactNames.For(runtime);
        var bundleDirectory = Path.Combine(root, $"freex-42-1-{runtime}-macos-app");
        Directory.CreateDirectory(bundleDirectory);

        var zipPath = Path.Combine(bundleDirectory, names.Zip);
        File.WriteAllText(zipPath, $"Synthetic FreeX.app zip for {runtime}.");
        var zipHash = ComputeSha256(zipPath);
        File.WriteAllText(Path.Combine(bundleDirectory, names.Checksum), $"{zipHash}  {names.Zip}{Environment.NewLine}");

        var channel = distributionCandidate ? "distribution-candidate" : "internal-preview";
        var candidate = distributionCandidate ? "true" : "false";
        var contract = distributionCandidate
            ? "distribution_candidate_requires_developer_id_notarization_stapling"
            : "internal_preview_not_for_distribution_notarization_optional";
        var readiness = distributionCandidate
            ? "distribution_candidate_ready"
            : "internal_preview_not_for_distribution";
        var codesignMode = distributionCandidate ? "developer-id" : "ad-hoc";
        var notarizationStatus = distributionCandidate ? "accepted" : "skipped_missing_credentials";
        var staplerValidated = distributionCandidate ? "true" : "false";

        var evidencePath = Path.Combine(bundleDirectory, names.Evidence);
        File.WriteAllText(
            evidencePath,
            Lines(
                $"runtime={runtime}",
                $"artifact_channel={channel}",
                $"distribution_candidate={candidate}",
                $"distribution_contract={contract}",
                $"distribution_readiness={readiness}",
                $"zip_name={names.Zip}",
                "codesign_verified=true",
                $"codesign_mode={codesignMode}",
                $"notarization_status={notarizationStatus}",
                $"stapler_validated={staplerValidated}",
                $"zip_sha256={zipHash}",
                "format_cells_style_roundtrip=true",
                "format_cells_style_roundtrip_count=2",
                "smoke_status=passed"));

        var packagingSmokePath = Path.Combine(bundleDirectory, names.PackagingSmoke);
        File.WriteAllText(
            packagingSmokePath,
            Lines(
                "Packaging smoke opened macOS Preview Workbook.",
                "drawing_object_previews=3",
                "roundtrip_drawing_object_previews=3",
                "edited, saved, and reopened.",
                "format_cells_style_roundtrip=true",
                "Packaging smoke opened freex fixture csv.",
                "edited, saved, and reopened.",
                "format_cells_style_roundtrip=true"));

        File.WriteAllText(
            Path.Combine(bundleDirectory, names.LaunchSmoke),
            Lines(
                "macos_launch_smoke=passed",
                "window_shown=true",
                $"opened_source_path=/tmp/freex-{runtime}-launch.csv",
                "viewport_rows=24",
                "viewport_columns=8",
                "native_open_recent_menu_item=true",
                "native_open_recent_item_count=1",
                "live_command_key_smoke_required=true",
                "live_command_key_smoke=passed",
                "live_command_key_smoke_attempted=true",
                "live_command_key_smoke_ready=true",
                "live_cmd_bold_state_changed=true",
                "live_cmd_italic_state_changed=true",
                "live_cmd_underline_state_changed=true"));

        File.WriteAllText(
            Path.Combine(bundleDirectory, names.OpenWithSmoke),
            Lines(
                "macos_launch_smoke=passed",
                "window_shown=true",
                $"opened_source_path=/tmp/freex-{runtime}-open-with.csv",
                "viewport_rows=24",
                "viewport_columns=8",
                "native_open_recent_menu_item=true",
                "native_open_recent_item_count=1"));

        File.WriteAllText(
            Path.Combine(bundleDirectory, names.NotarizationLog),
            distributionCandidate
                ? Lines(
                    "artifact_channel=distribution-candidate",
                    "distribution_candidate=true",
                    $"distribution_contract={contract}",
                    "{\"status\":\"Accepted\"}",
                    "xcrun stapler validate FreeX.app")
                : Lines(
                    "artifact_channel=internal-preview",
                    "distribution_candidate=false",
                    $"distribution_contract={contract}",
                    "notarization_status=skipped_missing_credentials"));

        File.WriteAllText(
            Path.Combine(bundleDirectory, names.TesterInstructions),
            Lines(
                $"# FreeX macOS App ({channel}, {runtime})",
                "This artifact is a macOS port validation build. Internal-preview artifacts are not a public release channel.",
                "For artifact_channel=internal-preview: This artifact is a preview build for macOS port validation. It is not a public release channel.",
                $"Download {names.Zip}, {names.Checksum}, {names.Evidence}, {names.PackagingSmoke}, {names.LaunchSmoke}, {names.OpenWithSmoke}, {names.NotarizationLog}.",
                $"Run shasum -a 256 -c {names.Checksum}.",
                $"artifact_channel={channel}",
                $"distribution_readiness={readiness}",
                $"codesign_mode={codesignMode}",
                $"notarization_status={notarizationStatus}",
                $"stapler_validated={staplerValidated}",
                $"zip_sha256={zipHash}",
                "If artifact_channel=distribution-candidate, reject the artifact unless it has Developer ID signing, accepted notarization, and stapling evidence."));

        if (includeDiagnosticsArtifact)
        {
            var diagnosticsDirectory = Path.Combine(root, $"freex-42-1-{runtime}-macos-diagnostics");
            Directory.CreateDirectory(diagnosticsDirectory);
            foreach (var file in Directory.EnumerateFiles(bundleDirectory))
            {
                File.Copy(file, Path.Combine(diagnosticsDirectory, Path.GetFileName(file)), overwrite: true);
            }
        }

        return new SyntheticBundle(
            bundleDirectory,
            zipPath,
            evidencePath,
            packagingSmokePath);
    }

    private static string Lines(params string[] lines) =>
        string.Join(Environment.NewLine, lines) + Environment.NewLine;

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void ReplaceInFile(string path, string oldValue, string newValue)
    {
        var text = File.ReadAllText(path);
        text.Should().Contain(oldValue);
        File.WriteAllText(path, text.Replace(oldValue, newValue));
    }

    private sealed record SyntheticBundle(
        string BundleDirectory,
        string ZipPath,
        string EvidencePath,
        string PackagingSmokePath);

    private sealed record RuntimeArtifactNames(
        string Zip,
        string Checksum,
        string Evidence,
        string PackagingSmoke,
        string LaunchSmoke,
        string OpenWithSmoke,
        string NotarizationLog,
        string TesterInstructions)
    {
        public static RuntimeArtifactNames For(string runtime) =>
            new(
                $"freex-{runtime}-macos-app.zip",
                $"freex-{runtime}-macos-app.zip.sha256",
                $"freex-{runtime}-macos-evidence.txt",
                $"freex-{runtime}-macos-packaging-smoke.log",
                $"freex-{runtime}-macos-launch-smoke.txt",
                $"freex-{runtime}-macos-open-with-launch-smoke.txt",
                $"freex-{runtime}-macos-notarization.log",
                $"freex-{runtime}-macos-tester-instructions.md");
    }
}
