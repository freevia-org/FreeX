namespace Free.Shared.AppServices.Tests;

public sealed class CustomDictionaryFileStoreTests
{
    [Fact]
    public void Add_IsCaseInsensitiveAndKeepsTheFirstCasing()
    {
        var store = new CustomDictionaryFileStore(storePath: null);

        store.Add("Kubernetes").Should().BeTrue();
        store.Add("kubernetes").Should().BeFalse("custom dictionaries ignore case when matching words");
        store.Add("KUBERNETES").Should().BeFalse();

        store.Words.Should().Equal("Kubernetes");
        store.Contains("kUBERNETES").Should().BeTrue();
    }

    [Fact]
    public void LoadedFileIsDeduplicatedCaseInsensitivelyAndBlankLinesAreIgnored()
    {
        using var directory = new TestTemporaryDirectory("custom-dictionary-store-");
        var path = Path.Combine(directory.Path, CustomDictionaryFileStore.DefaultFileName);
        File.WriteAllLines(path, ["TEH", "teh", "", "  ", "Zed"]);

        var store = new CustomDictionaryFileStore(path);

        store.Words.Should().Equal("TEH", "Zed");
    }

    [Fact]
    public void Add_PersistsAndSurvivesARestart()
    {
        using var directory = new TestTemporaryDirectory("custom-dictionary-store-");
        var path = Path.Combine(directory.Path, CustomDictionaryFileStore.DefaultFileName);

        new CustomDictionaryFileStore(path).Add("gonna").Should().BeTrue();

        new CustomDictionaryFileStore(path).Contains("GONNA").Should().BeTrue();
    }

    [Theory]
    [InlineData("freew", "FreeW.App.Presentation", "Proofing", "CustomDictionaryStore.cs")]
    [InlineData("freep", "FreeP.App.Presentation", "", "PresentationCustomDictionaryStore.cs")]
    public void SisterStores_DeriveFromTheSharedStoreInsteadOfReimplementingIt(
        string app,
        string project,
        string folder,
        string file)
    {
        var parts = folder.Length == 0 ? new[] { app, project, file } : new[] { app, project, folder, file };
        var source = File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(parts));

        source.Should().Contain(": CustomDictionaryFileStore");
        source.Should().NotContain("AtomicLineSetStore");
        source.Should().NotContain("PlatformApplicationDataPathProvider");
    }
}
