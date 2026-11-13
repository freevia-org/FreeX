using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class RibbonShotManifestSourceTests
{
    [Fact]
    public void RibbonShot_WritesManifestForEveryRenderedEvidenceMode()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.RibbonShot", "Program.cs"));

        source.Should().Contain("freew_ribbonshot_manifest.json");
        source.Should().Contain("WriteManifest(outDir, tabArg, w, h, captures);");
        source.Should().Contain("RibbonShotCapture.Ribbon(");
        source.Should().Contain("RibbonShotCapture.Backstage(");
        source.Should().Contain("RibbonShotCapture.Dialog(");
        source.Should().Contain("RibbonShotCapture.Split(");
        source.Should().Contain("tabArg == \"split\"");
        source.Should().Contain("ManifestSchemaVersion: 1");
        source.Should().Contain("RequestedMode");
        source.Should().Contain("CaptureCount");
    }

    [Fact]
    public void RibbonShot_HonorsTheRequestedWindowSizeInsteadOfStartupMaximize()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.RibbonShot", "Program.cs"));

        source.Should().Contain("Width = w,");
        source.Should().Contain("Height = h,");
        source.Should().Contain("WindowState = WindowState.Normal,");
        source.Should().Contain("new RenderTargetBitmap((int)w, (int)h");
    }


    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
