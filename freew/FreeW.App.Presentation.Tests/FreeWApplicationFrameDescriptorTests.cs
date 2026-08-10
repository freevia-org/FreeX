using Free.Shared.AppServices;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWApplicationFrameDescriptorTests
{
    [Fact]
    public void Portable_provider_projection_matches_shared_storage_label()
    {
        var provider = new StubApplicationDataPathProvider(Path.Combine("root", "local"));

        FreeWApplicationFrameDescriptor.ResolveDataFolderLabel(provider)
            .Should().Be(AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(provider));
    }

    [Fact]
    public void Options_store_projection_preserves_avalonia_directory_label()
    {
        var storePath = Path.Combine("root", "FreeW", "options.json");

        FreeWApplicationFrameDescriptor.ResolveDataFolderLabel(storePath)
            .Should().Be(Path.GetDirectoryName(storePath));
    }

    [Fact]
    public void Title_UsesSharedProductPolicy()
    {
        FreeWApplicationFrameDescriptor.Title.Should().Be(new ApplicationWindowTitleSpec(
            ApplicationName: "FreeW",
            DefaultDocumentDisplayName: "Untitled",
            DirtyMarker: " *",
            Separator: " \u2014 ",
            ApplicationPlacement: WindowTitleApplicationPlacement.DocumentThenApplication));
    }

    private sealed class StubApplicationDataPathProvider(string path) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => path;
    }
}
