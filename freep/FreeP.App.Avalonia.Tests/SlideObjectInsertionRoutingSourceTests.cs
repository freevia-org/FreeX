using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SlideObjectInsertionRoutingSourceTests
{
    [Fact]
    public void MainWindow_RoutesObjectInsertionThroughPlanner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));
        var workflow = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "Ribbon",
            "FreePRibbonCommandWorkflow.cs"));
        var assetWorkflow = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationAssetImportWorkflow.cs"));
        var adapter = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.AssetImports.cs"));

        workflow.Should().Contain("foreach (var plan in SlideObjectInsertionPlanner.BuiltInPlans)");
        workflow.Should().Contain("FreePRibbonHostActionKind.InsertPicture");
        source.Should().Contain("InsertPictureFromFileAsync");
        workflow.Should().Contain("SlideObjectInsertionPlanner.Apply(editor, plan)");
        source.Should().Contain("ImportPresentationAssetAsync(PresentationAssetImportKind.Picture)");
        assetWorkflow.Should().Contain("SlideObjectInsertionPlanner.CreatePicturePayload(bytes, sourceName)");
        assetWorkflow.Should().Contain("SlideObjectInsertionPlanner.ApplyCommand(");
        assetWorkflow.Should().Contain("SlideObjectInsertionPlanner.PictureCommandId");
        adapter.Should().Contain("new PresentationAssetImportExecutionPort(");
        source.Should().NotContain("PickSingleOpenFileAsync(");
        source.Should().NotContain("SlideObjectInsertionPlanner.CreatePicturePayload(");
        workflow.Should().NotContain("editor.InsertDefaultTextBox(");
        workflow.Should().NotContain("editor.InsertDefaultRectangle(");
        workflow.Should().NotContain("editor.InsertDefaultEllipse(");
        workflow.Should().NotContain("editor.InsertPicture(");
        workflow.Should().NotContain("editor.InsertTable(");
        workflow.Should().NotContain("editor.InsertChart(");
    }

}
