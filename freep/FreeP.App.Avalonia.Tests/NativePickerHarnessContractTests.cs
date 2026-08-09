using System.Text.Json;

namespace FreeP.App.Avalonia.Tests;

public sealed class NativePickerHarnessContractTests
{
    [Fact]
    public void Native_picker_schema_pins_the_ordered_physical_contract()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoFile("tools/LinuxInteractiveDocker/freep-native-picker-x11-wave90-validation.schema.json")));
        var root = document.RootElement;

        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        root.GetProperty("properties").GetProperty("suite").GetProperty("const").GetString()
            .Should().Be("freep-native-picker-x11-wave90-physical");
        root.GetProperty("properties").GetProperty("appSurface").GetProperty("const").GetString()
            .Should().Be("native-storage-provider-open-save-as");
        root.GetProperty("properties").GetProperty("summary").GetProperty("properties")
            .GetProperty("total").GetProperty("const").GetInt32().Should().Be(9);

        var requiredIds = new[]
        {
            "visible-window-discovery",
            "open-cancel-preserves-document",
            "open-pptx-selection-loads-package",
            "save-as-pptx-filter-selection-writes-package",
            "save-as-overwrite-cancel-preserves-collision",
            "save-as-unwritable-bounded-error",
            "escape-cancel-open-no-modal-blocker",
            "escape-cancel-save-no-modal-blocker",
            "focus-return-after-cancel-and-error",
        };
        root.GetProperty("$defs").GetProperty("packageState").GetProperty("required")
            .EnumerateArray().Select(value => value.GetString()).Should().Contain("sha256");
        root.GetProperty("properties").GetProperty("results").GetProperty("maxItems").GetInt32()
            .Should().Be(requiredIds.Length);
    }

    [Fact]
    public void Native_picker_probe_is_physical_and_keeps_package_postconditions()
    {
        var probe = File.ReadAllText(RepoFile(
            "tools/LinuxInteractiveDocker/run-freep-native-picker-x11-wave90-probe.sh"));

        probe.Should().Contain("xdotool key");
        probe.Should().Contain("ctrl+o");
        probe.Should().Contain("ctrl+shift+s");
        probe.Should().Contain("xdotool mousemove");
        probe.Should().Contain("scrot -o");
        probe.Should().Contain("PowerPoint presentations (*.pptx)");
        probe.Should().Contain("state-collision-before.json");
        probe.Should().Contain("state-collision-after.json");
        probe.Should().Contain("state-invalid-target.json");
        probe.Should().Contain("containsPresentationXml");
        probe.Should().Contain("sha256");
        probe.Should().Contain("no_modal_blocker");
        probe.Should().NotContain("SetFilePickerOverridesForTests");
        probe.Should().NotContain("callback");
    }

    [Fact]
    public void Native_picker_runner_uses_the_existing_harness_and_strict_artifacts()
    {
        var runner = File.ReadAllText(RepoFile("tools/Run-FreePNativePickerX11Validation.ps1"));

        runner.Should().Contain("Run-LinuxInteractiveDocker.ps1");
        runner.Should().Contain("-DocumentPath");
        runner.Should().Contain("docker @dockerArguments");
        runner.Should().Contain("FREEP_PICKER_OPEN_SELECTED_PATH");
        runner.Should().Contain("FREEP_PICKER_COLLISION_PATH");
        runner.Should().Contain("FREEP_PICKER_INVALID_PATH");
        runner.Should().Contain("Assert-ManifestContract");
        runner.Should().Contain("Assert-PackageState");
        runner.Should().Contain("Get-ManifestEvidenceFileMap");
        runner.Should().Contain("-Action", "Stop");
        runner.Should().Contain("tools/LinuxInteractiveDocker/freep-native-picker-x11-wave90-validation.schema.json");
        runner.Should().NotContain("FreeX");
        runner.Should().NotContain("FreeW");
    }

    private static string RepoFile(string relativePath) =>
        TestWorkspaceFileLocator.Find(relativePath);
}
