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
        script.Should().Contain("Run-PackagedProductLaunchProbe.sh");
        script.Should().Contain("--executable \"$published/FreeX\"");
        script.Should().Contain("Linux workflow must exercise the published product apphost before recording launch_smoke_status=passed.");
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
    public void PackagedProductProbe_WaitsForProductReadinessAndTargetsOnlyItsChild()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find("tools", "Run-PackagedProductLaunchProbe.sh"));

        script.Should().Contain("\"$executable\" \"${app_arguments[@]}\" >\"$log_path\" 2>&1 &");
        script.Should().Contain("grep -R -F -q \"$readiness_marker\" \"$readiness_root\"");
        script.Should().Contain("process_is_active \"$probe_pid\"");
        script.Should().Contain("kill \"$probe_pid\"");
        script.Should().Contain("packaged_product_launch_status=passed");
        script.Should().NotContain("FreeX.Validation.Avalonia");
        script.Should().NotContain("pkill");
        script.Should().NotContain("killall");
    }

    [Fact]
    public void LaunchSmokeSource_ExposesPlatformNeutralAliases()
    {
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("tools", "FreeX.Validation.Avalonia", "MacOsLaunchSmoke.cs"));

        smokeSource.Should().Contain("public const string NeutralArgument = \"--launch-smoke\";");
        smokeSource.Should().Contain("public const string NeutralDiagnosticsDirectoryArgument = \"--launch-smoke-diagnostics-dir\";");
        smokeSource.Should().Contain("public const string NeutralVerifyImageClipboardPasteArgument = \"--launch-smoke-verify-image-clipboard\";");
        smokeSource.Should().Contain("public const string NeutralVerifyLiveCommandKeysArgument = \"--launch-smoke-verify-live-command-keys\";");
        // The macOS spellings stay intact for the existing hosted macOS lane.
        smokeSource.Should().Contain("public const string Argument = \"--macos-launch-smoke\";");
        smokeSource.Should().Contain("IsReportArgument(argument)");
    }

    [Fact]
    public void InteractiveDockerHarness_IsLocalOnlyOwnedAndAppSelectable()
    {
        var runner = File.ReadAllText(RepositoryFileLocator.Find("tools", "Run-LinuxInteractiveDocker.ps1"));
        var dockerfile = File.ReadAllText(RepositoryFileLocator.Find("tools", "LinuxInteractiveDocker", "Dockerfile"));
        var entrypoint = File.ReadAllText(RepositoryFileLocator.Find("tools", "LinuxInteractiveDocker", "entrypoint.sh"));
        var refreshAfterVnc = File.ReadAllText(RepositoryFileLocator.Find("tools", "LinuxInteractiveDocker", "refresh-after-vnc.sh"));
        var readme = File.ReadAllText(RepositoryFileLocator.Find("tools", "LinuxInteractiveDocker", "README.md"));

        runner.Should().Contain("[ValidateSet(\"FreeX\", \"FreeW\", \"FreeP\")]");
        runner.Should().Contain("127.0.0.1:$Port`:6080");
        runner.Should().Contain("\"--rm\"");
        runner.Should().Contain("\"--init\"");
        runner.Should().Contain("io.github.tony-xmelon.freex.linux-interactive");
        runner.Should().Contain("Container '$containerName' exists but is not owned by this harness.");
        runner.Should().Contain("freex-linux-interactive-app-$publishKey-$workspaceKey");
        runner.Should().Contain("$workspaceKey`:current");
        runner.Should().Contain("SHA256");
        runner.Should().Contain("& tar -czf $archivePath -C $publishDir .");
        runner.Should().Contain("COPY app.tar.gz /tmp/app.tar.gz");
        runner.Should().Contain("Docker image '$appImage' exists but is not owned by this harness.");
        runner.Should().Contain("FreeX-LinuxInteractive/$workspaceKey/$publishKey/publish/linux-x64");
        runner.Should().Contain("@(\"stop\", \"--timeout\", \"10\", $containerName) -AllowFailure");
        runner.Should().Contain("$null -ne (Get-OwnedContainerStatus)");
        runner.Should().NotContain("@(\"stop\", \"--time\", \"10\", $containerName)");

        dockerfile.Should().Contain("FROM ubuntu:24.04");
        dockerfile.Should().Contain("novnc");
        dockerfile.Should().Contain("openbox");
        dockerfile.Should().Contain("picom");
        dockerfile.Should().Contain("xclip");
        dockerfile.Should().Contain("x11vnc");
        dockerfile.Should().Contain("xvfb");

        entrypoint.Should().Contain("Xvfb :99");
        entrypoint.Should().Contain("picom");
        entrypoint.Should().Contain("--backend xrender");
        entrypoint.Should().Contain("-afteraccept /usr/local/bin/freex-refresh-after-vnc");
        entrypoint.Should().Contain("websockify");
        entrypoint.Should().Contain("xdotool search --onlyvisible");
        entrypoint.Should().Contain("/work/ready.json");
        entrypoint.Should().Contain("scrot /work/screenshots/initial.png");
        entrypoint.Should().Contain("--interaction-validation");
        entrypoint.Should().Contain("interaction validation exited with code");
        entrypoint.Should().Contain("/work/validation/interaction-validation.json");

        refreshAfterVnc.Should().Contain("remove,maximized_vert,maximized_horz");
        refreshAfterVnc.Should().Contain("add,maximized_vert,maximized_horz");

        readme.Should().Contain("http://127.0.0.1:6080/vnc.html");
        readme.Should().Contain("-Action Screenshot");
        readme.Should().Contain("-Action Stop");
    }
}
