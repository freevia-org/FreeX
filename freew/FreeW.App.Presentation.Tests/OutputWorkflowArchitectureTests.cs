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
        source.Should().Contain("AtomicFileWriter.ReplaceTarget(");
        source.Should().Contain("public static class FreeWPrintRequestPlanner");
        source.Should().Contain("public sealed class FreeWPortablePrintWorkflow");
        source.Should().Contain("Func<Stream, PrintSelection, CancellationToken, ValueTask> renderPdfAsync");
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
        var parityPlan = ReadSource("docs", "planning", "freew-avalonia-parity-plan.md");

        wpf.Should().Contain("FreeWExportWorkflow.CreatePlan(");
        wpf.Should().Contain("FreeWExportWorkflow.ExecuteAsync(");
        wpf.Should().Contain("FreeWPrintRequestPlanner.Create(");
        wpf.Should().Contain("FreeWPrintRequestPlanner.ResolvePageRange(");
        avalonia.Should().Contain("FreeWExportWorkflow.CreatePlan(");
        avalonia.Should().Contain("FreeWExportWorkflow.ExecuteAsync(");
        avalonia.Should().Contain("_portablePrintWorkflow.ExecuteAsync(");
        avalonia.Should().Contain("_portablePrintWorkflow.DiscoverAsync(");
        avalonia.Should().Contain("FreeWPrintMessagePlanner.PlanCapability(");
        avalonia.Should().Contain("PlatformPrintServiceSelector.Select(");
        avalonia.Should().Contain("windowsFactory: static () => new WindowsPrintService()");
        avalonia.Should().Contain("cupsFactory: static () => new CupsPrintService()");
        parityPlan.Should().Contain("Direct printer selection is also closed:");
        parityPlan.Should().NotContain(
            "The remaining Backstage print gap is direct native printer selection in Avalonia");

        foreach (var renderer in new[] { wpf, avalonia })
        {
            renderer.Should().NotContain("AtomicFileWriter.CreateTempPath(");
            renderer.Should().NotContain("AtomicFileWriter.ReplaceTarget(");
            renderer.Should().NotContain("AtomicFileWriter.CleanupTempFile(");
        }

        avalonia.Should().NotContain("_printService.SubmitAsync(");
        avalonia.Should().NotContain("_printService.DiscoverAsync(");
        avalonia.Should().NotContain("FormatPrintDiscoveryStatus(");
        avalonia.Should().NotContain("FormatPrintSubmissionStatus(");
        avalonia.Should().NotContain("DirectPrintDeferredReason(");
        wpf.Should().NotContain("File.WriteAllBytes(temporaryPath");
        avalonia.Should().NotContain("File.Create(temporaryPath");

        ReadSource("freew", "FreeW.App.Host", "PdfExport.cs")
            .Should().NotContain("Save(DocumentPaginator paginator, string path");
        ReadSource("freew", "FreeW.App.Host", "XpsExport.cs")
            .Should().NotContain("Save(DocumentPaginator paginator, string path");
        avalonia.Should().NotContain("FreeWAvaloniaPdfExport.Save(_editor, path")
            .And.NotContain("FreeWAvaloniaXpsExport.Save(_editor, path");

        ReadSource("freew", "FreeW.App.Avalonia", "Pdf", "FreeWAvaloniaPdfExport.cs")
            .Should().Contain("Save(DocumentView view, Stream stream");
        ReadSource("freew", "FreeW.App.Avalonia", "Pdf", "FreeWAvaloniaXpsExport.cs")
            .Should().Contain("Save(DocumentView view, Stream stream");
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
