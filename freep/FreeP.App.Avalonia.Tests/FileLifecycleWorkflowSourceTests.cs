using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class FileLifecycleWorkflowSourceTests
{
    [Fact]
    public void MainWindow_RoutesFileLifecycleThroughSharedWorkflow()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));
        var ports = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.FileCommandPorts.cs"));
        var session = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationFileCommandSession.cs"));
        var lifecycleAdapter = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationFileLifecycleAdapter.cs"));
        var project = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "FreeP.App.Avalonia.csproj"));
        var sharedShellWorkflow = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "SisterAvaloniaFileCommandWorkflow.cs"));

        source.Should().Contain("private readonly SisterAvaloniaFileCommandWorkflow _fileWorkflow;");
        source.Should().Contain("private readonly PresentationFileCommandSession _fileSession;");
        source.Should().Contain("new SisterAvaloniaFileCommandWorkflow(");
        source.Should().Contain("new PresentationFileCommandSession(");
        source.Should().Contain("new SisterAvaloniaFileTitleSpec(");
        source.Should().Contain("_fileSession.NewAsync()");
        source.Should().Contain("_fileSession.OpenAsync()");
        source.Should().Contain("_fileSession.SaveAsync()");
        source.Should().Contain("_fileSession.ConfirmCloseAllowedAsync()");
        source.Should().Contain("SisterAvaloniaAsyncWindowCloseCoordinator");
        source.Should().Contain("Closing += (_, e) => e.Cancel =");
        source.Should().Contain("_closeCoordinator.ShouldCancelClosing();");
        source.Should().Contain("new PresentationFileLifecycleAdapter(");
        source.Should().Contain("_fileWorkflow.Workflow");
        source.Should().Contain("_fileWorkflow.NewAsync(action, load)");
        source.Should().Contain("_fileWorkflow.OpenAsync");
        source.Should().Contain("_fileWorkflow.ConfirmCloseAllowedAsync");
        ports.Should().NotContain("AvaloniaPresentationFileLifecyclePort");
        lifecycleAdapter.Should().Contain("FileCommandWorkflow _workflow");
        lifecycleAdapter.Should().Contain("_workflow.SaveAsync(saveToCurrentPathAsync, saveAsAsync)");
        session.Should().Contain("PresentationFilePersistenceWorkflow.Open(path)");
        session.Should().Contain("PresentationFilePersistenceWorkflow.Save(path, _getPresentation())");
        session.Should().Contain("PresentationFileDialogPlanner.BuildOpenPickerPlan()");
        session.Should().Contain("PresentationFileDialogPlanner.BuildSavePickerPlan(");
        sharedShellWorkflow.Should().Contain("new FileCommandWorkflow(");
        sharedShellWorkflow.Should().Contain("ApplicationWindowTitlePolicy.Compose(");
        sharedShellWorkflow.Should().Contain("AvaloniaSaveChangesDialog.ShowAsync(");
        sharedShellWorkflow.Should().Contain("AvaloniaSaveChangesPromptText.ForDocumentAction(");
        sharedShellWorkflow.Should().Contain("RecentEntries => _workflow.RecentEntries");
        source.Should().NotContain("private string? _currentPath");
        source.Should().NotContain("private bool _isDirty");
        source.Should().NotContain("private async Task<SaveChangesPrompt> ShowSaveChangesPromptAsync");
        source.Should().NotContain("PromptSaveChangesSync");
        source.Should().NotContain("GetAwaiter().GetResult()");
        source.Should().NotContain("AvaloniaSaveChangesDialog.ShowAsync(");
        source.Should().NotContain("Do you want to save changes to");
        source.Should().NotContain("Content = \"Don't save\"");
        source.Should().NotContain("FileLifecyclePlanner.PlanSave(");
        source.Should().NotContain("new FileCommandSession");
        source.Should().Contain("PresentationFilePersistenceWorkflow.Open(startupPresentation)");
        source.Should().Contain("PresentationFilePersistenceWorkflow.IsSupportedPresentationPath(path)");
        source.Should().NotContain("PresentationFilePersistenceWorkflow.Save(");
        source.Should().NotContain("PresentationFileDialogPlanner.");
        source.Should().NotContain("v1: proceed without a save-changes dialog");
        project.Should().Contain(@"..\..\shared\Free.Shared.AppServices\Free.Shared.AppServices.csproj");
        project.Should().Contain(@"..\..\shared\Free.Shared.Shell.Avalonia\Free.Shared.Shell.Avalonia.csproj");
    }

}
