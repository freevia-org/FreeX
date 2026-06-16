using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class LinuxAppReadinessPreflightTests
{
    [Fact]
    public void StaticPreflight_ChecksPackagingWorkflowAndNeutralSmokeAlias()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find("tools", "Test-LinuxAppReadiness.ps1"));

        script.Should().Contain("$appId = \"io.github.tony-xmelon.freex\"");
        script.Should().Contain("\"$appId.desktop\"");
        script.Should().Contain("\"$appId.xml\"");
        script.Should().Contain("\"$appId.svg\"");
        script.Should().Contain("package-linux-app.sh");
        script.Should().Contain("build-appimage.sh");
        script.Should().Contain("linux-app.yml");
        script.Should().Contain("NeutralArgument = \"--launch-smoke\"");
        script.Should().Contain("Test-LinuxPublicPreviewReadiness.ps1");
        // The static lane must reject macOS-only machinery leaking into the Linux workflow.
        script.Should().Contain("codesign");
        script.Should().Contain("notarytool");
    }

    [Fact]
    public void ArtifactValidator_AssertsSmokeEvidenceAndChecksumIntegrity()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find("tools", "Test-LinuxPublicPreviewReadiness.ps1"));

        script.Should().Contain("linux-preview-readiness.v1");
        script.Should().Contain("packaging_smoke_status");
        script.Should().Contain("launch_smoke_status");
        script.Should().Contain("format_cells_style_roundtrip_count");
        script.Should().Contain("Get-FileHash");
        script.Should().Contain("tarball_sha256");
        script.Should().Contain("ConvertTo-Json");
        script.Should().Contain("linux-x64");
        script.Should().Contain("linux-arm64");
    }

    [Fact]
    public void LaunchSmokeSource_ExposesPlatformNeutralAliases()
    {
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));

        smokeSource.Should().Contain("public const string NeutralArgument = \"--launch-smoke\";");
        smokeSource.Should().Contain("public const string NeutralDiagnosticsDirectoryArgument = \"--launch-smoke-diagnostics-dir\";");
        smokeSource.Should().Contain("public const string NeutralVerifyImageClipboardPasteArgument = \"--launch-smoke-verify-image-clipboard\";");
        smokeSource.Should().Contain("public const string NeutralVerifyLiveCommandKeysArgument = \"--launch-smoke-verify-live-command-keys\";");
        // The macOS spellings stay intact for the existing hosted macOS lane.
        smokeSource.Should().Contain("public const string Argument = \"--macos-launch-smoke\";");
        smokeSource.Should().Contain("IsReportArgument(argument)");
    }
}
