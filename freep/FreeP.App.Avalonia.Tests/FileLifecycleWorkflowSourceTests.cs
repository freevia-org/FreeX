using System.IO;
using System.Text.RegularExpressions;

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
        // Constructed through PresentationFileCommandSessionFactory now; the guard is that the
        // shell still routes file commands through that shared session, not that it news it up.
        source.Should().Contain("PresentationFileCommandSessionFactory.Create(");
        source.Should().Contain("new PresentationFileCommandSessionComposition(");
        source.Should().Contain("new SisterAvaloniaFileTitleSpec(");
        source.Should().Contain("_fileSession.NewAsync()");
        source.Should().Contain("_fileSession.OpenAsync()");
        source.Should().Contain("_fileSession.SaveAsync()");
        source.Should().Contain("_fileSession.ConfirmCloseAllowedAsync()");
        source.Should().Contain("SisterAvaloniaAsyncWindowCloseCoordinator");
        source.Should().Contain("Closing += (_, e) =>");
        source.Should().Contain("var cancel = _closeCoordinator.ShouldCancelClosing();");
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
        session.Should().Contain("ExternalFileWriteConflictPolicy.SelectExpectedLastWriteTimeUtc(");
        session.Should().Contain("ExternalFileWriteConflictPolicy.PrepareAsync(");
        session.Should().Contain("conflictPreparation.ExpectedLastWriteTimeUtc");
        session.Should().Contain("PresentationFilePersistenceWorkflow.Save(");
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
        (ports + lifecycleAdapter + session).Should().NotContain(
            "GetAwaiter().GetResult()",
            "the file-lifecycle chain must stay non-blocking -- these types are driven from the UI "
            + "thread, so blocking on a Task here deadlocks when the awaited work needs that thread "
            + "to pump. This is a plain substring match, so it also trips on the token appearing in "
            + "a COMMENT: describe the pattern in prose rather than quoting the call.");
        source.Should().NotContain("AvaloniaSaveChangesDialog.ShowAsync(");
        source.Should().NotContain("Do you want to save changes to");
        source.Should().NotContain("Content = \"Don't save\"");
        source.Should().NotContain("FileLifecyclePlanner.PlanSave(");
        source.Should().NotContain("new FileCommandSession");
        source.Should().Contain("new PresentationStartupOpenSession(_fileSession)");
        source.Should().Contain("startupOpenSession.Plan(startupArguments)");
        source.Should().NotContain("PresentationFilePersistenceWorkflow.Open(startupPresentation)");
        source.Should().NotContain("PresentationFilePersistenceWorkflow.Save(");
        source.Should().NotContain("PresentationFileDialogPlanner.");
        source.Should().NotContain("v1: proceed without a save-changes dialog");
        project.Should().Contain(@"..\..\shared\Free.Shared.AppServices\Free.Shared.AppServices.csproj");
        project.Should().Contain(@"..\..\shared\Free.Shared.Shell.Avalonia\Free.Shared.Shell.Avalonia.csproj");
    }

    [Fact]
    public void NewWindow_PreservesTheInjectedMessageService()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));

        var method = ExtractMethod(source, "private void OpenNewPresentationWindow()");
        var constructor = ExtractInvocation(method, "new MainWindow(");

        Regex.IsMatch(
                constructor,
                @"optionsStore\s*:\s*_optionsStore\s*,\s*messageService\s*:\s*_messageService\s*,\s*documentWindowPlanner\s*:\s*_documentWindowPlanner\s*,\s*documentWindowNumber\s*:\s*plan\.WindowNumber\s*\)",
                RegexOptions.CultureInvariant)
            .Should().BeTrue("the new presentation window must preserve all injected services and its planned window number");
    }

    private static string ExtractMethod(string source, string signature)
    {
        source = NormalizeLineEndings(source);
        var signatureStart = source.IndexOf(signature, StringComparison.Ordinal);
        signatureStart.Should().BeGreaterThanOrEqualTo(0);

        var bodyStart = source.IndexOf('{', signatureStart);
        bodyStart.Should().BeGreaterThanOrEqualTo(0);
        var bodyEnd = FindMatchingDelimiter(source, bodyStart, '{', '}');
        return source[signatureStart..(bodyEnd + 1)];
    }

    private static string ExtractInvocation(string source, string invocationStart)
    {
        var invocationIndex = source.IndexOf(invocationStart, StringComparison.Ordinal);
        invocationIndex.Should().BeGreaterThanOrEqualTo(0);

        var argumentStart = invocationIndex + invocationStart.Length - 1;
        var invocationEnd = FindMatchingDelimiter(source, argumentStart, '(', ')');
        return source[invocationIndex..(invocationEnd + 1)];
    }

    private static int FindMatchingDelimiter(string source, int openingIndex, char opening, char closing)
    {
        var depth = 0;
        for (var index = openingIndex; index < source.Length; index++)
        {
            if (source[index] == opening)
                depth++;
            else if (source[index] == closing && --depth == 0)
                return index;
        }

        throw new InvalidOperationException($"No matching '{closing}' was found for index {openingIndex}.");
    }

    private static string NormalizeLineEndings(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

}
