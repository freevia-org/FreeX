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

        // 574 canonical shared placements plus the 42 runtime shape-gallery leaves.
        Assert.Equal(616, rows.Length);
        Assert.Equal(294, rows.Count(row => row.Kind != nameof(RibbonMenuItem)));
        Assert.Equal(322, rows.Count(row => row.Kind == nameof(RibbonMenuItem)));
        Assert.Equal(573, rows.Select(row => row.CommandId).Distinct().Count());
        Assert.Equal(73, definition.Tabs.Sum(tab => tab.Groups.Count));
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
        Assert.Contains("xclip -selection clipboard -in >/dev/null 2>&1", probe, StringComparison.Ordinal);
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
            "print-preview-ctrl-shift-f12-cancel",
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
        Assert.Contains("wait_for_csv_cell 6 11 \"$value\"", probe, StringComparison.Ordinal);
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
        Assert.Contains("harmless readiness sentinel", probe, StringComparison.Ordinal);
        Assert.True(
            probe.IndexOf("probe_cancelable_window \"print-preview-ctrl-shift-f12-cancel\"", StringComparison.Ordinal) <
            probe.IndexOf("probe_cancelable_window \"native-save-as-f12-cancel\"", StringComparison.Ordinal));
        Assert.Contains("Physical X11 manifest", readme, StringComparison.Ordinal);
        Assert.Contains("unique `x11-input` rows", readme, StringComparison.Ordinal);
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
