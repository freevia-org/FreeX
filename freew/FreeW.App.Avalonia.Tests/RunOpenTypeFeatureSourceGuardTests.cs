using System.IO;
using System.Linq;

namespace FreeW.App.Avalonia.Tests;

public sealed class RunOpenTypeFeatureSourceGuardTests
{
    [Fact]
    public void FormattedText_ConsumesSharedOpenTypeFeaturePlan()
    {
        var source = File.ReadAllText(RepositoryFile(
            "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        source.Should().Contain("RunOpenTypeFeaturePlanner.Build(fmt)");
        source.Should().Contain("formatted.SetFontFeatures(new FontFeatureCollection(");
        source.Should().Contain("featurePlan.AvaloniaFeatureSettings.Select(FontFeature.Parse)");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeW.slnx", RepositoryFile);
}
