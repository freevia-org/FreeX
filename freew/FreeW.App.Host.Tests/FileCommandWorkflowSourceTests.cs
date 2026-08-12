using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class FileCommandWorkflowSourceTests
{
    [Theory]
    [InlineData("freew", "FreeW.App.Host", "FileCommands.cs")]
    [InlineData("freep", "FreeP.App.Host", "WpfPresentationFileCommandPorts.cs")]
    public void SisterAppFileCommands_UseSharedWorkflow(
        string appFolder,
        string projectFolder,
        string sourceFileName)
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            appFolder,
            projectFolder,
            sourceFileName));

        source.Should().Contain("SisterWpfFileCommandWorkflow");
        if (appFolder == "freep")
        {
            source.Should().Contain("PresentationFileCommandSession");
            source.Should().Contain("new PresentationFileLifecycleAdapter(workflow.Workflow)");
            source.Should().NotContain("WpfPresentationFileLifecyclePort");
            source.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
            source.Should().Contain("WpfFileDialogService.ShowSaveDialog(");
            source.Should().Contain("_workflow.ShowError(");
            source.Should().NotContain("new FileCommandSession");
            source.Should().NotContain("FileLifecyclePlanner.PlanSave(");
            return;
        }

        source.Should().Contain("_workflow.New(");
        source.Should().Contain("_workflow.Open(");
        source.Should().Contain("_workflow.Save(");
        source.Should().Contain("_workflow.ConfirmCloseAllowed(");
        source.Should().Contain("IUserMessageService? messageService = null");
        source.Should().Contain("messageService);");
        source.Should().Contain("_workflow.ShowError(summary, ex");
        source.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        source.Should().Contain("WpfFileDialogService.ShowSaveDialog(");
        if (appFolder == "freew")
        {
            source.Should().Contain("DocumentPersistenceWorkflow");
            source.Should().Contain("FreeWDocumentFileWorkflow");
            source.Should().Contain("OpenPathAsync(path, suppressRecentFiles)");
            source.Should().Contain("SavePathAsync(path, filterIndex, kind)");
            source.Should().Contain("_persistence.BuildSaveDialogPlan(");
            source.Should().Contain("OpenRecentPath(string path)");
            source.Should().Contain("_workflow.Open(\"opening another document\", () => path, OpenPath)");
            source.Should().Contain("OpenFromFolder(string folderPath)");
            source.Should().Contain("PromptOpenPath(folderPath)");
            source.Should().Contain("initialDirectory: initialDirectory");
            source.Should().Contain("SaveAsSuggested(string? suggestedFileName, string? preferredExtension)");
            source.Should().Contain("TryPromptSavePath(preferredExtension, suggestedFileName");
            source.Should().NotContain("DocumentFileFormatResolver.FindOpenAdapter");
            source.Should().NotContain("DocumentFileFormatResolver.FindSaveAdapter");
            source.Should().NotContain("FileDialogSaveSelectionResolver.ResolveAdapter");
            source.Should().NotContain("AtomicFileWriter.CreateTempPath");
            source.Should().NotContain("_persistence.Open(path)");
            source.Should().NotContain("_persistence.Save(_editor.Model, target)");
            source.Should().NotContain("_persistence.TryResolveSaveTarget(");
            source.Should().NotContain("new DocumentOpenExecutionRequest(");
            source.Should().NotContain("new DocumentSaveExecutionRequest(");
        }
        source.Should().NotContain("new FileCommandSession");
        source.Should().NotContain("FileLifecyclePlanner.PlanSave(");
        source.Should().NotContain(".ConfirmDiscardOrSave(action");
        source.Should().NotContain("FileCommandMessageBox.PromptSaveChanges(");
        source.Should().NotContain("FileCommandMessageBox.ShowError(");
        source.Should().NotContain("PromptSaveChanges(DisplayName, action");
        source.Should().NotContain("ShowFileCommandError(summary, ex");
        source.Should().NotContain("UserMessageButtons.YesNoCancel");
        source.Should().NotContain("UserMessageButtons.Ok");
        source.Should().NotContain("MessageBox.Show(");
        source.Should().NotContain("new OpenFileDialog");
        source.Should().NotContain("new SaveFileDialog");
    }

    [Fact]
    public void SharedWpfShellOwnsSisterFileCommandPromptWiring()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "shared",
            "Free.Shared.Shell.Wpf",
            "SisterWpfFileCommandWorkflow.cs"));

        source.Should().Contain("public sealed class SisterWpfFileCommandWorkflow");
        source.Should().Contain("new WpfUserMessageService()");
        source.Should().Contain("new FileCommandWorkflow(");
        source.Should().Contain("PromptSaveChanges,");
        source.Should().Contain("_messageService.PromptSaveChanges(DisplayName, action, _applicationName)");
        source.Should().Contain("_messageService.ShowFileCommandError(summary, exception, _applicationName)");
        source.Should().Contain("public void ShowError(string summary, Exception exception)");
    }

    [Fact]
    public void SharedUserMessageServiceOwnsFileCommandPromptPolicy()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "shared",
            "Free.Shared.AppServices",
            "IUserMessageService.cs"));

        source.Should().Contain("public static class UserMessageServiceFileCommandExtensions");
        source.Should().Contain("PromptSaveChanges(");
        source.Should().Contain("ShowFileCommandError(");
        source.Should().Contain("Do you want to save changes to {displayName} before {action}?");
        source.Should().Contain("UserMessageButtons.YesNoCancel");
        source.Should().Contain("UserMessageIcon.Warning");
        source.Should().Contain("UserMessageButtons.Ok");
        source.Should().Contain("UserMessageIcon.Error");
        source.Should().Contain("UserMessageResult.Yes => SaveChangesPrompt.Save");
        source.Should().Contain("UserMessageResult.No => SaveChangesPrompt.DontSave");
        source.Should().Contain("_ => SaveChangesPrompt.Cancel");
    }

}
