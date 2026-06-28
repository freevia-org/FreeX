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

        source.Should().Contain("FileDialogRequestPlanner.BuildPerFormatOpenDialogPlan(Formats)");
        source.Should().Contain("FileDialogRequestPlanner.BuildPerFormatSaveDialogPlanFromSourceName(");
        source.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        source.Should().Contain("WpfFileDialogService.ShowSaveDialog(");
        source.Should().Contain("IUserMessageService? messageService = null");
        source.Should().Contain("_messageService = messageService ?? new WpfUserMessageService();");
        source.Should().Contain("_messageService.PromptSaveChanges(DisplayName, action");
        source.Should().Contain("_messageService.ShowFileCommandError(summary, ex");
        source.Should().NotContain("FileDialogFilterBuilder.BuildPerFormatFilter(Formats)");
        source.Should().NotContain("FileDialogFilterBuilder.GetDefaultExtension(Formats)");
        source.Should().NotContain("FileCommandMessageBox.PromptSaveChanges(");
        source.Should().NotContain("FileCommandMessageBox.ShowError(");
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
