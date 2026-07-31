using Avalonia.Headless;
using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaInteractionCoverageTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void Options_ParseAndRemoveInteractionValidationArguments()
    {
        var parsed = InteractionValidationOptions.TryParse(
            ["--interaction-validation", "/work/validation", "book.xlsx"],
            out var options,
            out var startupArguments,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal("/work/validation", options!.OutputDirectory);
        Assert.Equal(["book.xlsx"], startupArguments);
    }

    [Fact]
    public void Options_RejectMissingOutputDirectory()
    {
        Assert.False(InteractionValidationOptions.TryParse(
            ["--interaction-validation"],
            out _,
            out _,
            out var error));

        Assert.Contains("requires an output directory", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_ParseBoundedDialogBatch()
    {
        var parsed = InteractionValidationOptions.TryParse(
            [
                "--interaction-validation", "/work/validation",
                "--interaction-validation-dialog-start", "20",
                "--interaction-validation-dialog-count", "10",
                "--interaction-validation-dialog-only",
                "book.xlsx",
            ],
            out var options,
            out var startupArguments,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal(20, options!.DialogStart);
        Assert.Equal(10, options.DialogCount);
        Assert.False(options.IncludeCoreResults);
        Assert.Equal(["book.xlsx"], startupArguments);
    }

    [Fact]
    public void RibbonSurfaceInventory_PreservesEveryDeclaredPlacement()
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var rows = AvaloniaRibbonComposition.EnumerateSurfaceRows(definition).ToArray();

        // 589 canonical shared placements plus the 42 runtime shape-gallery leaves.
        Assert.Equal(631, rows.Length);
        Assert.Equal(309, rows.Count(row => row.Kind != nameof(RibbonMenuItem)));
        Assert.Equal(322, rows.Count(row => row.Kind == nameof(RibbonMenuItem)));
        Assert.Equal(588, rows.Select(row => row.CommandId).Distinct().Count());
        Assert.Equal(74, definition.Tabs.Sum(tab => tab.Groups.Count));
    }

    [Fact]
    public void LinuxRunner_DiscoversDialogCountAndDefaultsToOneDialogPerProcess()
    {
        var script = File.ReadAllText(RepoFile("tools", "Run-FreeXLinuxInteractionValidation.ps1"));

        Assert.DoesNotContain("$authoritativeDialogCount = 120", script, StringComparison.Ordinal);
        Assert.Contains("$batchManifest.dialogCatalogCount", script, StringComparison.Ordinal);
        Assert.Contains("[int]$DialogBatchSize = 1", script, StringComparison.Ordinal);
        Assert.Contains("$existingContextPath", script, StringComparison.Ordinal);
        Assert.Contains("$existingDialogPath", script, StringComparison.Ordinal);
        Assert.Contains("$existingRibbonPath", script, StringComparison.Ordinal);
        Assert.Contains("Reusing dialog interaction batch", script, StringComparison.Ordinal);
        Assert.Contains("Reusing ribbon interaction batch", script, StringComparison.Ordinal);
        Assert.Contains("\"cell-inline-formula-point-range-drag\"", script, StringComparison.Ordinal);
        Assert.Contains("quick-analysis-drawing", script, StringComparison.Ordinal);
        Assert.Contains("drawing.shape.capture-loss-no-op", script, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxRunner_ValidatesResumeProvenanceAndBatchIdentity()
    {
        var script = File.ReadAllText(RepoFile("tools", "Run-FreeXLinuxInteractionValidation.ps1"));

        Assert.Contains("$runnerSchemaVersion = 2", script, StringComparison.Ordinal);
        Assert.Contains("Ensure-ReportProvenance", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ProvenanceMatchesCurrent", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ManifestCatalog", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ManifestIdentity", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ManifestResultShape", script, StringComparison.Ordinal);
        Assert.Contains("validationSelectionIds", script, StringComparison.Ordinal);
        Assert.Contains("runnerBatchIdentity", script, StringComparison.Ordinal);
        Assert.Contains("Save-ValidatedManifest", script, StringComparison.Ordinal);
        Assert.Contains("Merge-ContextMenuAggregateResults", script, StringComparison.Ordinal);
        Assert.Contains("bounded-batch-aggregate-incomplete", script, StringComparison.Ordinal);
        Assert.Contains("payloadFingerprint", script, StringComparison.Ordinal);
        Assert.Contains("appImageId", script, StringComparison.Ordinal);
        Assert.Contains("[IO.Path]::DirectorySeparatorChar", script, StringComparison.Ordinal);
        Assert.DoesNotContain(".Replace('', '/')", script, StringComparison.Ordinal);
        Assert.Contains("$fingerprintHasher.ComputeHash($bytes)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[Security.Cryptography.SHA256]::HashData", script, StringComparison.Ordinal);
        Assert.Contains("[BitConverter]::ToString($digest)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[Convert]::ToHexString", script, StringComparison.Ordinal);
        Assert.Contains("git -C $repoRoot rev-parse --verify HEAD", script, StringComparison.Ordinal);
        Assert.DoesNotContain("rev-parse HEAD 2>$null | Select-Object -First 1", script, StringComparison.Ordinal);
        Assert.Contains("([int]::MaxValue)", script, StringComparison.Ordinal);
        Assert.DoesNotContain(" [int]::MaxValue", script, StringComparison.Ordinal);
        Assert.Contains("[bool]$RequireRunnerMetadata = $false", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[Parameter(Mandatory = $true)][bool]$RequireRunnerMetadata = $false",
            script,
            StringComparison.Ordinal);
        Assert.Contains("$value = $property.Value", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "$value = Get-ManifestProperty -Manifest $Manifest -Name $Name",
            script,
            StringComparison.Ordinal);
        Assert.Contains("$selected -gt 0 -and $batchStatus -eq \"failed\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "0 ([int]::MaxValue) 0 0 $contextStart $contextCount",
            script,
            StringComparison.Ordinal);
        Assert.Contains("ConvertTo-ManifestEscapedId", script, StringComparison.Ordinal);
        Assert.Contains("Replace(\"(\", \"%28\")", script, StringComparison.Ordinal);
        Assert.Contains("Replace(\")\", \"%29\")", script, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxPhysicalProbe_IsGeometryCalibratedClipboardBackedAndSchemaVersioned()
    {
        var probe = File.ReadAllText(RepoFile("tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));
        var runner = File.ReadAllText(RepoFile("tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var readme = File.ReadAllText(RepoFile("tools", "LinuxInteractiveDocker", "README.md"));

        Assert.Contains("schemaVersion\":2", probe, StringComparison.Ordinal);
        Assert.Contains("calibrate_geometry()", probe, StringComparison.Ordinal);
        Assert.Contains("selection_box()", probe, StringComparison.Ordinal);
        Assert.Contains("cellWidth", probe, StringComparison.Ordinal);
        Assert.Contains("set_clipboard_sentinel", probe, StringComparison.Ordinal);
        Assert.Contains("xclip -selection clipboard -out", probe, StringComparison.Ordinal);
        Assert.Contains("X11 clipboard formula='=B2'", probe, StringComparison.Ordinal);

        string[] requiredPhysicalRows =
        [
            "inline-edit-f2-escape",
            "inline-edit-f2-enter-commit",
            "save-ctrl-s-persist",
            "save-shift-f12-persist",
            "inline-point-mode-click",
            "inline-point-mode-drag-range",
            "formula-bar-point-mode-click",
            "keytips-alt",
            "keytips-f10",
            "worksheet-context-shift-f10",
            "worksheet-context-right-click",
            "worksheet-context-copy-physical",
            "worksheet-context-clear-physical",
            "clipboard-copy-paste-roundtrip",
            "clipboard-cut-paste-roundtrip",
            "window-new-arrange-switch-physical",
            "dialog-format-cells-keyboard",
            "native-save-as-f12-cancel",
            "native-open-ctrl-f12-cancel",
            "backstage-print-ctrl-shift-f12-cancel",
            "sheet-tab-overflow-create-physical",
            "sheet-tab-overflow-navigation-physical",
            "sheet-tab-overflow-activate-dialog-physical",
            "sheet-tab-drag-reorder-physical",
        ];
        foreach (var id in requiredPhysicalRows)
        {
            Assert.Contains($"\"{id}\"", probe, StringComparison.Ordinal);
            Assert.Contains($"\"{id}\"", runner, StringComparison.Ordinal);
        }

        Assert.Contains("$physicalSchemaValid", runner, StringComparison.Ordinal);
        Assert.Contains("$missingPhysicalArtifactIds", runner, StringComparison.Ordinal);
        Assert.Contains("$invalidPhysicalArtifactRows", runner, StringComparison.Ordinal);
        Assert.Contains("\\\"artifacts\\\":$(artifact_json", probe, StringComparison.Ordinal);
        Assert.Contains("window_bounds_signature", probe, StringComparison.Ordinal);
        Assert.Contains("^.+ - FreeX$", probe, StringComparison.Ordinal);
        Assert.Contains("shared-workbook-parity=managed-behavior-tested", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("send_key Enter", probe, StringComparison.Ordinal);
        Assert.Contains("send_key Return", probe, StringComparison.Ordinal);
        Assert.Contains("local before=\"\" after=\"\" dialog_id=\"\"", probe, StringComparison.Ordinal);
        Assert.Contains("wait_for_document_clean()", probe, StringComparison.Ordinal);
        Assert.Contains("seed_cell_text()", probe, StringComparison.Ordinal);
        Assert.Contains("seed_cell_text 6 14 G15 \"$value\"", probe, StringComparison.Ordinal);
        Assert.Contains("seed_cell_text 6 15 G16 \"$copy_value\"", probe, StringComparison.Ordinal);
        Assert.Contains("seed_cell_text 6 16 G17 \"$cut_value\"", probe, StringComparison.Ordinal);
        Assert.Contains("xdotool mousemove --window \"$window_id\" 520 420 click 1", probe, StringComparison.Ordinal);
        Assert.Contains("alt_changed=false", probe, StringComparison.Ordinal);
        Assert.Contains("context_keyboard_changed=false", probe, StringComparison.Ordinal);
        Assert.Contains("for _ in $(seq 1 3); do", probe, StringComparison.Ordinal);
        Assert.Contains("send_active_key Home Return", probe, StringComparison.Ordinal);
        Assert.Contains("Physical X11 manifest does not satisfy schema v2", runner, StringComparison.Ordinal);
        Assert.Contains("geometry calibration did not pass", runner, StringComparison.Ordinal);
        Assert.Contains("xdotool getactivewindow", probe, StringComparison.Ordinal);
        Assert.Contains(
            "xdotool key --clearmodifiers --delay \"$input_delay_ms\" Escape",
            probe,
            StringComparison.Ordinal);
        Assert.DoesNotContain("--window \"$dialog_id\" Escape", probe, StringComparison.Ordinal);
        Assert.Contains("FREEX_X11_DIALOG_SETTLE_SECONDS", probe, StringComparison.Ordinal);
        Assert.Contains("FREEX_X11_PROBE_SELECTOR", probe, StringComparison.Ordinal);
        Assert.Contains("probe_selector\" == \"sheet-tabs\"", probe, StringComparison.Ordinal);
        Assert.Contains("probe_selector\" == \"name-box-dropdown-parity\"", probe, StringComparison.Ordinal);
        Assert.Contains("name-box-dropdown-parity-native-crop", probe, StringComparison.Ordinal);
        Assert.Contains("Assert-NameBoxDropdownParityNativeContract", runner, StringComparison.Ordinal);
        Assert.Contains("probe_selector\" == \"formula-3d-point\"", probe, StringComparison.Ordinal);
        Assert.Contains("probe_selector\" == \"formula-3d-grip\"", probe, StringComparison.Ordinal);
        Assert.Contains("probe_selector\" == \"formula-3d-native-xlsx\"", probe, StringComparison.Ordinal);
        Assert.Contains("formula-bar-point-mode-3d-sheet-range", probe, StringComparison.Ordinal);
        Assert.Contains("formula-bar-point-mode-3d-sheet-range-grip", probe, StringComparison.Ordinal);
        Assert.Contains("formula-bar-point-mode-3d-sheet-range-grip", runner, StringComparison.Ordinal);
        Assert.Contains("formula-bar-point-mode-3d-native-xlsx", probe, StringComparison.Ordinal);
        Assert.Contains("formula-bar-point-mode-3d-native-xlsx", runner, StringComparison.Ordinal);
        Assert.Contains("New-FreeXWave66Native3DFixture.ps1", runner, StringComparison.Ordinal);
        Assert.Contains("freex-native-3d-formula-validation.schema.json", readme, StringComparison.Ordinal);
        Assert.Contains("normalize_formula", probe, StringComparison.Ordinal);
        Assert.Contains("SHEET2:SHEET3!B2", probe, StringComparison.Ordinal);
        Assert.Contains("select_sheet_tab_range_end", probe, StringComparison.Ordinal);
        Assert.Contains("xdotool_mousemove_sync", probe, StringComparison.Ordinal);
        Assert.Contains("probe_sheet_tabs", probe, StringComparison.Ordinal);
        Assert.Contains("sheet-tab-overflow-create-physical", probe, StringComparison.Ordinal);
        Assert.Contains("sheet-tab-overflow-navigation-physical", probe, StringComparison.Ordinal);
        Assert.Contains("sheet-tab-overflow-activate-dialog-physical", probe, StringComparison.Ordinal);
        Assert.Contains("sheet-tab-drag-reorder-physical", probe, StringComparison.Ordinal);
        Assert.Contains("set_cell_text_without_save", probe, StringComparison.Ordinal);
        Assert.Contains("Sheet2Anchor", probe, StringComparison.Ordinal);
        Assert.Contains("Sheet3Anchor", probe, StringComparison.Ordinal);
        Assert.Contains("harmless readiness sentinel", probe, StringComparison.Ordinal);
        Assert.True(
            probe.IndexOf("probe_backstage_print_shortcut", StringComparison.Ordinal) <
            probe.IndexOf("probe_cancelable_window \"native-save-as-f12-cancel\"", StringComparison.Ordinal));
        Assert.Contains("Physical X11 manifest", readme, StringComparison.Ordinal);
        Assert.Contains("unique `x11-input` rows", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxPhysicalProbe_WholeRangeFormulaPoint_HasClosedSemanticContract()
    {
        var probe = File.ReadAllText(RepoFile("tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));
        var runner = File.ReadAllText(RepoFile("tools", "Run-FreeXLinuxInteractionValidation.ps1"));

        Assert.Contains("formula-whole-range-point", runner, StringComparison.Ordinal);
        Assert.Contains("probe_formula_bar_point_mode_whole_range", probe, StringComparison.Ordinal);
        Assert.Contains("probe_selector\" == \"formula-whole-range-point\"", probe, StringComparison.Ordinal);
        Assert.Contains("Assert-FormulaWholeRangePointPostcondition", runner, StringComparison.Ordinal);
        Assert.Contains("read_active_formula_bar", probe, StringComparison.Ordinal);
        Assert.Contains("copy_cell_formula_allow_empty", probe, StringComparison.Ordinal);
        Assert.Contains("__FREEX_NO_FORMULA__", probe, StringComparison.Ordinal);
        Assert.Contains("row_header_x=\"$((window_x + (a1_x - window_x) / 2))\"", probe, StringComparison.Ordinal);
        Assert.Contains("Enter reaches the formula commit path instead of accepting BAHTTEXT", probe, StringComparison.Ordinal);
        Assert.True(
            probe.Split("type_text \"=SUM()\"", StringSplitOptions.None).Length - 1 >= 3,
            "each whole-range case must preserve a closing parenthesis around the pointed reference");
        Assert.True(
            probe.Split("send_key Left", StringSplitOptions.None).Length - 1 >= 3,
            "each whole-range case must place the caret before the closing parenthesis");
        Assert.Contains("xdotool_mousemove_sync \"$column_header_x\" \"$column_header_y\" click 1", probe, StringComparison.Ordinal);
        Assert.Contains("xdotool_mousemove_sync \"$row_header_x\" \"$row_header_y\" click 1", probe, StringComparison.Ordinal);
        Assert.Contains("xdotool_mousemove_sync \"$corner_x\" \"$corner_y\" click 1", probe, StringComparison.Ordinal);
        Assert.Contains("xdotool_mousemove_sync \"$formula_cancel_x\" \"$formula_cancel_y\" click 1", probe, StringComparison.Ordinal);
        Assert.Contains("column-header-formula-bar-clipboard=$column_formula_bar", probe, StringComparison.Ordinal);
        Assert.Contains("row-header-formula-bar-clipboard=$row_formula_bar", probe, StringComparison.Ordinal);
        Assert.Contains("select-all-formula-bar-clipboard=$select_all_formula_bar", probe, StringComparison.Ordinal);
        Assert.Contains("column-header-cell-package-formula=$column_cell_formula", probe, StringComparison.Ordinal);
        Assert.Contains("row-header-cell-package-formula=$row_cell_formula", probe, StringComparison.Ordinal);
        Assert.Contains("select-all-cell-package-formula-after-cancel=$select_all_cell_formula", probe, StringComparison.Ordinal);

        string[] requiredProbeIds =
        [
            "formula-bar-point-mode-whole-column-header",
            "formula-bar-point-mode-whole-row-header",
            "formula-bar-point-mode-whole-select-all-corner"
        ];
        foreach (var id in requiredProbeIds)
        {
            Assert.Contains($"\"{id}\"", probe, StringComparison.Ordinal);
            Assert.Contains($"\"{id}\"", runner, StringComparison.Ordinal);
            Assert.True(
                runner.Split($"\"{id}\"", StringSplitOptions.None).Length - 1 >= 2,
                $"Runner must require '{id}' in both physical probe contract lists.");
        }

        string[] requiredArtifacts =
        [
            "formula-whole-range-column-before.png",
            "formula-whole-range-column-editing.png",
            "formula-whole-range-column-committed.png",
            "formula-whole-range-row-before.png",
            "formula-whole-range-row-editing.png",
            "formula-whole-range-row-committed.png",
            "formula-whole-range-select-all-before.png",
            "formula-whole-range-select-all-editing.png",
            "formula-whole-range-select-all-canceled.png",
            "formula-whole-range-point-postcondition.txt"
        ];
        foreach (var artifact in requiredArtifacts)
        {
            Assert.Contains(artifact, probe, StringComparison.Ordinal);
        }

        Assert.Contains("formula-whole-range-point-postcondition.txt", runner, StringComparison.Ordinal);
        Assert.Contains("column-header-expected=B:B", runner, StringComparison.Ordinal);
        Assert.Contains("row-header-expected=3:3", runner, StringComparison.Ordinal);
        Assert.Contains("select-all-expected=A1:XFD1048576", runner, StringComparison.Ordinal);
        Assert.Contains("select-all-formula-bar-clipboard==SUM(A1:XFD1048576)", runner, StringComparison.Ordinal);
        Assert.Contains("select-all-edit-active-before-cancel=true", runner, StringComparison.Ordinal);
        Assert.Contains("select-all-cell-package-formula-after-cancel=", runner, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveWindow_AllRibbonCommandsAreFunctionalOrExplicitlyDisabled()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var registry = Assert.IsAssignableFrom<IRibbonCommandRegistry>(window.RibbonCommandRegistryForTest);
            var unresolved = AvaloniaRibbonComposition
                .EnumerateCommandIds(AvaloniaRibbonComposition.BuildDefinition())
                .Distinct()
                .Where(id => !registry.TryGet(id, out var command) || command is EmptyRibbonCommand)
                .Select(id => id.Value)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            window.Close();

            Assert.True(
                unresolved.Length == 0,
                "Live Avalonia ribbon commands still bound to EmptyRibbonCommand: " +
                string.Join(", ", unresolved));
        }, CancellationToken.None);
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;
        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");
        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
