using FluentAssertions;
using Free.Shared.AppServices;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// r191 (backlog item 24): a failed <see cref="JsonSettingsStore{T}.Load"/> returns a fresh default
/// and records <c>LastError</c> -- which no caller in any of the three apps reads. An unreadable
/// settings file therefore came back as "empty", and the next ordinary save wrote that emptiness
/// over it. FreeW's Quick Parts gallery surfaced it: the library loads at startup and every
/// Save/Remove afterwards persists the whole in-memory set, so one corrupt file plus one saved
/// snippet destroyed every snippet the user had.
///
/// The store now keeps the unreadable file rather than requiring each caller to remember a check
/// none of them makes.
/// </summary>
public sealed class R191_JsonSettingsStoreQuarantineTests : IDisposable
{
    private sealed class Settings
    {
        public List<string> Items { get; set; } = [];
    }

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "freex-r191-" + Guid.NewGuid().ToString("N"));

    private string StorePath => Path.Combine(_dir, "settings.json");

    private string QuarantinePath => StorePath + ".unreadable";

    public R191_JsonSettingsStoreQuarantineTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Test cleanup only.
        }
    }

    [Fact]
    public void Save_AfterAnUnreadableLoad_KeepsTheOriginalFileAside()
    {
        const string original = "{ this is not valid json";
        File.WriteAllText(StorePath, original);

        var store = JsonSettingsStore<Settings>.ForPath(StorePath);
        var loaded = store.Load();

        loaded.Items.Should().BeEmpty("an unreadable file yields a fresh default");
        store.LastError.Should().NotBeNull();

        // The ordinary next action: the user adds one item and it is persisted.
        loaded.Items.Add("newly added");
        store.Save(loaded).Should().BeTrue();

        File.Exists(QuarantinePath).Should().BeTrue("the unreadable content must survive the save");
        File.ReadAllText(QuarantinePath).Should().Be(original);
    }

    [Fact]
    public void Save_AfterASuccessfulLoad_DoesNotQuarantineAnything()
    {
        File.WriteAllText(StorePath, """{ "Items": [ "kept" ] }""");

        var store = JsonSettingsStore<Settings>.ForPath(StorePath);
        var loaded = store.Load();
        loaded.Items.Should().Equal("kept");

        loaded.Items.Add("added");
        store.Save(loaded).Should().BeTrue();

        File.Exists(QuarantinePath).Should().BeFalse("nothing was lost, so nothing is set aside");
        JsonSettingsStore<Settings>.ForPath(StorePath).Load().Items.Should().Equal("kept", "added");
    }

    [Fact]
    public void Save_WithNoFileAtAll_DoesNotQuarantineAnything()
    {
        // A first run is not a corruption: Load reports no error for a missing file.
        var store = JsonSettingsStore<Settings>.ForPath(StorePath);
        var loaded = store.Load();

        store.LastError.Should().BeNull();
        loaded.Items.Add("first");
        store.Save(loaded).Should().BeTrue();

        File.Exists(QuarantinePath).Should().BeFalse();
    }

    [Fact]
    public void Save_FromASecondStoreOnTheSamePath_DoesNotOverwriteTheAlreadyRescuedCopy()
    {
        // r192. The rescue flag is per-INSTANCE, and several stores can exist against one path:
        // FreeW builds a fresh store for the Quick Parts gallery on every window, so opening two
        // windows over a corrupt file leaves both flagged. The second window's save then copied the
        // file as it stood by then -- the first window's freshly written, VALID content -- over the
        // rescued original, destroying exactly the bytes the rescue exists to keep.
        const string original = "{ the user's real data, unparseable";
        File.WriteAllText(StorePath, original);

        var first = JsonSettingsStore<Settings>.ForPath(StorePath);
        var second = JsonSettingsStore<Settings>.ForPath(StorePath);

        // Both load the same corrupt file before either saves, as two windows would.
        var firstSettings = first.Load();
        var secondSettings = second.Load();
        first.LastError.Should().NotBeNull();
        second.LastError.Should().NotBeNull();

        firstSettings.Items.Add("from window one");
        first.Save(firstSettings).Should().BeTrue();
        File.ReadAllText(QuarantinePath).Should().Be(original);

        secondSettings.Items.Add("from window two");
        second.Save(secondSettings).Should().BeTrue();

        File.ReadAllText(QuarantinePath).Should().Be(
            original,
            "the first rescue wins -- the second store must not copy the now-valid file over it");
    }

    [Fact]
    public void Save_QuarantinesOnceOnly_SoLaterSavesDoNotOverwriteTheRescuedCopy()
    {
        const string original = "{ corrupt";
        File.WriteAllText(StorePath, original);

        var store = JsonSettingsStore<Settings>.ForPath(StorePath);
        var loaded = store.Load();

        loaded.Items.Add("one");
        store.Save(loaded).Should().BeTrue();
        loaded.Items.Add("two");
        store.Save(loaded).Should().BeTrue();

        // The second save must not copy the now-valid file over the rescued original.
        File.ReadAllText(QuarantinePath).Should().Be(original);
        JsonSettingsStore<Settings>.ForPath(StorePath).Load().Items.Should().Equal("one", "two");
    }
}
