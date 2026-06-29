using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class FileLifecycleWorkflowSourceTests
{
    [Fact]
    public void MainWindow_RoutesFileLifecycleThroughSharedWorkflow()
    {
        var root = FindRepositoryRoot();
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

        source.Should().Contain("private readonly FileCommandWorkflow _fileWorkflow;");
        source.Should().Contain("new FileCommandWorkflow(");
        source.Should().Contain("_fileWorkflow.New(");
        source.Should().Contain("_fileWorkflow.OpenAsync(");
        source.Should().Contain("_fileWorkflow.SaveAsync(");
        source.Should().Contain("_fileWorkflow.MarkSavedWithoutPath()");
        source.Should().Contain("_fileWorkflow.MarkSavedWithPath(path, suppressRecentFiles: false)");
        source.Should().Contain("_fileWorkflow.MarkDirty();");
        source.Should().Contain("PromptSaveChangesSync");
        source.Should().Contain("PresentationFileDialogPlanner.BuildOpenPickerPlan()");
        source.Should().Contain("PresentationFileDialogPlanner.BuildSavePickerPlan(");
        source.Should().NotContain("private string? _currentPath");
        source.Should().NotContain("private bool _isDirty");
        source.Should().NotContain("FileLifecyclePlanner.PlanSave(");
        source.Should().NotContain("new FileCommandSession");
        source.Should().NotContain("v1: proceed without a save-changes dialog");
        project.Should().Contain(@"..\..\shared\Free.Shared.AppServices\Free.Shared.AppServices.csproj");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
