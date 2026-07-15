using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class ShellStringCompositionSourceTests
{
    [Fact]
    public void AppComposition_InstallsResourceBackedSharedShellStringAdapters()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var composition = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "AppComposition.cs"));

        composition.Should().Contain("AppLocalization.InstallSharedSeams();");
        composition.Should().NotContain("StaticShellStrings.ForProductTitle");
        composition.Should().NotContain("DefaultBackstageStrings.Instance");
        composition.Should().NotContain("new FreePShellStrings");
        composition.Should().NotContain("new FreePBackstageStrings");
        File.Exists(Path.Combine(root, "freep", "FreeP.App.Host", "FreePShellStrings.cs")).Should().BeFalse();
    }

}
