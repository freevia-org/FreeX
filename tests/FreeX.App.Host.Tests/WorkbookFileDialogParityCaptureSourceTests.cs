using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookFileDialogParityCaptureSourceTests
{
    [Fact]
    public void ParityCapture_RendersWorkbookOpenAndSaveAsDialogSurfacesFromSharedPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("CaptureDialog(results, \"dialog.OpenWorkbook\", outDir");
        source.Should().Contain("CaptureDialog(results, \"dialog.SaveAsWorkbook\", outDir");
        source.Should().Contain("WorkbookFileDialogSurfacePlanner.CreateOpenPlan");
        source.Should().Contain("WorkbookFileDialogSurfacePlanner.CreateSaveAsPlan");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, plan.DialogAutomationId);");
    }
}
