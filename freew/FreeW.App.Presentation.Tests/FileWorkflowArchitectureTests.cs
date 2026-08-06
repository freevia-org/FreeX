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
        var wpf = ReadSource("freew", "FreeW.App.Host", "FileCommands.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "MainWindow.cs");

        wpf.Should().Contain("OpenPathAsync(path, suppressRecentFiles)");
        wpf.Should().Contain("_documentWorkflow.ImportPdfTextPathAsync(");
        wpf.Should().Contain("_documentWorkflow.SaveCurrentPathAsync(");
        wpf.Should().Contain("SavePathAsync(path, filterIndex, kind)");
        avalonia.Should().Contain("_documentFileWorkflow.OpenPathAsync(");
        avalonia.Should().Contain("_documentFileWorkflow.ImportPdfTextPathAsync(");
        avalonia.Should().Contain("_documentFileWorkflow.SaveCurrentPathAsync(");
        avalonia.Should().Contain("_documentFileWorkflow.SavePathAsync(");
        wpf.Should().Contain("FreeWDocumentFileFeedbackPlanner.PlanOpen(");
        wpf.Should().Contain("FreeWDocumentFileFeedbackPlanner.PlanImport(");
        wpf.Should().Contain("FreeWDocumentFileFeedbackPlanner.PlanSave(");
        avalonia.Should().Contain("FreeWDocumentFileFeedbackPlanner.PlanOpen(");
        avalonia.Should().Contain("FreeWDocumentFileFeedbackPlanner.PlanImport(");
        avalonia.Should().Contain("FreeWDocumentFileFeedbackPlanner.PlanSave(");

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
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
