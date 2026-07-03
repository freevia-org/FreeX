using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookFileDialogParityCaptureSourceTests
{
    [Fact]
    public void ParityCapture_RendersWorkbookOpenAndSaveAsDialogSurfacesFromSharedPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("CaptureWorkbookFileDialogSurface(results, \"dialog.OpenWorkbook\", outDir");
        source.Should().Contain("CaptureWorkbookFileDialogSurface(results, \"dialog.SaveAsWorkbook\", outDir");
        source.Should().Contain("WorkbookFileDialogSurfacePlanner.CreateOpenPlan");
        source.Should().Contain("WorkbookFileDialogSurfacePlanner.CreateSaveAsPlan");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, plan.DialogAutomationId);");
        source.Should().Contain("CreateWorkbookFileDialogSurfaceContent(plan)");
        source.Should().Contain("RenderWorkbookFileDialogSurface(planFactory())");
        source.Should().Contain("RenderWorkbookFileDialogContent(content)");
        source.Should().Contain("WorkbookFileDialogSurfacePlanner.Width");
        source.Should().Contain("WorkbookFileDialogSurfacePlanner.Height");
        source.Should().Contain("RenderDialog(dialog, width, height)");
        source.Should().Contain("RenderElementOnBackground(content, width, height, Brushes.White)");
        source.Should().Contain("HasVisiblePixels(bitmap)");
        source.Should().Contain("refusing to record stale WPF parity-capture evidence");
    }
}
