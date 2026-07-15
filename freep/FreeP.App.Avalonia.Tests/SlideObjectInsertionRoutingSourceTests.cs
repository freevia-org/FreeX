using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SlideObjectInsertionRoutingSourceTests
{
    [Fact]
    public void MainWindow_RoutesObjectInsertionThroughPlanner()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));

        source.Should().Contain("foreach (var plan in SlideObjectInsertionPlanner.BuiltInPlans)");
        source.Should().Contain("if (plan.RequiresPicturePayload)");
        source.Should().Contain("InsertPictureFromFileAsync");
        source.Should().Contain("SlideObjectInsertionPlanner.Apply(Editor, plan)");
        source.Should().Contain("SlideObjectInsertionPlanner.CreatePicturePayload");
        source.Should().Contain("SlideObjectInsertionPlanner.ApplyCommand(");
        source.Should().Contain("SlideObjectInsertionPlanner.PictureCommandId");
        source.Should().NotContain("Editor.InsertDefaultTextBox(");
        source.Should().NotContain("Editor.InsertDefaultRectangle(");
        source.Should().NotContain("Editor.InsertDefaultEllipse(");
        source.Should().NotContain("Editor.InsertPicture(");
        source.Should().NotContain("Editor.InsertTable(");
        source.Should().NotContain("Editor.InsertChart(");
    }

}
