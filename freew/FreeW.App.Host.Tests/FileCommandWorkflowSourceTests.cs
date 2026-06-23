using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class FileCommandWorkflowSourceTests
{
    [Theory]
    [InlineData("freew", "FreeW.App.Host")]
    [InlineData("freep", "FreeP.App.Host")]
    public void SisterAppFileCommands_UseSharedWorkflow(string appFolder, string projectFolder)
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            appFolder,
            projectFolder,
            "FileCommands.cs"));

        source.Should().Contain("FileCommandWorkflow");
        source.Should().Contain("_workflow.New(");
        source.Should().Contain("_workflow.Open(");
        source.Should().Contain("_workflow.Save(");
        source.Should().Contain("_workflow.ConfirmCloseAllowed(");
        source.Should().Contain("FileCommandMessageBox.PromptSaveChanges(");
        source.Should().Contain("FileCommandMessageBox.ShowError(");
        source.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        source.Should().Contain("WpfFileDialogService.ShowSaveDialog(");
        if (appFolder == "freew")
        {
            source.Should().Contain("OpenRecentPath(string path)");
            source.Should().Contain("_workflow.Open(\"opening another document\", () => path, OpenPath)");
            source.Should().Contain("OpenFromFolder(string folderPath)");
            source.Should().Contain("PromptOpenPath(folderPath)");
            source.Should().Contain("initialDirectory: initialDirectory");
            source.Should().Contain("SaveAsSuggested(string? suggestedFileName, string? preferredExtension)");
            source.Should().Contain("TryPromptSaveTarget(preferredExtension, suggestedFileName");
        }
        source.Should().NotContain("new FileCommandSession");
        source.Should().NotContain("FileLifecyclePlanner.PlanSave(");
        source.Should().NotContain(".ConfirmDiscardOrSave(action");
        source.Should().NotContain("MessageBox.Show(");
        source.Should().NotContain("new OpenFileDialog");
        source.Should().NotContain("new SaveFileDialog");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
