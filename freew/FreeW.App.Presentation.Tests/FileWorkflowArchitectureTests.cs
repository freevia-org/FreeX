namespace FreeW.App.Presentation.Tests;

public sealed class FileWorkflowArchitectureTests
{
    [Fact]
    public void SharedWorkflowOwnsDocumentExecutionAndLifecyclePublication()
    {
        var source = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Shell",
            "FreeWDocumentFileWorkflow.cs");

        source.Should().Contain("public sealed class FreeWDocumentFileWorkflow");
        source.Should().Contain("new DocumentFileExecutionCoordinator(persistence)");
        source.Should().Contain("_lifecycle.MarkSavedWithPath(");
        source.Should().Contain("_lifecycle.MarkDirtyWithPath(result.TargetPath)");
        source.Should().Contain("_persistence.TryResolveSaveTarget(path, filterIndex, out var target)");
        source.Should().Contain("_persistence.ImportPdfText(path)");
        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia");
        source.Should().NotContain("DocumentView");
        source.Should().NotContain("OpenFileDialog");
        source.Should().NotContain("SaveFileDialog");
    }

    [Fact]
    public void SharedFeedbackPlannerOwnsFileOutcomeMessagesAndErrorDecisions()
    {
        var source = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Shell",
            "FreeWDocumentFileFeedbackPlanner.cs");

        source.Should().Contain("public static class FreeWDocumentFileFeedbackPlanner");
        source.Should().Contain("PlanOpen(");
        source.Should().Contain("PlanImport(");
        source.Should().Contain("PlanSave(");
        source.Should().Contain("PlanSnapshot(");
        source.Should().Contain("SisterAppFileTextPlanner.FormatUnsupportedFileType(");
        source.Should().Contain("SisterAppFileTextPlanner.FormatCommandFailed(");
        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia");
    }

    [Fact]
    public void RenderersDelegateFileResultAndTargetHandling()
    {
        var session = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Shell",
            "FreeWDocumentFileCommandSession.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "FileCommands.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "MainWindow.cs");

        session.Should().Contain("public sealed class FreeWDocumentFileCommandSession");
        session.Should().Contain("_workflow.OpenPathAsync(path, suppressRecentFiles)");
        session.Should().Contain("_workflow.ImportPdfTextPathAsync(path)");
        session.Should().Contain("_workflow.SaveCurrentPathAsync(path)");
        session.Should().Contain("_workflow.SavePathAsync(path, filterIndex, kind)");
        session.Should().Contain("FreeWDocumentFileFeedbackPlanner.PlanOpen(");
        session.Should().Contain("FreeWDocumentFileFeedbackPlanner.PlanImport(");
        session.Should().Contain("FreeWDocumentFileFeedbackPlanner.PlanSave(");
        session.Should().Contain("public Task<bool> SaveAsFormatAsync(string preferredExtension)");

        foreach (var renderer in new[] { wpf, avalonia })
        {
            renderer.Should().Contain("FreeWDocumentFileCommandSession");
            renderer.Should().Contain("FreeWDocumentFileCommandPorts");
            renderer.Should().Contain("FreeWFileCommandLifecyclePorts");
            renderer.Should().Contain("_fileCommands.SaveAsFormatAsync(");
            renderer.Should().NotContain("FreeWDocumentFileFeedbackPlanner.PlanOpen(");
            renderer.Should().NotContain("FreeWDocumentFileFeedbackPlanner.PlanImport(");
            renderer.Should().NotContain("FreeWDocumentFileFeedbackPlanner.PlanSave(");
        }

        foreach (var renderer in new[] { wpf, avalonia })
        {
            renderer.Should().NotContain("new DocumentOpenExecutionRequest(");
            renderer.Should().NotContain("new DocumentSaveExecutionRequest(");
            renderer.Should().NotContain("_persistence.Save(");
            renderer.Should().NotContain("_documentPersistence.Save(");
            renderer.Should().NotContain("DocumentFileFormatResolver.FindSaveAdapter(");
            renderer.Should().NotContain("RecentFilesStore.Load(");
            renderer.Should().NotContain("execution.Outcome == DocumentFileExecutionOutcome.UnsupportedFormat");
            renderer.Should().NotContain("HandleSaveResult(");
        }

        avalonia.Should().NotContain("SaveAsWithFormatAsync(");
        avalonia.Should().NotContain("_documentPersistence.TryGetSaveFormat(");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
