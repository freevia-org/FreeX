namespace FreeP.App.Avalonia.Tests;

public sealed class AvaloniaOleInPlaceHostCommitCallbackTests
{
    [Fact]
    public void NativeHost_UsesPresentationCommitCallbacks_ForSlideAndInlinePayloads()
    {
        string root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        string source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "AvaloniaOleInPlaceHost.cs"));

        source.Should().Contain("OleActivationService.BuildOleObjectUpdateCallback")
            .And.Contain("OleActivationService.BuildInlineOleObjectUpdateCallback")
            .And.NotContain("BuildCommitCallback")
            .And.NotContain("EmbeddedBytes = bytes");
    }
}
