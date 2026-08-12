using FluentAssertions;
using Xunit;

namespace FreeX.App.Services.Tests;

public sealed class PivotRuntimeEvidenceOwnershipSourceTests
{
    [Fact]
    public void ShippingRenderer_ExposesOnlyGenericPivotObservation()
    {
        var pivot = Read("src", "FreeX.App.Avalonia", "MainWindow.Pivot.cs");
        var access = Read("src", "FreeX.App.Avalonia", "MainWindow.PivotObservation.cs");

        pivot.Should().Contain("ObservePivotRuntimeState(");
        pivot.Should().NotContain("--freex-pivot-runtime-evidence");
        pivot.Should().NotContain("JsonSerializer");
        pivot.Should().NotContain("File.AppendAllText");
        pivot.Should().NotContain("App.StartupArguments");
        access.Should().Contain("SetObserver(Action<PivotRuntimeObservation> observer)");
    }

    [Fact]
    public void ValidationHost_OwnsPivotEvidenceOptionPayloadAndPersistence()
    {
        var source = Read("tools", "FreeX.Validation.Avalonia", "PivotRuntimeEvidence.cs");
        var program = Read("tools", "FreeX.Validation.Avalonia", "Program.cs");

        source.Should().Contain("--freex-pivot-runtime-evidence");
        source.Should().Contain("JsonSerializer.Serialize(payload)");
        source.Should().Contain("File.AppendAllText(path");
        program.Should().Contain("PivotRuntimeEvidenceOptions.TryParse(");
        program.Should().Contain("RunPivotRuntimeObservationHost(");
    }

    [Fact]
    public void PhysicalPivotWorkflow_UsesExternalTestSupportHost()
    {
        var runner = Read("tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var dockerHarness = Read("tools", "Run-LinuxInteractiveDocker.ps1");

        runner.Should().Contain("Start-ValidationSession -HostMode TestSupport");
        dockerHarness.Should().Contain("tools/FreeX.Validation.Avalonia/FreeX.Validation.Avalonia.csproj");
        dockerHarness.Should().Contain("Executable = \"FreeX.Validation.Avalonia\"");
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(RepositoryFileLocator.Find(segments));
}
