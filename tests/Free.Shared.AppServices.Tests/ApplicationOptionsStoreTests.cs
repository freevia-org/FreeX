namespace Free.Shared.AppServices.Tests;

using FreePOptions = FreeP.App.Compositor.FreePOptions;
using FreeWOptions = FreeW.App.Presentation.Options.FreeWOptions;

public sealed class ApplicationOptionsStoreTests
{
    [Fact]
    public void InMemoryStore_NormalizesAndSnapshotsAcrossSaveLoadBoundary()
    {
        var initial = new DummyOptions { RecentFilesCap = 999, Label = " initial " };
        IApplicationOptionsStore<DummyOptions> store =
            new InMemoryApplicationOptionsStore<DummyOptions>(initial);

        var firstLoad = store.Load();
        firstLoad.RecentFilesCap.Should().Be(ApplicationOptionsNormalizer.MaxRecentFilesCap);
        firstLoad.Label.Should().Be("initial");

        firstLoad.RecentFilesCap = 4;
        firstLoad.Label = " saved ";
        store.Save(firstLoad).Should().BeTrue();

        firstLoad.RecentFilesCap = 12;
        firstLoad.Label = "changed after save";
        var secondLoad = store.Load();

        secondLoad.RecentFilesCap.Should().Be(4);
        secondLoad.Label.Should().Be("saved");
        secondLoad.Should().NotBeSameAs(firstLoad);
        store.LastError.Should().BeNull();
    }

    [Fact]
    public void InMemoryStore_RoundTripsBothSisterAppModelsBySnapshot()
    {
        var freeWStore = new InMemoryApplicationOptionsStore<FreeWOptions>();
        var freeWOptions = new FreeWOptions { RecentFilesCap = 4, UiLanguage = "  en-us  " };
        freeWStore.Save(freeWOptions).Should().BeTrue();
        freeWOptions.RecentFilesCap = 9;

        var reloadedFreeW = freeWStore.Load();
        reloadedFreeW.RecentFilesCap.Should().Be(4);
        reloadedFreeW.UiLanguage.Should().Be("en-US");

        var freePStore = new InMemoryApplicationOptionsStore<FreePOptions>();
        var freePOptions = new FreePOptions { RecentFilesCap = 5, UiLanguage = "  uk-ua  " };
        freePStore.Save(freePOptions).Should().BeTrue();
        freePOptions.RecentFilesCap = 10;

        var reloadedFreeP = freePStore.Load();
        reloadedFreeP.RecentFilesCap.Should().Be(5);
        reloadedFreeP.UiLanguage.Should().Be("uk-UA");
    }

    [Fact]
    public void InMemoryStore_ProductFilePathRemainsLogicalAndCreatesNoResources()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"Free.Shared.AppServices.Tests-{Guid.NewGuid():N}");
        var storePath = Path.Combine(directory, ApplicationOptionsStore<DummyOptions>.DefaultFileName);
        var store = InMemoryApplicationOptionsStore<DummyOptions>.ForProductFile(
            overridePath: storePath);

        store.StorePath.Should().Be(storePath);
        store.Save(new DummyOptions { RecentFilesCap = 3 }).Should().BeTrue();

        File.Exists(storePath).Should().BeFalse();
        Directory.Exists(directory).Should().BeFalse();
    }

    private sealed class DummyOptions : INormalizableApplicationOptions
    {
        public int RecentFilesCap { get; set; } = ApplicationOptionsNormalizer.DefaultRecentFilesCap;

        public string Label { get; set; } = string.Empty;

        public void Normalize()
        {
            RecentFilesCap = ApplicationOptionsNormalizer.NormalizeRecentFilesCap(RecentFilesCap);
            Label = Label.Trim();
        }
    }
}
