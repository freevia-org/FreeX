using FluentAssertions;
using FreeX.Core.IO;

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
        source.Should().Contain("CreateWorkbookFileDialogSurface(plan)");
        source.Should().Contain("WindowStartupLocation.Manual");
        source.Should().Contain("RenderDialog(");
        source.Should().Contain("WorkbookFileDialogSurfacePlanner.Width");
        source.Should().Contain("WorkbookFileDialogSurfacePlanner.Height");
        source.Should().Contain("RenderDialog(dialog, width, height)");
        source.Should().Contain("RenderElementOnBackground(content, width, height, Brushes.White)");
        source.Should().Contain("HasVisiblePixels(bitmap)");
        source.Should().Contain("refusing to record stale WPF parity-capture evidence");
        source.Should().Contain("RenderWorkbookFileDialogSurfaceForTest");
        source.Should().Contain("HasVisiblePixelsForTest");
    }

    [Fact]
    public void ParityCapture_WorkbookFileDialogDirectSurfacesRenderNonblankAtPlannerSize()
    {
        StaTestRunner.Run(() =>
        {
            foreach (var (surfaceId, plan) in CreateWorkbookFileDialogSurfacePlans())
            {
                var bitmap = ParityCapture.RenderWorkbookFileDialogSurfaceForTest(plan);

                bitmap.PixelWidth.Should().Be((int)WorkbookFileDialogSurfacePlanner.Width, surfaceId);
                bitmap.PixelHeight.Should().Be((int)WorkbookFileDialogSurfacePlanner.Height, surfaceId);
                ParityCapture.HasVisiblePixelsForTest(bitmap).Should().BeTrue(surfaceId);
            }
        });
    }

    private static IEnumerable<(string SurfaceId, WorkbookFileDialogSurfacePlan Plan)> CreateWorkbookFileDialogSurfacePlans()
    {
        var adapters = WorkbookFileAdapterCatalog.CreateDefaultAdapters();
        var openFormats = adapters
            .SelectMany(adapter => adapter.Formats)
            .Where(format => format.CanOpen)
            .ToArray();
        var saveFormats = adapters
            .SelectMany(adapter => adapter.Formats)
            .Where(format => format.CanSave)
            .ToArray();

        yield return (
            "dialog.OpenWorkbook",
            WorkbookFileDialogSurfacePlanner.CreateOpenPlan(
                WorkbookFilePickerPlanner.BuildOpenPickerPlan(openFormats)));

        yield return (
            "dialog.SaveAsWorkbook",
            WorkbookFileDialogSurfacePlanner.CreateSaveAsPlan(
                WorkbookFilePickerPlanner.BuildSavePickerPlan(
                    saveFormats,
                    "ParityDemo",
                    fallbackDisplayName: "Book1",
                    preferredExtension: AppOptions.FreeXWorkbookDefaultFormat)));
    }
}
