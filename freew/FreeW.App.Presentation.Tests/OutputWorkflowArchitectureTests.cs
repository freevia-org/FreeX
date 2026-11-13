namespace FreeW.App.Presentation.Tests;

public sealed class OutputWorkflowArchitectureTests
{
    [Fact]
    public void SharedWorkflowOwnsExportPrintAndPreviewDecisions()
    {
        var source = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Shell",
            "FreeWOutputWorkflow.cs");

        source.Should().Contain("public static class FreeWExportWorkflow");
        source.Should().Contain("Func<Stream, CancellationToken, ValueTask<FreeWExportArtifact>> renderAsync");
        source.Should().Contain("new AtomicExportExecutor().ExecuteAsync<FreeWExportArtifact>(");
        source.Should().NotContain("AtomicFileWriter.CreateTempLease(");
        source.Should().NotContain("AtomicFileWriter.ReplaceTarget(");
        source.Should().Contain("public static class FreeWPrintRequestPlanner");
        source.Should().NotContain("FreeWPortablePrintWorkflow");
        source.Should().NotContain("FreeWPrintSelectionHandoffPlanner");
        source.Should().Contain("public static class FreeWPrintMessagePlanner");
        source.Should().Contain("public sealed class FreeWPrintPreviewSession");
        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia");
        source.Should().NotContain("DocumentView");
        source.Should().NotContain("DocumentViewer");
    }


    [Fact]
    public void PreviewWindowsUseSharedSessionForRendererNeutralState()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "PrintPreviewWindow.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "PrintPreviewDialog.cs");

        wpf.Should().Contain("new FreeWPrintPreviewSession(");
        avalonia.Should().Contain("new FreeWPrintPreviewSession(");
        avalonia.Should().NotContain("BackstagePrintPanePlanner.Build(");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
