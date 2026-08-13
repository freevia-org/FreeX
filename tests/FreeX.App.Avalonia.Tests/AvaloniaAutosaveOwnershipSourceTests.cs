namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaAutosaveOwnershipSourceTests
{
    [Fact]
    public void AvaloniaAutosave_UsesPortableServiceAndRetainsOnlyNativeLifetimeWiring()
    {
        var appSource = TestWorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Avalonia",
            "App.cs");

        appSource.Should().Contain("private readonly AutosaveService _service;");
        appSource.Should().Contain("_service.Attach(mainWindow, Guid.NewGuid());");
        appSource.Should().Contain("Interval = AutosaveService.DefaultInterval");
        appSource.Should().Contain("_service.OnTimerTick()");
        appSource.Should().Contain("coordinator._service.TryEmergencySnapshot()");
        appSource.Should().Contain("_service.DeleteSnapshot();");
        appSource.Should().Contain("_service.Dispose();");

        appSource.Should().Contain("DispatcherTimer");
        appSource.Should().Contain("ActiveCoordinators");
        appSource.Should().NotContain("AutosaveSnapshotCoordinator");
        appSource.Should().NotContain("NativeJsonAdapter");
        appSource.Should().NotContain("IAutosaveSnapshotSource");
        appSource.Should().NotContain("SessionSnapshotSource");
        appSource.Should().NotContain("TimeSpan.FromMinutes(5)");
    }

    [Fact]
    public void AvaloniaMainWindow_ProjectsSessionThroughSharedAutosaveContract()
    {
        var source = TestWorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.Autosave.cs");

        source.Should().Contain("MainWindow : IAutosaveWorkbookSource");
        source.Should().Contain("IAutosaveWorkbookSource.Workbook => _session.Workbook");
        source.Should().Contain("IAutosaveWorkbookSource.CurrentFilePath => _session.CurrentFilePath");
        source.Should().Contain("IAutosaveWorkbookSource.DisplayName => _session.DisplayName");
        source.Should().Contain("IAutosaveWorkbookSource.IsWorkbookDirty => _session.IsDirty");
        source.Should().Contain("IAutosaveWorkbookSource.WorkbookDirtyGeneration => _session.DirtyGeneration");
        source.Should().Contain("IAutosaveWorkbookSource.DocumentId => _session.Workbook.Id.Value.ToString()");
    }

    [Fact]
    public void SnapshotIdentity_IsComposedOnlyByPortableAutosaveService()
    {
        var serviceSource = TestWorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Services",
            "AutosaveService.cs");
        var avaloniaSource = TestWorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Avalonia",
            "App.cs");
        var wpfSource = TestWorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Host",
            "MainWindow.Autosave.cs");

        serviceSource.Should().Contain("$\"recovery-{Environment.ProcessId}-{launchTag}-{windowTag}\"");
        avaloniaSource.Should().Contain("_service.Attach(mainWindow, Guid.NewGuid());");
        wpfSource.Should().Contain("_autosaveService.Attach(this, _autosaveWindowId);");
        avaloniaSource.Should().NotContain("AutosaveSnapshotStore.LaunchId.ToString");
        wpfSource.Should().NotContain("AutosaveSnapshotStore.LaunchId.ToString");
    }
}
