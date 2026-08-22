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

        source.Should().Contain("private readonly WpfAutosaveTimer _timer;");
        source.Should().Contain("new WpfAutosaveTimer(");
        source.Should().NotContain("DispatcherTimer");
        source.Should().NotContain(".Tick +=");
    }

    [Theory]
    [InlineData("freep", "FreeP.App.Host")]
    [InlineData("freew", "FreeW.App.Host")]
    public void AutosaveCoordinator_UsesSharedWpfRecoveryHostPolicy(
        string productDirectory,
        string hostProject)
    {
        var source = ReadHostSource(productDirectory, hostProject, "AutosaveCoordinator.cs");

        source.Should().Contain("WpfAutosaveRecoveryHost.OfferStartup(");
        source.Should().Contain("WpfAutosaveRecoveryHost.RecoverManually(");
        source.Should().NotContain("DialogMessageHelper.");
        source.Should().NotContain("catch (Exception ex)");
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

    private static string ReadHostSource(string productDirectory, string hostProject, string fileName) =>
        File.ReadAllText(TestWorkspaceFileLocator.Find(productDirectory, hostProject, fileName));
}
