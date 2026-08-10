using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class ExportRendererBoundarySourceTests
{
    [Fact]
    public void Renderers_ConsumeSharedExportAndBackstageCapturePolicy()
    {
        var avaloniaMain = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var avaloniaOptions = Read("src", "FreeX.App.Avalonia", "MainWindow.ExportOptions.cs");
        var avaloniaCapture = Read("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs");
        var wpfExport = Read("src", "FreeX.App.Host", "MainWindow.PrintExport.cs");
        var wpfCapture = Read("src", "FreeX.App.Host", "ParityCapture.cs");

        avaloniaMain.Should().Contain("WorkbookExportInteractionPlanner.CreateRequestPlan(");
        avaloniaMain.Should().Contain("WorkbookExportInteractionPlanner.CreateResultPlan(");
        avaloniaMain.Should().Contain("PortablePdfExportPlanner.TryApplyOptions(");
        avaloniaMain.Should().NotContain("private static ExportContentScope ToExportContentScope(");
        avaloniaMain.Should().NotContain("private static WorkbookExportPrintScope ToWorkbookExportPrintScope(");
        avaloniaOptions.Should().NotContain("TryPreparePortablePdfExportPlan(");
        avaloniaOptions.Should().NotContain("ApplyPageRangeToPortablePdfExportPlan(");

        wpfExport.Should().Contain("ExportFilePickerPlanner.FormatFromPdfXpsFilterIndex(");
        wpfExport.Should().Contain("WorkbookExportInteractionPlanner.CreateRequestPlan(");
        wpfExport.Should().Contain("WorkbookExportInteractionPlanner.CreateResultPlan(");

        avaloniaCapture.Should().Contain("FreeXBackstageCapturePlanner.Build(FreeXBackstageCaptureHost.Avalonia)");
        wpfCapture.Should().Contain("FreeXBackstageCapturePlanner.Build(FreeXBackstageCaptureHost.Wpf)");
        avaloniaCapture.Should().NotContain("private static readonly string[] ParityBackstageSurfaces");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(RepositoryFileLocator.Find(parts));
}
