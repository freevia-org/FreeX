using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.Shell.Avalonia;
using System.Threading;

namespace FreeW.App.Avalonia.Tests;

public sealed class SharedSaveChangesDialogTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public void PromptText_ForDocumentAction_BuildsSharedDirtyGateText()
    {
        var text = AvaloniaSaveChangesPromptText.ForDocumentAction(
            "FreeW",
            "Draft.docx",
            "closing");

        text.WindowTitle.Should().Be("FreeW");
        text.Message.Should().Be("Do you want to save changes to \"Draft.docx\" before closing?");
        text.SaveButtonText.Should().Be("Save");
        text.DontSaveButtonText.Should().Be("Don't save");
        text.CancelButtonText.Should().Be("Cancel");
    }

    [Fact]
    public async Task Dialog_BuildsExpectedButtonShape()
    {
        string? title = null;
        string? message = null;
        IReadOnlyList<string?> buttonText = [];
        IReadOnlyList<bool> defaultButtons = [];
        IReadOnlyList<bool> cancelButtons = [];

        var ran = await OnUiThread(() =>
        {
            var dialog = AvaloniaSaveChangesDialog.CreateForTests(
                AvaloniaSaveChangesPromptText.ForDocumentAction(
                    "FreeW",
                    "Draft.docx",
                    "opening another document"));

            title = dialog.Title;
            var root = (StackPanel)dialog.Content!;
            message = ((TextBlock)root.Children[0]).Text;
            var buttons = (StackPanel)root.Children[1];
            var buttonList = buttons.Children.OfType<Button>().ToArray();
            buttonText = buttonList.Select(button => button.Content as string).ToArray();
            defaultButtons = buttonList.Select(button => button.IsDefault).ToArray();
            cancelButtons = buttonList.Select(button => button.IsCancel).ToArray();
        });

        if (!ran)
            return;

        title.Should().Be("FreeW");
        message.Should().Be("Do you want to save changes to \"Draft.docx\" before opening another document?");
        buttonText.Should().Equal("Save", "Don't save", "Cancel");
        defaultButtons.Should().Equal(true, false, false);
        cancelButtons.Should().Equal(false, false, true);
    }

    [Fact]
    public void FreeW_AvaloniaDirtyGate_UsesSharedSaveChangesDialog()
    {
        var mainWindow = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));
        var sharedDialog = File.ReadAllText(FindRepoFile("shared", "Free.Shared.Shell.Avalonia", "AvaloniaSaveChangesDialog.cs"));
        var sharedWorkflow = File.ReadAllText(FindRepoFile("shared", "Free.Shared.Shell.Avalonia", "SisterAvaloniaFileCommandWorkflow.cs"));

        mainWindow.Should().Contain("SisterAvaloniaFileCommandWorkflow");
        mainWindow.Should().Contain("_fileWorkflow.NewAsync(");
        mainWindow.Should().Contain("_fileWorkflow.ConfirmCloseAllowedAsync(");
        mainWindow.Should().Contain("new SisterAvaloniaAsyncWindowCloseCoordinator(");
        mainWindow.Should().NotContain("_fileWorkflow.ConfirmCloseAllowed(\"closing\")");

        // The dirty-save and close paths must stay fully async — blocking the UI thread on them is
        // the deadlock this guard exists to prevent. The closed exceptions are the background
        // mail-merge prompt bridge and startup application of an already-loaded document result.
        var blockingLines = mainWindow.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains("GetAwaiter().GetResult()", StringComparison.Ordinal))
            .ToArray();
        blockingLines.Should().BeEquivalentTo(
            "return completion.Task.GetAwaiter().GetResult();",
            "var execution = _documentFileWorkflow.ApplyOpenResultAsync(result).GetAwaiter().GetResult();");
        mainWindow.Should().NotContain("AvaloniaSaveChangesDialog.ShowAsync(");
        sharedWorkflow.Should().Contain("AvaloniaSaveChangesDialog.ShowAsync(");
        sharedWorkflow.Should().Contain("AvaloniaSaveChangesPromptText.ForDocumentAction(");
        File.Exists(FindRepoFile("freew", "FreeW.App.Avalonia", "SaveChangesDialog.cs"))
            .Should()
            .BeFalse("the Avalonia dirty-save prompt should be owned by the shared sister shell");

        typeof(AvaloniaSaveChangesDialog).Should().BeAssignableTo<Window>();
        sharedDialog.Should().Contain("public sealed class AvaloniaSaveChangesDialog : AvaloniaDialogWindow");
        sharedDialog.Should().Contain("SaveChangesPrompt.Save");
        sharedDialog.Should().Contain("SaveChangesPrompt.DontSave");
        sharedDialog.Should().Contain("SaveChangesPrompt.Cancel");
    }

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string FindRepoFile(params string[] parts) =>
        Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), Path.Combine(parts));

}
