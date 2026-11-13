using System.IO;

namespace Free.Shared.Shell.Wpf.Tests;

public sealed class WpfAutosaveHostRuntimeAdoptionTests
{
    [Theory]
    [InlineData("freep", "FreeP.App.Host")]
    [InlineData("freew", "FreeW.App.Host")]
    public void AutosaveCoordinator_UsesSharedWpfTimer(string productDirectory, string hostProject)
    {
        var source = ReadHostSource(productDirectory, hostProject, "AutosaveCoordinator.cs");
        var shared = ReadSharedAutosaveHost();

        shared.Should().Contain("private readonly WpfAutosaveTimer _timer;");
        shared.Should().Contain("new WpfAutosaveTimer(");
        foreach (var text in new[] { source, shared })
        {
            text.Should().NotContain("DispatcherTimer");
            text.Should().NotContain(".Tick +=");
        }

        source.Should().Contain("new WpfAutosaveHost<");
        source.Should().NotContain("WpfAutosaveTimer");
    }

    [Theory]
    [InlineData("freep", "FreeP.App.Host")]
    [InlineData("freew", "FreeW.App.Host")]
    public void AutosaveCoordinator_UsesSharedWpfRecoveryHostPolicy(
        string productDirectory,
        string hostProject)
    {
        var source = ReadHostSource(productDirectory, hostProject, "AutosaveCoordinator.cs");
        var shared = ReadSharedAutosaveHost();

        shared.Should().Contain("WpfAutosaveRecoveryHost.OfferStartup(");
        shared.Should().Contain("WpfAutosaveRecoveryHost.RecoverManually(");
        foreach (var text in new[] { source, shared })
        {
            text.Should().NotContain("DialogMessageHelper.");
            text.Should().NotContain("catch (Exception ex)");
        }

        // The product coordinator is a thin binding: it must not re-grow its own recovery walk.
        source.Should().NotContain("WpfAutosaveRecoveryHost.")
            .And.NotContain("AutosaveRecoveryPromptMode")
            .And.NotContain("AutosaveRecoveryWorkflow")
            .And.NotContain("CreateOffer(");
    }

    [Theory]
    [InlineData("freep", "FreeP.App.Host")]
    [InlineData("freew", "FreeW.App.Host")]
    public void EmergencyCrashHandler_UsesSharedBoundedFanOutAndKeepsProductFilter(
        string productDirectory,
        string hostProject)
    {
        var source = ReadHostSource(productDirectory, hostProject, "EmergencySnapshotCrashHandler.cs");

        source.Should().Contain("WpfEmergencySnapshotFanOut.TrySnapshotAllWindows(window =>");
        source.Should().Contain("window is MainWindow mainWindow");
        source.Should().Contain("mainWindow.AutosaveCoordinatorForCrashHandler?.TryEmergencySnapshot()");
        source.Should().NotContain("Application.Current?.Dispatcher");
        source.Should().NotContain("dispatcher.Invoke(");
        source.Should().NotContain("foreach (Window window in Application.Current.Windows)");
    }

    [Fact]
    public void SharedEmergencyRuntime_PinsSendPriorityAndEightSecondTimeout()
    {
        var source = File.ReadAllText(TestWorkspaceFileLocator.Find(
            "shared", "Free.Shared.Shell.Wpf", "WpfEmergencySnapshotFanOut.cs"));

        source.Should().Contain("TimeSpan.FromSeconds(8)");
        source.Should().Contain("DispatcherPriority.Send");
        source.Should().Contain("CancellationToken.None");
    }

    private static string ReadSharedAutosaveHost() =>
        File.ReadAllText(TestWorkspaceFileLocator.Find(
            "shared", "Free.Shared.Shell.Wpf", "WpfAutosaveHost.cs"));

    private static string ReadHostSource(string productDirectory, string hostProject, string fileName) =>
        File.ReadAllText(TestWorkspaceFileLocator.Find(productDirectory, hostProject, fileName));
}
