using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class SlideObjectInsertionRoutingSourceTests
{
    [Fact]
    public void FreePRibbonCommands_RoutesObjectInsertionThroughPlanner()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "FreePRibbonCommands.cs"));

        source.Should().Contain("RegisterSlideObjectInsertionCommands(registry, editor, includePictureCommand: true, onTablePicker)");
        source.Should().Contain("SlideObjectInsertionPlanner.Table3x3CommandId && onTablePicker is not null");
        source.Should().Contain("SlideObjectInsertionPlanner.BuiltInPlans");
        source.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        source.Should().Contain("SlideObjectInsertionPlanner.CreatePicturePayload(bytes, result.FileName)");
        source.Should().NotContain("new Microsoft.Win32.OpenFileDialog");
        source.Should().NotContain("new OpenFileDialog");
        source.Should().NotContain(".ShowDialog()");
        source.Should().NotContain("editor.InsertDefaultTextBox(");
        source.Should().NotContain("editor.InsertDefaultRectangle(");
        source.Should().NotContain("editor.InsertDefaultEllipse(");
        source.Should().NotContain("editor.InsertPicture(");
        source.Should().NotContain("editor.InsertTable(");
        source.Should().NotContain("editor.InsertChart(");
    }

}
