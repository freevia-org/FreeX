using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class RecentFileRegistrationServiceTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeX.RecentFileRegistrationServiceTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public void RegisterIfNeeded_SkipsSuppressedRecoveryPath()
    {
        var store = CreateStore();
        var snapshotPath = Path.Combine(_tempDir, "recovery.fxl");

        var result = RecentFileRegistrationService.RegisterIfNeeded(
            store,
            new RecentFileRegistrationRequest(snapshotPath, SuppressRecentFiles: true));

        result.Decision.Should().Be(RecentFileRegistration.Skip);
        result.Registered.Should().BeFalse();
        store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void RegisterIfNeeded_SuppressedPathDoesNotLoadStore()
    {
        var loaded = false;

        var result = RecentFileRegistrationService.RegisterIfNeeded(
            () =>
            {
                loaded = true;
                return CreateStore();
            },
            new RecentFileRegistrationRequest(
                Path.Combine(_tempDir, "recovery.fxl"),
                SuppressRecentFiles: true));

        result.Registered.Should().BeFalse();
        loaded.Should().BeFalse();
    }

    [Fact]
    public void RegisterIfNeeded_RegistersRealPathWithCap()
    {
        var store = CreateStore();
        var first = Path.Combine(_tempDir, "first.fxl");
        var second = Path.Combine(_tempDir, "second.fxl");

        RecentFileRegistrationService.RegisterIfNeeded(
            store,
            new RecentFileRegistrationRequest(first, MaxRecentEntries: 1));
        var result = RecentFileRegistrationService.RegisterIfNeeded(
            store,
            new RecentFileRegistrationRequest(second, MaxRecentEntries: 1));

        result.Should().Be(new RecentFileRegistrationResult(RecentFileRegistration.Register, Registered: true));
        store.Snapshot().Select(entry => entry.Path).Should().Equal(second);
    }

    [Fact]
    public void RegisterIfNeeded_PreservesBookmarkIdentity()
    {
        var store = CreateStore();
        var path = Path.Combine(_tempDir, "book.fxl");
        var identity = new WorkbookFileAccessIdentity(path, "bookmark-kind", "bookmark-payload");

        RecentFileRegistrationService.RegisterIfNeeded(
            store,
            new RecentFileRegistrationRequest(path, FileAccessIdentity: identity));

        store.Snapshot().Should().ContainSingle()
            .Which.FileAccessIdentity.Should().BeEquivalentTo(identity);
    }

    private RecentFilesStore CreateStore() =>
        RecentFilesStore.Load(Path.Combine(_tempDir, "recent.json"));
}
