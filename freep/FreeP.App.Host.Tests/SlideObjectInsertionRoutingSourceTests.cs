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
            "FreePRibbonCommands.cs"));

        workflow.Should().Contain("SlideObjectInsertionPlanner.BuiltInPlans");
        workflow.Should().Contain("plan.CommandId == SlideObjectInsertionPlanner.Table3x3CommandId");
        workflow.Should().Contain("FreePRibbonHostActionKind.InsertPicture");
        host.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        host.Should().Contain("SlideObjectInsertionPlanner.CreatePicturePayload(File.ReadAllBytes(result.FileName), result.FileName)");
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
