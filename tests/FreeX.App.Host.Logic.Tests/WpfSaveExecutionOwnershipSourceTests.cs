using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WpfSaveExecutionOwnershipSourceTests
{
    [Fact]
    public void SaveRenderer_DelegatesPortableExecutionAndRetainsNativeGateAndWriter()
    {
        var source = WorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Host",
            "MainWindow.Backstage.cs");

        source.Should().Contain("WorkbookSaveExecutionCoordinator.Begin(");
        source.Should().Contain("saveExecution.ExecuteAsync(");
        source.Should().NotContain("generationAtSaveStart");
        source.Should().NotContain("SaveCompletionPlanner.Plan(");
        source.Should().NotContain("catch (WorkbookExternallyModifiedException)");

        source.Should().Contain("AdjustSaveGate(acquire: true)");
        source.Should().Contain("BroadcastSaveInProgress(this, inProgress: true)");
        source.Should().Contain("new SaveWorkbookWriter().SaveAsync(");
        source.Should().Contain("ConfirmExternallyModifiedFileOverwrite");
    }
}
