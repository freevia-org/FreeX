using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class FileCommandsSourceTests
{
    [Fact]
    public void FileCommands_UsesSharedPerFormatDialogPlans()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.App.Host",
            "FileCommands.cs"));

        source.Should().Contain("PresentationFileDialogPlanner.BuildOpenDialogPlan()");
        source.Should().Contain("PresentationFileDialogPlanner.BuildSaveAsDialogPlan(");
        source.Should().Contain("PresentationFileDialogPlanner.BuildPdfExportDialogPlan(");
        source.Should().Contain("PresentationFileDialogPlanner.IsLegacyPresentationPath(path)");
        source.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        source.Should().Contain("WpfFileDialogService.ShowSaveDialog(");
        source.Should().Contain("IUserMessageService? messageService = null");
        source.Should().Contain("SisterWpfFileCommandWorkflow");
        source.Should().Contain("messageService);");
        source.Should().Contain("_workflow.ShowError(summary, ex");
        source.Should().NotContain("new FileDialogFormatDescriptor");
        source.Should().NotContain("FileDialogRequestPlanner.");
        source.Should().NotContain("FileDialogFilterBuilder.BuildPerFormatFilter(Formats)");
        source.Should().NotContain("FileDialogFilterBuilder.GetDefaultExtension(Formats)");
        source.Should().NotContain("FileCommandMessageBox.PromptSaveChanges(");
        source.Should().NotContain("FileCommandMessageBox.ShowError(");
        source.Should().NotContain("PromptSaveChanges(DisplayName, action");
        source.Should().NotContain("ShowFileCommandError(summary, ex");
        source.Should().NotContain("UserMessageButtons.YesNoCancel");
        source.Should().NotContain("UserMessageButtons.Ok");
        source.Should().NotContain("new OpenFileDialog");
        source.Should().NotContain("new SaveFileDialog");
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
