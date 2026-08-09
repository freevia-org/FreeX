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
        source.Should().Contain("ManifestSchemaVersion: 1");
        source.Should().Contain("RequestedMode");
        source.Should().Contain("CaptureCount");
    }

    [Fact]
    public void WordParityPlanningDocs_DocumentRibbonShotManifestContract()
    {
        var source = File.ReadAllText(RepositoryFile(
            "docs",
            "planning",
            "freew-ms-word-parity-session-2026-06-21.md"));

        source.Should().Contain("freew/tools/FreeW.RibbonShot");
        source.Should().Contain("freew_ribbonshot_manifest.json");
        source.Should().Contain("schema version 1");
        source.Should().Contain("requested mode");
        source.Should().Contain("capture count");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(RepositoryFile);
}
