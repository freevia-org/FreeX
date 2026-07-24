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
        source.Should().Contain("new SisterAvaloniaFileCommandWorkflow(");
        source.Should().Contain("new SisterAvaloniaFileTitleSpec(");
        source.Should().Contain("_fileWorkflow.NewAsync(");
        source.Should().Contain("_fileWorkflow.OpenAsync(");
        source.Should().Contain("_fileWorkflow.SaveAsync(");
        source.Should().Contain("_fileWorkflow.ConfirmCloseAllowedAsync(");
        source.Should().Contain("SisterAvaloniaAsyncWindowCloseCoordinator");
        source.Should().Contain("Closing += (_, e) => e.Cancel =");
        source.Should().Contain("_closeCoordinator.ShouldCancelClosing();");
        source.Should().Contain("_fileWorkflow.ShowFileCommandErrorAsync(\"Could not open the presentation\"");
        source.Should().Contain("_fileWorkflow.ShowFileCommandErrorAsync(\"Could not save the presentation\"");
        source.Should().Contain("_fileWorkflow.MarkSavedWithoutPath()");
        source.Should().Contain("_fileWorkflow.MarkSavedWithPath(path, suppressRecentFiles)");
        source.Should().Contain("_fileWorkflow.MarkDirty();");
        source.Should().Contain("PresentationFilePersistenceWorkflow.Open(path)");
        source.Should().Contain("PresentationFilePersistenceWorkflow.Save(path, _presentation)");
        source.Should().Contain("PresentationFileDialogPlanner.BuildOpenPickerPlan()");
        source.Should().Contain("PresentationFileDialogPlanner.BuildSavePickerPlan(");
        sharedShellWorkflow.Should().Contain("new FileCommandWorkflow(");
        sharedShellWorkflow.Should().Contain("WindowTitlePlanner.Compose(");
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
        source.Should().NotContain("v1: proceed without a save-changes dialog");
        project.Should().Contain(@"..\..\shared\Free.Shared.AppServices\Free.Shared.AppServices.csproj");
        project.Should().Contain(@"..\..\shared\Free.Shared.Shell.Avalonia\Free.Shared.Shell.Avalonia.csproj");
    }

}
