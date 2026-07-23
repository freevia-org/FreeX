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
        script.Should().Contain("docker exec --env DISPLAY=:99");
        script.Should().Contain("Physical X11 manifest does not satisfy schema v2");
        script.Should().Contain("x11-validation/x11-input-results.json");
    }
}
