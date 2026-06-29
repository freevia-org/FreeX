using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class ShellStringCompositionSourceTests
{
    [Fact]
    public void AppComposition_InstallsSharedShellStringAdapters()
    {
        var root = FindRepositoryRoot();
        var composition = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "AppComposition.cs"));

        composition.Should().Contain("ShellStrings.Current = StaticShellStrings.ForProductTitle(\"FreeP\")");
        composition.Should().Contain("BackstageStrings.Current = DefaultBackstageStrings.Instance");
        composition.Should().NotContain("new FreePShellStrings");
        composition.Should().NotContain("new FreePBackstageStrings");
        File.Exists(Path.Combine(root, "freep", "FreeP.App.Host", "FreePShellStrings.cs")).Should().BeFalse();
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
