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
        source.Should().Contain("ExportAtomicWriter.ReplaceTarget(");
        source.Should().Contain("public static class FreeWPrintRequestPlanner");
        source.Should().Contain("public sealed class FreeWPortablePrintWorkflow");
        source.Should().Contain("_printService.SubmitAsync(");
        source.Should().Contain("public static class FreeWPrintMessagePlanner");
        source.Should().Contain("public sealed class FreeWPrintPreviewSession");
        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia");
        source.Should().NotContain("DocumentView");
        source.Should().NotContain("DocumentViewer");
    }

    [Fact]
    public void RenderersDelegateOutputPlanningAndPortablePrintExecution()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "MainWindow.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "MainWindow.cs");

        wpf.Should().Contain("FreeWExportWorkflow.CreatePlan(");
        wpf.Should().Contain("FreeWExportWorkflow.ExecuteAsync(");
        wpf.Should().Contain("FreeWPrintRequestPlanner.Create(");
        wpf.Should().Contain("FreeWPrintRequestPlanner.ResolvePageRange(");
        avalonia.Should().Contain("FreeWExportWorkflow.CreatePlan(");
        avalonia.Should().Contain("FreeWExportWorkflow.ExecuteAsync(");
        avalonia.Should().Contain("_portablePrintWorkflow.ExecuteAsync(");
        avalonia.Should().Contain("FreeWPrintMessagePlanner.PlanCapability(");

        foreach (var renderer in new[] { wpf, avalonia })
        {
            renderer.Should().NotContain("ExportAtomicWriter.CreateTempPath(");
            renderer.Should().NotContain("ExportAtomicWriter.ReplaceTarget(");
            renderer.Should().NotContain("ExportAtomicWriter.CleanupTempFile(");
        }

        avalonia.Should().NotContain("_printService.SubmitAsync(");
        avalonia.Should().NotContain("FormatPrintDiscoveryStatus(");
        avalonia.Should().NotContain("FormatPrintSubmissionStatus(");
        avalonia.Should().NotContain("DirectPrintDeferredReason(");
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
