using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class SmartArtTransactionSourceTests
{
    [Fact]
    public void MainWindow_SmartArtAuthoringDoesNotCommitWhenNativeRefreshFails()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));

        source.Should().Contain(
            "private bool CommitSmartArtTextPaneMutation(");
        source.Should().Contain(
            "return LastSmartArtDrawingCacheRegenerationResult is { Applied: true };");
        source.Should().Contain(
            "allowCachedPackageEdit: true");
        source.Should().Contain(
            "Message = \"SmartArt native data or drawing cache refresh failed.\"");
        source.Should().Contain(
            "LastSmartArtTextPaneApplyResult = LastSmartArtTextPaneApplyResult with");
    }
}
