using FreeP.App.Avalonia;

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
        var source = File.ReadAllText(RepoFile("freep/FreeP.App.Avalonia/AccessibilityValidation.cs"));

        source.Should().Contain("PaneAccessibilitySnapshotForTests");
        source.Should().Contain("AutomationProperties.GetAutomationId(control)");
        source.Should().Contain("AutomationProperties.GetName(control)");
        source.Should().Contain("AutomationProperties.GetItemStatus(control)");
        source.Should().Contain("control.GetType().Name");
        source.Should().Contain("atspi-result.json");
    }

    [Fact]
    public void Linux_accessibility_probe_is_os_bound_and_documents_unavailable_exposure()
    {
        var probe = File.ReadAllText(RepoFile("tools/LinuxInteractiveDocker/run-freep-accessibility-probe.sh"));
        var dockerfile = File.ReadAllText(RepoFile("tools/LinuxInteractiveDocker/Dockerfile"));

        probe.Should().Contain("pyatspi.Registry.getDesktop(0)");
        probe.Should().Contain("find_freep_window");
        probe.Should().Contain("getRoleName()");
        probe.Should().Contain("queryValue()");
        probe.Should().Contain("not-proven");
        probe.Should().Contain("contained a FreeP-titled window");
        dockerfile.Should().Contain("at-spi2-core");
        dockerfile.Should().Contain("python3-pyatspi");
    }

    private static string RepoFile(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath));
}
