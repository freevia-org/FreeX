using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class LinuxFamilyInteractionToolTests
{
    [Fact]
    public void FamilyProbeIsParameterizedAndLeavesTheExhaustiveFreeXRunnerUntouched()
    {
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-family-input-probes.sh"));

        probe.Should().Contain("FAMILY_APP");
        probe.Should().Contain("FAMILY_WINDOW_PATTERN");
        probe.Should().Contain("FAMILY_TAB_KEY");
        probe.Should().Contain("FAMILY_FILE_SURFACE");
        probe.Should().Contain("visible-window-discovery");
        probe.Should().Contain("run_keytip_cycle \"alt-keytips\" Alt_L");
        probe.Should().Contain("run_keytip_cycle \"f10-keytips\" F10");
        probe.Should().Contain("${id_prefix}-appearance");
        probe.Should().Contain("ribbon-tab-keytip-switch");
        probe.Should().Contain("file-surface-open");
        probe.Should().Contain("editor-sentinel-copy");
        probe.Should().Contain("editor-undo-restores-clipboard");
        probe.Should().Contain("editor-redo-restores-clipboard");
        probe.Should().Contain("editor-keyboard-context-open");
        probe.Should().Contain("editor-pointer-context-open");
        probe.Should().Contain("slide-pane-new-slide-create");
        probe.Should().Contain("slide-pane-new-slide-undo");
        probe.Should().Contain("slide-pane-new-slide-redo");
        probe.Should().Contain("slide-pane-keyboard-context-open");
        probe.Should().Contain("slide-pane-pointer-context-open");
        probe.Should().Contain("slide-pane-pointer-select-second");
        probe.Should().Contain("slide-pane-keyboard-up-first");
        probe.Should().Contain("slide-pane-duplicate-create");
        probe.Should().Contain("slide-pane-duplicate-undo");
        probe.Should().Contain("slide-pane-duplicate-redo");
        probe.Should().Contain("slide-pane-delete-selected");
        probe.Should().Contain("slide-pane-delete-undo");
        probe.Should().Contain("status-geometry=");
        probe.Should().Contain("navigation_start_gate");
        probe.Should().Contain("capture_region");
        probe.Should().Contain("slide-pane-calibration.txt");
        probe.Should().Contain("redo-gated-on-create-and-undo");
        probe.Should().Contain("slide-pane-stable-band=thumbnail-area-below-ribbon-above-button-and-status");
        probe.Should().Contain("FAMILY_X11_POINTER_TIMEOUT_SECONDS");
        probe.Should().Contain("FAMILY_X11_CLIPBOARD_TIMEOUT_SECONDS");
        probe.Should().Contain("timeout --foreground --kill-after=1s");
        probe.Should().Contain("xclip -selection clipboard -o");
        probe.Should().Contain("xclip -silent -selection clipboard -in");
        probe.Should().Contain("stop_clipboard_owner");
        probe.Should().Contain("screen_matches");
        probe.Should().Contain("trap on_exit EXIT");
        probe.Should().Contain("required_ids=(");
        probe.Should().Contain("has_result");
        probe.Should().Contain("Probe exited before collecting this required row");
        probe.Should().Contain("probe-failure.png");
        probe.Should().Contain("family-x11-results.json");
        probe.Should().NotContain("FreeX-specific");
        probe.Should().NotContain("run-freex-input-probes.sh");
    }

    [Fact]
    public void FamilyRunnerDeclaresOnlyFreeWAndFreePAndValidatesTheManifest()
    {
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FamilyLinuxInteractionValidation.ps1"));
        var evidenceHelper = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "ManifestEvidence.ps1"));
        var source = runner + Environment.NewLine + evidenceHelper;

        runner.Should().Contain("[ValidateSet(\"FreeW\", \"FreeP\")]");
        runner.Should().Contain("Assert-ManifestContract");
        source.Should().Contain("Wait-ForManifestEvidence");
        source.Should().Contain("Start-Sleep -Milliseconds $PollMilliseconds");
        source.Should().Contain("evidence-settle-timeout.txt");
        source.Should().Contain("Manifest evidence did not settle within");
        source.Should().Contain("ConvertFrom-Json -ErrorAction Stop");
        source.Should().Contain("last-manifest-read-error:");
        source.Should().Contain("previousCompleteSizeSignature");
        source.Should().Contain("last-observed-size-state:");
        source.Should().Contain("completeSizeSignature -eq $previousCompleteSizeSignature");
        source.Should().Contain("Get-ChildItem -LiteralPath $EvidenceDirectory -File");
        source.Should().NotContain("Test-Path -LiteralPath $path");
        source.Should().NotContain("Get-Item -LiteralPath $path");
        runner.Should().Contain("family-x11-validation.schema.json");
        runner.Should().Contain("contractValidation");
        runner.Should().Contain("parameters.fileKey");
        runner.Should().Contain("appSurface");
        runner.Should().Contain("Length -le 0");
        runner.Should().Contain("exhaustive -ne $false");
        runner.Should().Contain("Run-FreeXLinuxInteractionValidation.ps1");
        runner.Should().Contain("$expectedResultCount = if ($App -eq \"FreeP\") { 22 } else { 15 }");
        runner.Should().Contain("slide-pane-delete-undo");
        runner.Should().Contain("editor-keyboard-context-dismissal");
        runner.Should().Contain("durable failure manifest");
        runner.Should().Contain("probe-runner-failure.txt");
        runner.Should().Contain("screenshots/initial.png");
        runner.Should().Contain("probe-runner-failure.png");
        runner.Should().NotContain("name = \"baseline.png\"; kind = \"screenshot\"");
    }

    [Fact]
    public void FamilySchemaRequiresBaselineEvidenceAndExplicitNonExhaustiveCoverage()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "family-x11-validation.schema.json")));
        var root = document.RootElement;

        root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32()
            .Should().Be(1);
        root.GetProperty("properties").GetProperty("app").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()).Should().BeEquivalentTo("FreeW", "FreeP");
        root.GetProperty("properties").GetProperty("coverage").GetProperty("properties")
            .GetProperty("exhaustive").GetProperty("const").GetBoolean().Should().BeFalse();
        root.GetProperty("properties").GetProperty("results").GetProperty("minItems").GetInt32()
            .Should().BeGreaterThanOrEqualTo(8);
        var freePContract = root.GetProperty("allOf")[0].GetProperty("then").GetProperty("properties").GetProperty("results");
        freePContract.GetProperty("minItems").GetInt32().Should().Be(22);
        freePContract.GetProperty("maxItems").GetInt32().Should().Be(22);
        var freeWContract = root.GetProperty("allOf")[1].GetProperty("then").GetProperty("properties").GetProperty("results");
        freeWContract.GetProperty("minItems").GetInt32().Should().Be(15);
        freeWContract.GetProperty("maxItems").GetInt32().Should().Be(15);
        root.GetProperty("allOf").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void FamilyParityDocStatesBaselineBoundaryAndAppSurfaceDifferences()
    {
        var doc = File.ReadAllText(RepositoryFileLocator.Find(
            "docs", "parity", "family-linux-physical-baseline-2026-07-23.md"));

        doc.Should().Contain("not exhaustive");
        doc.Should().Contain("FreeW");
        doc.Should().Contain("FreeP");
        doc.Should().Contain("top-level");
        doc.Should().Contain("FreePBackstageOverlay");
        doc.Should().Contain("contractValidation");
        doc.Should().Contain("Run-FamilyLinuxInteractionValidation.ps1");
        doc.Should().Contain("exact twenty-two-row contract");
        doc.Should().Contain("slide-pane-new-slide-create");
        doc.Should().Contain("slide-pane-delete-undo");
        doc.Should().Contain("Ctrl+Z").And.Contain("Shift+F10");
    }

    [Fact]
    public void FamilyRunnerUsesCurrentAvaloniaRibbonTabKeyTipsAndFileSurfaceContracts()
    {
        var freeWDefinition = File.ReadAllText(RepositoryFileLocator.Find(
            "freew", "FreeW.Ribbon.Definitions", "FreeWAvaloniaRibbonDefinition.cs"));
        var freePResources = File.ReadAllText(RepositoryFileLocator.Find(
            "freep", "FreeP.App.Localization", "Resources", "Strings.resx"));
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FamilyLinuxInteractionValidation.ps1"));

        freeWDefinition.Should().Contain(".Tab(\"insert\", \"Insert\", \"I\"");
        freePResources.Should().Contain("Ribbon_Tab_Insert_KeyTip").And.Contain("<value>N</value>");
        runner.Should().Contain("RibbonTabKey = \"I\"").And.Contain("RibbonTabKey = \"N\"");
        runner.Should().Contain("WindowPattern = \"FreeW\"").And.Contain("WindowPattern = \"FreeP\"");
        runner.Should().Contain("FileSurface = \"top-level-backstage-window\"");
        runner.Should().Contain("FileSurface = \"in-window-backstage-overlay\"");
    }

    [Fact]
    public void ManifestEvidenceSettle_UsesDirectChildMapForLongEvidencePaths()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var powershell = ResolvePowerShellExecutable();
        powershell.Should().NotBeNull("the long-path evidence regression requires PowerShell");

        using var temporary = new TestTemporaryDirectory();
        var evidenceDirectory = Path.Combine(temporary.Path, new string('a', 150));
        Directory.CreateDirectory(evidenceDirectory);
        var evidenceName = new string('b', 130) + ".proof.txt";
        File.WriteAllText(Path.Combine(evidenceDirectory, evidenceName), "proof", Encoding.UTF8);

        var manifestPath = Path.Combine(temporary.Path, "family-x11-results.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            results = new[] { new { evidence = new[] { evidenceName } } },
            screenshots = Array.Empty<object>()
        }), Encoding.UTF8);

        var helperPath = RepositoryFileLocator.Find("tools", "LinuxInteractiveDocker", "ManifestEvidence.ps1");
        var command = $". '{EscapePowerShell(helperPath)}'; Wait-ForManifestEvidence -ManifestPath '{EscapePowerShell(manifestPath)}' -EvidenceDirectory '{EscapePowerShell(evidenceDirectory)}' -TimeoutSeconds 3 -PollMilliseconds 50; 'settled'";
        var result = RunPowerShellCommand(powershell!, command);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("settled");
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static ProbeResult RunPowerShellCommand(string executable, string command)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });
        process.Should().NotBeNull();
        var output = process!.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(10000).Should().BeTrue("the evidence regression must remain bounded");
        return new ProbeResult(process.ExitCode, output);
    }

    private static string? ResolvePowerShellExecutable()
    {
        foreach (var candidate in new[] { "pwsh.exe", "powershell.exe" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "-NoProfile -NonInteractive -Command \"exit 0\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process is not null && process.WaitForExit(5000) && process.HasExited)
                    return candidate;
            }
            catch (Win32Exception)
            {
            }
        }
        return null;
    }

    private sealed record ProbeResult(int ExitCode, string Output);
}
