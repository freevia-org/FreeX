using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class SmartArtTransactionSourceTests
{
    [Fact]
    public void PresentationSession_SmartArtAuthoringDoesNotCommitWhenNativeRefreshFails()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationSmartArtTextPaneSession.cs"));

        source.Should().Contain(
            "private bool CommitMutation(");
        source.Should().Contain(
            "return LastDrawingCacheRegenerationResult is { Applied: true };");
        source.Should().Contain(
            "allowCachedPackageEdit: true");
        source.Should().Contain(
            "Message = NativeRefreshFailureMessage");
        source.Should().Contain(
            "LastTextPaneApplyResult = LastTextPaneApplyResult with");
    }
}
