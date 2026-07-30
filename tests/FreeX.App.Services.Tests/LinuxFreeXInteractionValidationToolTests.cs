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
}
