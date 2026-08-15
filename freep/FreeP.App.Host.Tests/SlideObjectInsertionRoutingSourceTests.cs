using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class SlideObjectInsertionRoutingSourceTests
{
    [Fact]
    public void FreePRibbonCommandWorkflow_RoutesObjectInsertionThroughPlanner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var workflow = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "Ribbon",
            "FreePRibbonCommandWorkflow.cs"));
        var host = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "MainWindow.RibbonProfile.cs"));
        var adapter = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "MainWindow.AssetImports.cs"));
        var importWorkflow = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationAssetImportWorkflow.cs"));

        workflow.Should().Contain("SlideObjectInsertionPlanner.BuiltInPlans");
        workflow.Should().Contain("plan.CommandId == SlideObjectInsertionPlanner.Table3x3CommandId");
        workflow.Should().Contain("FreePRibbonHostActionKind.InsertPicture");
        host.Should().Contain("QueueAssetImport(PresentationAssetImportKind.Picture)");
        adapter.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        adapter.Should().Contain("new PresentationAssetImportHostSession(");
        adapter.Should().Contain("new WpfPresentationAssetPickerPort(this)");
        adapter.Should().Contain("new WpfPresentationAssetReaderPort()");
        adapter.Should().Contain("new PresentationAssetImportExecutionCallbacks(");
        adapter.Should().Contain("AssetImportSession.ImportAsync(kind, applyZoomCoverImage)");
        adapter.Should().Contain("AssetImportSession.MaterializeOutcomeAsync(");
        importWorkflow.Should().Contain("SlideObjectInsertionPlanner.CreatePicturePayload(bytes, sourceName)");
        host.Should().NotContain("File.ReadAllBytes(");
        host.Should().NotContain("SlideObjectInsertionPlanner.CreatePicturePayload(");
        host.Should().NotContain("new Microsoft.Win32.OpenFileDialog");
        host.Should().NotContain("new OpenFileDialog");
        host.Should().NotContain(".ShowDialog()");
        workflow.Should().NotContain("editor.InsertDefaultTextBox(");
        workflow.Should().NotContain("editor.InsertDefaultRectangle(");
        workflow.Should().NotContain("editor.InsertDefaultEllipse(");
        workflow.Should().NotContain("editor.InsertPicture(");
        workflow.Should().NotContain("editor.InsertTable(");
        workflow.Should().NotContain("editor.InsertChart(");
    }

}
