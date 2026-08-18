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
        probe.Should().Contain("editor-cut-undo-restores");
        probe.Should().Contain("editor-paste-text-only");
        probe.Should().Contain("editor-find-open");
        probe.Should().Contain("editor-find-dismissal");
        probe.Should().Contain("editor-replace-open");
        probe.Should().Contain("editor-replace-dismissal");
        probe.Should().Contain("editor-reveal-formatting-open");
        probe.Should().Contain("editor-reveal-formatting-dismissal");
        probe.Should().Contain("editor-thesaurus-open");
        probe.Should().Contain("editor-thesaurus-dismissal");
        var findReplaceStart = probe.IndexOf("run_find_replace_route()", StringComparison.Ordinal);
        var findReplaceEnd = probe.IndexOf("run_side_pane_toggle_probe()", findReplaceStart, StringComparison.Ordinal);
        findReplaceStart.Should().BeGreaterThanOrEqualTo(0);
        findReplaceEnd.Should().BeGreaterThan(findReplaceStart);
        var findReplaceRoute = probe[findReplaceStart..findReplaceEnd];
        findReplaceRoute.Should().Contain("if ! send_active_key \"$key\"");
        findReplaceRoute.Should().NotContain("if ! send_editor_key \"$key\"");
        probe.Should().Contain("editor-keyboard-context-open");
        probe.Should().Contain("editor-pointer-context-open");
        probe.Should().Contain("slide-pane-new-slide-create");
        probe.Should().Contain("nested-keytip-prefix-deferral");
        probe.Should().Contain("send_key b");
        probe.Should().Contain("send_key i");
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
        probe.Should().Contain("FAMILY_X11_TEXT_ENTRY_MARGIN_MS");
        probe.Should().Contain("FAMILY_X11_TEXT_CLEANUP_TIMEOUT_SECONDS");
        probe.Should().Contain("timeout --foreground --kill-after=1s");
        probe.Should().Contain("xclip -selection clipboard -o");
        probe.Should().Contain("xclip -silent -selection clipboard -in");
        probe.Should().Contain("stop_clipboard_owner");
        probe.Should().Contain("run_file_shortcut_window_lifecycle \\\n        \"file-open-shortcut-dialog\" ctrl+o");
        probe.Should().Contain("if ! send_active_key \"$key\"; then");
        probe.Should().NotContain("run_file_shortcut_window_lifecycle +");
        probe.Should().Contain("candidate-class-availability=");
        probe.Should().Contain("unavailable-native-window-metadata");
        probe.Should().Contain("\"editor-expected-sentinel.txt\"");
        probe.Should().Contain("\"file-new-shortcut-cancel-clipboard.txt\"");
        probe.Should().Contain("\"file-new-shortcut-empty-marker.txt\"");
        probe.Should().Contain("\"file-new-shortcut-empty-clipboard.txt\"");
        probe.Should().NotContain("$class_ready");
        probe.Should().NotContain("$prompt_class\" == *WM_CLASS*");
        var shortcutLifecycleStart = probe.IndexOf("run_file_shortcut_window_lifecycle()", StringComparison.Ordinal);
        var shortcutLifecycleEnd = probe.IndexOf("run_dirty_new_prompt_probe()", shortcutLifecycleStart, StringComparison.Ordinal);
        shortcutLifecycleStart.Should().BeGreaterThanOrEqualTo(0);
        shortcutLifecycleEnd.Should().BeGreaterThan(shortcutLifecycleStart);
        var shortcutLifecycle = probe[shortcutLifecycleStart..shortcutLifecycleEnd];
        shortcutLifecycle.IndexOf("    focus_app", StringComparison.Ordinal)
            .Should().BeLessThan(shortcutLifecycle.IndexOf("    if ! send_active_key \"$key\"; then", StringComparison.Ordinal));
        foreach (var id in new[]
        {
            "file-open-shortcut-dialog-open",
            "file-open-shortcut-dialog-dismissal",
            "file-save-shortcut-dialog-open",
            "file-save-shortcut-dialog-dismissal",
            "file-save-as-shortcut-dialog-open",
            "file-save-as-shortcut-dialog-dismissal",
            "file-print-shortcut-dialog-open",
            "file-print-shortcut-dialog-dismissal",
            "file-new-shortcut-dirty-prompt-open",
            "file-new-shortcut-cancel-preserves",
            "file-new-shortcut-discard-creates-clean",
            "backstage-print-open",
            "backstage-print-dismissal",
            "backstage-export-open",
            "backstage-export-dismissal",
            "options-open",
            "options-tab-navigation",
            "options-focus",
            "options-close"
        })
        {
            probe.Should().Contain($"\"{id}\"");
        }
        probe.Should().Contain("run_backstage_pane_lifecycle \"backstage-print\" 438 \"Print\"");
        probe.Should().Contain("run_backstage_pane_lifecycle \"backstage-export\" 481 \"Export\"");
        probe.Should().Contain("read_window_geometry()");
        probe.Should().Contain("screen_changed \"$output/$backstage_open\" \"$output/$pane_open\"");
        probe.Should().Contain("send_active_key ctrl+Tab");
        probe.Should().Contain("options-rail-click=");
        probe.Should().Contain("options-action-click=");
        probe.Should().Contain("backstage-removed-for-dialog=");
        probe.Should().Contain("window-count-restored=");
        probe.Should().Contain("\"editor-autocorrect-typing\"");
        probe.Should().Contain("send_active_text 'I teh '");
        probe.Should().Contain("printf '%s' 'I the '");
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
    public void FamilyProbeBoundsLongTextEntryAndReleasesKeysAfterFailure()
    {
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-family-input-probes.sh"));
        var start = probe.IndexOf("send_active_text()", StringComparison.Ordinal);
        var end = probe.IndexOf("send_editor_key()", start, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        var textEntry = probe[start..end];

        textEntry.Should().Contain("text_length=\"${#text_value}\"");
        textEntry.Should().Contain("text_budget_ms");
        textEntry.Should().Contain("text_timeout_seconds");
        textEntry.Should().Contain("release_active_text_keys \"$active_id\" \"$text_value\"");
        textEntry.IndexOf("release_active_text_keys \"$active_id\" \"$text_value\"", StringComparison.Ordinal)
            .Should().BeLessThan(textEntry.IndexOf("if ! timeout", StringComparison.Ordinal));
        textEntry.Should().NotContain("\"$pointer_timeout_seconds\"");
        probe.Should().Contain("xdotool keyup --window \"$active_id\" \"$key_name\"");
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
        source.Should().Contain("Length -le 0");
        runner.Should().Contain("exhaustive -ne $false");
        runner.Should().Contain("Run-FreeXLinuxInteractionValidation.ps1");
        runner.Should().Contain("$expectedResultCount = if ($App -eq \"FreeP\") { 24 } else { 45 }");
        runner.Should().Contain("if ($App -eq \"FreeW\") { $startArguments += \"-CupsDryRun\" }");
        foreach (var id in new[]
        {
            "file-open-shortcut-dialog-open",
            "file-open-shortcut-dialog-dismissal",
            "file-save-shortcut-dialog-open",
            "file-save-shortcut-dialog-dismissal",
            "file-save-as-shortcut-dialog-open",
            "file-save-as-shortcut-dialog-dismissal",
            "file-print-shortcut-dialog-open",
            "file-print-shortcut-dialog-dismissal",
            "file-new-shortcut-dirty-prompt-open",
            "file-new-shortcut-cancel-preserves",
            "file-new-shortcut-discard-creates-clean",
            "backstage-print-open",
            "backstage-print-dismissal",
            "backstage-export-open",
            "backstage-export-dismissal",
            "options-open",
            "options-tab-navigation",
            "options-focus",
            "options-close"
        })
        {
            runner.Should().Contain($"\"{id}\"");
        }
        runner.Should().Contain("slide-pane-delete-undo");
        runner.Should().Contain("nested-keytip-prefix-deferral");
        runner.Should().Contain("editor-keyboard-context-dismissal");
        runner.Should().Contain("editor-find-open");
        runner.Should().Contain("editor-replace-open");
        runner.Should().Contain("editor-reveal-formatting-open");
        runner.Should().Contain("editor-thesaurus-open");
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
        freePContract.GetProperty("minItems").GetInt32().Should().Be(24);
        freePContract.GetProperty("maxItems").GetInt32().Should().Be(24);
        var freeWContract = root.GetProperty("allOf")[1].GetProperty("then").GetProperty("properties").GetProperty("results");
        freeWContract.GetProperty("minItems").GetInt32().Should().Be(45);
        freeWContract.GetProperty("maxItems").GetInt32().Should().Be(45);
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
        doc.Should().Contain("exact twenty-four-row contract");
        doc.Should().Contain("exact forty-five-row contract");
        doc.Should().Contain("animation-pane-physical-workflow");
        doc.Should().Contain("file-new-shortcut-discard-creates-clean");
        doc.Should().Contain("backstage-print-open");
        doc.Should().Contain("backstage-export-open");
        doc.Should().Contain("options-tab-navigation");
        doc.Should().Contain("slide-pane-new-slide-create");
        doc.Should().Contain("slide-pane-delete-undo");
        doc.Should().Contain("nested-keytip-prefix-deferral");
        doc.Should().Contain("Ctrl+Z").And.Contain("Shift+F10");
    }

    [Fact]
    public void Wave95FreeWPhysicalExpansionDocStatesBoundedBackstageAndOptionsRows()
    {
        var doc = File.ReadAllText(RepositoryFileLocator.Find(
            "docs", "parity", "freew-wave95-physical-backstage-options-20260801.md"));

        doc.Should().Contain("exactly forty-five result rows");
        doc.Should().Contain("backstage-print-dismissal");
        doc.Should().Contain("backstage-export-dismissal");
        doc.Should().Contain("options-open");
        doc.Should().Contain("options-focus");
        doc.Should().Contain("physical-x11-input");
        doc.Should().Contain("coverage.exhaustive");
        doc.Should().Contain("No product files");
    }

    [Fact]
    public void FamilyRunnerUsesCurrentAvaloniaRibbonTabKeyTipsAndFileSurfaceContracts()
    {
        var freeWDefinition = File.ReadAllText(RepositoryFileLocator.Find(
            "freew", "FreeW.Ribbon.Definitions", "FreeWRibbon.cs"));
        var freeWCanonicalTabs = File.ReadAllText(RepositoryFileLocator.Find(
            "freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.Ordinary.cs"));
        var freePResources = File.ReadAllText(RepositoryFileLocator.Find(
            "freep", "FreeP.App.Localization", "Resources", "Strings.resx"));
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FamilyLinuxInteractionValidation.ps1"));

        freeWDefinition.Should().Contain(".AddInsertTab(capabilities)");
        freeWCanonicalTabs.Should().Contain(".Tab(\"insert\", \"Insert\", \"N\",");
        freePResources.Should().Contain("Ribbon_Tab_Insert_KeyTip").And.Contain("<value>N</value>");
        runner.Should().Contain("RibbonTabKey = \"I\"").And.Contain("RibbonTabKey = \"N\"");
        runner.Should().Contain("WindowPattern = \"FreeW\"").And.Contain("WindowPattern = \"FreeP\"");
        runner.Should().Contain("FileSurface = \"top-level-backstage-window\"");
        runner.Should().Contain("FileSurface = \"in-window-backstage-overlay\"");
    }

    [Fact]
    public void FreeWFieldShortcutLaneUsesPhysicalKeysAndStructuredPersistenceInspection()
    {
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeWFieldShortcutValidation.ps1"));
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freew-field-shortcut-probe.sh"));
        var fixture = File.ReadAllText(RepositoryFileLocator.Find(
            "freew", "tools", "FreeW.FieldShortcutFixture", "Program.cs"));

        runner.Should().Contain("field-shortcut-validation.schema.json");
        runner.Should().Contain("DocxReader").And.Contain("saved-field-inspection.txt");
        runner.Should().Contain("coverage.exhaustive -ne $false");
        foreach (var id in new[]
        {
            "visible-window-discovery",
            "field-code-shortcut-show",
            "field-code-shortcut-hide",
            "field-update-shortcut-persist"
        })
        {
            probe.Should().Contain(id);
        }
        probe.Should().Contain("xdotool key").And.Contain("alt+F9").And.Contain("send_key F9").And.Contain("send_key ctrl+s");
        probe.Should().Contain("capture_editor_region").And.Contain("sha256sum").And.Contain("active-window=").And.Contain("focus-window=");
        runner.Should().Contain("FIELD_EXPECTED_DOCUMENT_NAME=$fixtureFileName");
        probe.Should().Contain("candidate_title").And.Contain("expected_document_name");
        probe.Should().Contain("owner_has_focus field-update-after-save")
            .And.Contain("owner_title_matches_expected_document field-update-after-save");
        probe.Should().NotContain("ToggleFieldCodes").And.NotContain("UpdateFields()");
        fixture.Should().Contain("DocxWriter.Write").And.Contain("DocxReader.Read");
        fixture.Should().Contain("Run.ComplexFieldRun(\" TITLE \", staleTitle)");
    }

    [Fact]
    public void FreeWFieldShortcutSchemaIsStrictAndExplicitlyNonExhaustive()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "field-shortcut-validation.schema.json")));
        var root = document.RootElement;
        root.GetProperty("properties").GetProperty("suite").GetProperty("const").GetString()
            .Should().Be("freew-linux-field-shortcut-physical");
        root.GetProperty("properties").GetProperty("coverage").GetProperty("properties")
            .GetProperty("exhaustive").GetProperty("const").GetBoolean().Should().BeFalse();
        root.GetProperty("properties").GetProperty("results").GetProperty("minItems").GetInt32().Should().Be(4);
        root.GetProperty("properties").GetProperty("results").GetProperty("maxItems").GetInt32().Should().Be(4);
        root.GetProperty("properties").GetProperty("results").GetProperty("items")
            .GetProperty("properties").GetProperty("category").GetProperty("const").GetString()
            .Should().Be("physical-x11-field-shortcut");
        root.GetProperty("properties").GetProperty("window").GetProperty("properties")
            .GetProperty("pattern").GetProperty("const").GetString()
            .Should().Be("field-shortcut-fixture.docx");
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

    [Fact]
    public void ManifestEvidenceContractHelpers_ValidateAndCompleteTheGenericContract()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var powershell = ResolvePowerShellExecutable();
        powershell.Should().NotBeNull("the manifest helper regression requires PowerShell");

        using var temporary = new TestTemporaryDirectory();
        var schemaPath = Path.Combine(temporary.Path, "schema.json");
        var evidencePath = Path.Combine(temporary.Path, "proof.txt");
        var manifestPath = Path.Combine(temporary.Path, "manifest.json");
        File.WriteAllText(schemaPath, "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\"}", Encoding.UTF8);
        File.WriteAllText(evidencePath, "proof", Encoding.UTF8);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            suite = "synthetic-validation",
            platform = "linux",
            shell = "avalonia",
            app = "FreeX",
            baseline = false,
            contractValidation = new { status = "pending" },
            summary = new { passed = 1, failed = 0, total = 1 },
            results = new[]
            {
                new
                {
                    id = "ok",
                    category = "physical-x11-synthetic",
                    status = "passed",
                    evidenceLevel = "physical-x11-input",
                    evidence = new[] { "proof.txt" },
                    note = "Synthetic proof"
                }
            },
            screenshots = Array.Empty<object>()
        }), Encoding.UTF8);

        var helperPath = RepositoryFileLocator.Find("tools", "LinuxInteractiveDocker", "ManifestEvidence.ps1");
        var command =
            $". '{EscapePowerShell(helperPath)}'; " +
            $"$manifest = Read-ManifestContract -ManifestPath '{EscapePowerShell(manifestPath)}' -SchemaPath '{EscapePowerShell(schemaPath)}'; " +
            "$results = @($manifest.results); " +
            "Assert-ManifestContractPending -Manifest $manifest; " +
            "Assert-ManifestIdentity -Manifest $manifest -Expected ([ordered]@{ schemaVersion = 1; suite = 'synthetic-validation'; platform = 'linux'; shell = 'avalonia'; app = 'FreeX'; baseline = $false }); " +
            "Assert-ManifestResultIds -Results $results -ExpectedIds @('ok'); " +
            "Assert-ManifestResultSummary -Manifest $manifest -Results $results -ExpectedTotal 1 -RequireCompleteStatuses; " +
            $"$map = Get-ManifestEvidenceFileMap -EvidenceDirectory '{EscapePowerShell(temporary.Path)}'; " +
            "Assert-ManifestResultEvidence -Results $results -FileMap $map -Category 'physical-x11-synthetic' -EvidenceLevel 'physical-x11-input' -ValidStatuses @('passed') -RequireNote; " +
            "Assert-ManifestScreenshotEvidence -Screenshots @($manifest.screenshots) -FileMap $map -ExpectedCount 0 -RequireKind; " +
            "$rejected = $false; try { Assert-ManifestEvidenceReference -FileMap $map -Name '../proof.txt' -Owner 'Result bad' } catch { $rejected = $true }; " +
            "if (-not $rejected) { throw 'non-basename evidence was accepted' }; " +
            $"Complete-ManifestContract -Manifest $manifest -ManifestPath '{EscapePowerShell(manifestPath)}' -Validator 'test-validator' -ContractReference 'schema.json' | Out-Null; " +
            "'validated'";

        var result = RunPowerShellCommand(powershell!, command);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("validated");
        using var completed = JsonDocument.Parse(File.ReadAllText(manifestPath));
        completed.RootElement.GetProperty("contractValidation").GetProperty("status").GetString()
            .Should().Be("passed");
    }

    [Fact]
    public void ToolTemporaryDirectoryHelpers_CreateAndGuardOwnedDirectories()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var powershell = ResolvePowerShellExecutable();
        powershell.Should().NotBeNull("the temporary-directory regression requires PowerShell");
        var helperPath = RepositoryFileLocator.Find("tools", "ToolScriptSupport.ps1");
        var repositoryRoot = Path.GetDirectoryName(Path.GetDirectoryName(helperPath))!;
        var command =
            $". '{EscapePowerShell(helperPath)}'; " +
            "$errors = [Collections.Generic.List[string]]::new(); $env:GITHUB_ACTIONS = 'true'; " +
            "Add-ToolValidationError -Errors $errors -Message \"percent%`r`nline\" -GitHubTitle 'Synthetic readiness' -SuppressWriteError; " +
            "if ($errors.Count -ne 1) { throw 'validation error was not collected' }; " +
            "$path = New-ToolTemporaryDirectory -Prefix 'freex-tool-test-'; " +
            "if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw 'temporary directory was not created' }; " +
            "Set-Content -LiteralPath (Join-Path $path 'owned.txt') -Value 'proof'; " +
            "Remove-ToolTemporaryDirectory -Path $path; " +
            "if (Test-Path -LiteralPath $path) { throw 'temporary directory was not removed' }; " +
            $"$rejected = $false; try {{ Remove-ToolTemporaryDirectory -Path '{EscapePowerShell(repositoryRoot)}' }} catch {{ $rejected = $true }}; " +
            "if (-not $rejected) { throw 'non-temporary directory was accepted' }; 'validated'";

        var result = RunPowerShellCommand(powershell!, command);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("validated");
        result.Output.Should().Contain("::error title=Synthetic readiness::percent%25%0D%0Aline");
    }

    [Fact]
    public void ProbeScriptSupport_OwnsSharedRotatedAndTransformedX11Primitives()
    {
        var support = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "ProbeScriptSupport.sh"));
        foreach (var helper in new[]
        {
            "probe_capture()",
            "probe_focus_owner()",
            "probe_send_owner_key()",
            "probe_capture_window_state()"
        })
        {
            support.Should().Contain(helper);
        }

        foreach (var probeName in new[]
        {
            "run-freep-rotated-shape-text-edit.sh",
            "run-freep-transformed-table-cell-edit.sh"
        })
        {
            var probe = File.ReadAllText(RepositoryFileLocator.Find(
                "tools", "LinuxInteractiveDocker", probeName));
            probe.Should().Contain("ProbeScriptSupport.sh")
                .And.NotContain("capture()")
                .And.NotContain("focus_owner()")
                .And.NotContain("send_owner_key()")
                .And.NotContain("capture_window_state()");
        }
    }

    [Fact]
    public void PhysicalValidationSupport_OwnsFixtureInvocationAndKeyValueParsing()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var supportPath = RepositoryFileLocator.Find("tools", "PhysicalValidationScriptSupport.ps1");
        var support = File.ReadAllText(supportPath);
        support.Should().Contain("function Invoke-PhysicalValidationFixture")
            .And.Contain("function ConvertFrom-PhysicalValidationKeyValueLines")
            .And.Contain("function Read-PhysicalValidationFixtureValues")
            .And.Contain("--configuration Release --no-restore");

        foreach (var runnerName in new[]
        {
            "Run-FreeWWave61GroupedChildValidation.ps1",
            "Run-FreeWWave62NestedGroupChildValidation.ps1",
            "Run-FreeWWave63NestedEditPointsValidation.ps1",
            "Run-FreeWWave64NestedTextValidation.ps1"
        })
        {
            var runner = File.ReadAllText(RepositoryFileLocator.Find("tools", runnerName));
            runner.Should().Contain("PhysicalValidationScriptSupport.ps1")
                .And.NotContain("function Invoke-Fixture")
                .And.NotContain("function Read-Geometry");
        }

        var powershell = ResolvePowerShellExecutable();
        powershell.Should().NotBeNull("the physical validation helper regression requires PowerShell");
        var command =
            $". '{EscapePowerShell(supportPath)}'; " +
            "$values = ConvertFrom-PhysicalValidationKeyValueLines -Lines @('alpha=one=two', 'ignored', 'beta=3'); " +
            "\"$($values['alpha'])|$($values['beta'])\"";
        var result = RunPowerShellCommand(powershell!, command);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("one=two|3");
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
                // Generous: this only asks whether PowerShell starts at all. Five seconds was
                // enough alone (the probe takes well under a second) but not in a loaded full-suite
                // run, where the timeout expired and the test concluded PowerShell was missing on a
                // machine that plainly has it.
                if (process is not null && process.WaitForExit(60_000) && process.HasExited)
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
