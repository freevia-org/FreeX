using FreeP.Validation.Avalonia;

namespace FreeP.App.Avalonia.Tests;

public sealed class AccessibilityValidationSourceTests
{
    [Fact]
    public void Accessibility_validation_option_filters_only_its_control_arguments()
    {
        AccessibilityValidationOptions.TryParse(
            ["--accessibility-validation", "/work/accessibility", "/documents/demo.pptx"],
            out var options,
            out var startupArguments,
            out var error).Should().BeTrue(error);

        options.Should().NotBeNull();
        options!.OutputDirectory.Should().Be("/work/accessibility");
        startupArguments.Should().Equal("/documents/demo.pptx");
        error.Should().BeNull();
    }

    [Fact]
    public void Accessibility_validation_source_reads_live_control_metadata()
    {
        var source = File.ReadAllText(RepoFile("freep/TestSupport/Validation.Avalonia/AccessibilityValidation.cs"));
        var adapter = File.ReadAllText(RepoFile("freep/FreeP.App.Avalonia/MainWindow.ValidationAccessAdapter.cs"));
        var program = File.ReadAllText(RepoFile("freep/FreeP.App.Avalonia/Program.cs"));

        source.Should().Contain("CaptureAccessibilityPanes");
        source.Should().Contain("FocusRepresentativeAccessibilityPanes");
        source.Should().Contain("atspi-ready.json");
        source.Should().Contain("atspi-result.json");
        adapter.Should().Contain("AutomationProperties.GetAutomationId(control)");
        adapter.Should().Contain("AutomationProperties.GetName(control)");
        adapter.Should().Contain("AutomationProperties.GetItemStatus(control)");
        adapter.Should().Contain("control.GetType().Name");
        adapter.Should().NotContain("atspi-ready.json");
        adapter.Should().NotContain("File.Exists");
        program.Should().NotContain("AccessibilityValidationOptions");
        program.Should().NotContain("--accessibility-validation");
        File.Exists(Path.Combine(Path.GetDirectoryName(RepoFile(
            "freep/FreeP.App.Avalonia/Program.cs"))!, "AccessibilityValidation.cs")).Should().BeFalse();
    }

    [Fact]
    public void Linux_accessibility_probe_is_os_bound_and_documents_unavailable_exposure()
    {
        var probe = File.ReadAllText(RepoFile("tools/LinuxInteractiveDocker/run-freep-accessibility-probe.sh"));
        var dockerfile = File.ReadAllText(RepoFile("tools/LinuxInteractiveDocker/Dockerfile"));

        probe.Should().Contain("pyatspi.Registry.getDesktop(0)");
        probe.Should().Contain("find_freep_window");
        probe.Should().Contain("target_contracts");
        probe.Should().Contain("lower_name == contract[\"name\"]");
        probe.Should().Contain("role_name in contract[\"roles\"]");
        probe.Should().Contain("getRoleName()");
        probe.Should().Contain("queryValue()");
        probe.Should().Contain("object:state-changed:focused");
        probe.Should().Contain("xdotool");
        probe.Should().Contain("focusTraversal");
        probe.Should().Contain("focusable");
        probe.Should().Contain("Labels are excluded by role matching");
        probe.Should().Contain("not-proven");
        probe.Should().Contain("uniquely identified FreeP window");
        dockerfile.Should().Contain("at-spi2-core");
        dockerfile.Should().Contain("python3-pyatspi");
    }

    [Fact]
    public void Focus_evidence_schema_requires_the_event_trail_contract()
    {
        var schema = File.ReadAllText(RepoFile("tools/LinuxInteractiveDocker/freep-atspi-validation.schema.json"));

        schema.Should().Contain("\"schemaVersion\": { \"const\": 2 }");
        schema.Should().Contain("os-atspi-x11-focus-events");
        schema.Should().Contain("focusEvents");
        schema.Should().Contain("expectedFocusOrder");
        schema.Should().Contain("keyboardTraversal");
        schema.Should().Contain("focusEventCount");
    }

    [Fact]
    public void Validation_runner_copies_the_branch_local_probe_before_execution()
    {
        var runner = File.ReadAllText(RepoFile("tools/Run-FreePAccessibilityValidation.ps1"));

        runner.Should().Contain("docker cp");
        runner.Should().Contain("run-freep-accessibility-probe.sh");
        runner.Should().Contain("/tmp/freep-accessibility-probe.sh");
        runner.Should().Contain("/bin/bash /tmp/freep-accessibility-probe.sh");
        runner.Should().Contain("os-atspi-x11-focus-events");
        runner.Should().Contain("expectedFocusOrder");
        runner.Should().Contain("wave59-report");
        runner.Should().Contain("\"-Host\", \"Validation\"");
    }

    private static string RepoFile(string relativePath) =>
        TestWorkspaceFileLocator.Find(relativePath);
}
