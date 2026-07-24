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

        runner.Should().Contain("[ValidateSet(\"FreeW\", \"FreeP\")]");
        runner.Should().Contain("Assert-ManifestContract");
        runner.Should().Contain("family-x11-validation.schema.json");
        runner.Should().Contain("contractValidation");
        runner.Should().Contain("parameters.fileKey");
        runner.Should().Contain("appSurface");
        runner.Should().Contain("Length -le 0");
        runner.Should().Contain("exhaustive -ne $false");
        runner.Should().Contain("Run-FreeXLinuxInteractionValidation.ps1");
        runner.Should().Contain("FreeP family baseline must retain exactly eight result rows");
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
        doc.Should().Contain("exact eight-row contract");
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
}
