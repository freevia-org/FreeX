using System.IO;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

public sealed class HyperlinkDialogTests
{
    [Fact]
    public void HyperlinkDialog_UsesSharedPlannerForPolicy()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.App.Host",
            "HyperlinkDialog.cs"));

        source.Should().Contain("HyperlinkDialogPlanner.BuildInitialState(current)");
        source.Should().Contain("HyperlinkDialogPlanner.BuildResult(");
        source.Should().Contain("FocusField(validation.FocusField)");
        source.Should().NotContain("Uri.TryCreate");
        source.Should().NotContain("new Hyperlink { Url =");
        source.Should().NotContain("new Hyperlink { TargetSlideId =");
    }

    [Fact]
    public void HyperlinkDialogPlanner_RemainsPresentationOwned()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.App.Presentation",
            "HyperlinkDialogPlanner.cs"));

        source.Should().Contain("public static class HyperlinkDialogPlanner");
        source.Should().Contain("Uri.TryCreate");
        source.Should().Contain("new Hyperlink");
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
