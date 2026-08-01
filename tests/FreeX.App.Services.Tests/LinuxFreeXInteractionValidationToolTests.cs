using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class LinuxFreeXInteractionValidationToolTests
{
    [Fact]
    public void RunnerExposesPhysicalOnlyModeAndKeepsTheDefaultManagedLane()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));

        script.Should().Contain("[switch]$PhysicalOnly");
        script.Should().Contain("validationMode = \"physical-only\"");
        script.Should().Contain("scope = \"bounded physical X11 probes\"");
        script.Should().Contain("summary = [pscustomobject]@{}");
        script.Should().Contain("-PhysicalOnly cannot be combined with -SkipX11");
        script.Should().Contain("Phase two uses a fresh X11 process for each bounded dialog slice");
        script.Should().Contain("--interaction-validation");
    }

    [Fact]
    public void PhysicalOnlyBranchDoesNotDispatchManagedInteractionValidation()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var physicalOnlyStart = script.IndexOf("if ($PhysicalOnly)", StringComparison.Ordinal);
        var managedLaneStart = script.IndexOf("} else {", physicalOnlyStart, StringComparison.Ordinal);

        physicalOnlyStart.Should().BeGreaterThanOrEqualTo(0);
        managedLaneStart.Should().BeGreaterThan(physicalOnlyStart);
        script[physicalOnlyStart..managedLaneStart]
            .Should().NotContain("--interaction-validation");
    }

    [Fact]
    public void PhysicalOnlyModeStillRunsTheBoundedX11ProbeScript()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));

        script.Should().Contain("run-freex-input-probes.sh");
        script.Should().Contain("\"DISPLAY=:99\"");
        script.Should().Contain("\"FREEX_X11_PROBE_SELECTOR=$PhysicalProbeSelector\"");
        script.Should().Contain("& docker exec @($probeEnvironment | ForEach-Object { \"--env\"; $_ })");
        script.Should().Contain("Physical X11 manifest does not satisfy schema v2");
        script.Should().Contain("x11-validation/x11-input-results.json");
    }

    [Fact]
    public void NameBoxObjectValidationUsesFixedIdentityContractsAndNeutralBaselines()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));

        script.Should().Contain("$expectedOrder = @(");
        script.Should().Contain("$expectedContracts = [ordered]@{");
        script.Should().Contain("expectedName = \"PhysicalChart\"");
        script.Should().Contain("expectedName = \"PhysicalPicture\"");
        script.Should().Contain("expectedName = \"PhysicalShape\"");
        script.Should().Contain("expectedName = \"PhysicalTextBox\"");
        script.Should().Contain("observedSelectedObjectKind");
        script.Should().Contain("baselineStage -ne \"neutral-cell-selected\"");
        script.Should().Contain("baselineNameBox -ne \"J20\"");
        script.Should().Contain("expectedOrder='$(@($actualOrder) -join ',')'");
    }

    [Fact]
    public void NameBoxObjectPostconditionClosesItsPythonHeredocBeforeShellHelpers()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find(
                "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        script.Should().Contain(
            "    stream.write(\"\\n\")\nPY\n}\n\nread_name_box_event() {");
    }

    [Fact]
    public void NameBoxInteractionLaneRequiresNativeKeyboardAndPointerCommitEvidence()
    {
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));

        runner.Should().Contain("Assert-NameBoxDropdownInteractionPostcondition");
        runner.Should().Contain("name-box-dropdown-keyboard-physical");
        runner.Should().Contain("name-box-dropdown-mouse-physical");
        probe.Should().Contain("send_key alt+Down");
        probe.Should().Contain("keyboard-gesture=Alt+Down,Home,Down,Down,Down,Down,Enter");
        probe.Should().Contain("mouse-gesture=NameBoxChevron,PhysicalTableRow");
        probe.Should().Contain("name-box-dropdown-interaction-postcondition.txt");
    }

    [Fact]
    public void NameBoxObjectEventsPreserveEmptyFieldsAndCaptureTheSettledPointerSelection()
    {
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));
        var window = File.ReadAllText(RepositoryFileLocator.Find(
                "src", "FreeX.App.Avalonia", "MainWindow.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        probe.Should().Contain("print(\"\\x1f\".join(value(key)");
        probe.Should().Contain("IFS=$'\\x1f' read -r baseline_sequence");
        probe.Should().Contain("IFS=$'\\x1f' read -r observed_sequence");

        var releaseStart = window.IndexOf(
            "private async Task EndCellSelectionDragAsync",
            StringComparison.Ordinal);
        var releaseEnd = window.IndexOf(
            "private bool TryResolveCellPointerAddress",
            releaseStart,
            StringComparison.Ordinal);
        releaseStart.Should().BeGreaterThanOrEqualTo(0);
        releaseEnd.Should().BeGreaterThan(releaseStart);
        window[releaseStart..releaseEnd].Should().Contain(
            "RevertNameBoxAfterCellSelectionDragEnd();\n" +
            "        RecordNameBoxDropdownPhysicalEvidence(item: null, stage: \"neutral-cell-selected\");");

        var selectCellStart = window.IndexOf(
            "private void SelectCell(CellAddress address)",
            StringComparison.Ordinal);
        var selectCellEnd = window.IndexOf(
            "private void SelectRange(CellAddress address)",
            selectCellStart,
            StringComparison.Ordinal);
        window[selectCellStart..selectCellEnd].Should().NotContain(
            "RecordNameBoxDropdownPhysicalEvidence");
    }

    [Fact]
    public void NameBoxParityPhysicalLaneRequiresLiveX11CropProvenance()
    {
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));

        runner.Should().Contain("\"name-box-dropdown-parity\"");
        runner.Should().Contain("--freex-name-box-dropdown-parity-physical");
        runner.Should().Contain("Assert-NameBoxDropdownParityNativeContract");
        runner.Should().Contain("native-x11-root-crop");
        runner.Should().Contain("Name Box parity native crop pixels must be 208x136");
        runner.Should().Contain("name-box-dropdown-parity-native");

        probe.Should().Contain("probe_name_box_dropdown_parity()");
        probe.Should().Contain("name-box-dropdown-parity-before-x11.txt");
        probe.Should().Contain("name-box-dropdown-parity-open-x11.txt");
        probe.Should().Contain("xdotool mousemove --window \"$window_id\"");
        probe.Should().Contain("local home_ready=false");
        probe.Should().Contain("for _ in $(seq 1 20)");
        probe.Should().Contain("if ! $home_ready");
        probe.Should().Contain("if len(candidates) == 1");
        probe.Should().Contain("-crop \"208x136+${popup_x}+${popup_y}\" +repage");
        probe.Should().Contain("\"evidenceProvenance\": \"native-x11-root-crop\"");
        probe.Should().Contain("\"resized\": False");
        probe.Should().NotContain("-resize 208x136");
    }

    [Fact]
    public void OutlineGroupPhysicalLaneRequiresRealSelectionRibbonAndGutterRestorationEvidence()
    {
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));

        runner.Should().Contain("\"outline-group\"");
        runner.Should().Contain("outline-group-physical");
        probe.Should().Contain("probe_outline_group_physical()");
        probe.Should().Contain("send_key shift+space");
        probe.Should().Contain("send_key alt+a");
        probe.Should().Contain("outline_green_score");
        probe.Should().Contain("outline-collapsed.png");
        probe.Should().Contain("outline-expanded.png");
        probe.Should().Contain("values-restored=$values_restored");
        probe.Should().Contain("xdotool_mousemove_sync \"$toggle_x\" \"$toggle_y\" click 1");
    }
}
