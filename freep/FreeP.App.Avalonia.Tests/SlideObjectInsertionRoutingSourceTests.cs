using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SlideObjectInsertionRoutingSourceTests
{
    [Fact]
    public void MainWindow_RoutesObjectInsertionThroughPlanner()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));

        source.Should().Contain("foreach (var plan in SlideObjectInsertionPlanner.BuiltInPlans)");
        source.Should().Contain("if (plan.RequiresPicturePayload)");
        source.Should().Contain("SlideObjectInsertionPlanner.Apply(Editor, plan)");
        source.Should().NotContain("Editor.InsertDefaultTextBox(");
        source.Should().NotContain("Editor.InsertDefaultRectangle(");
        source.Should().NotContain("Editor.InsertDefaultEllipse(");
        source.Should().NotContain("Editor.InsertTable(");
        source.Should().NotContain("Editor.InsertChart(");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
