using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class AutosaveSnapshotStoreTests
{
    // ── ShouldSnapshot ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, 0, -1, false)]   // not dirty → no snapshot
    [InlineData(true, 0, 0, false)]    // dirty but generation unchanged → no snapshot
    [InlineData(true, 1, 0, true)]     // dirty + new generation → snapshot
    [InlineData(true, 5, 3, true)]     // dirty + higher generation → snapshot
    [InlineData(false, 5, 3, false)]   // not dirty even if generation changed → no snapshot
    public void ShouldSnapshot_ReturnsExpected(bool dirty, int current, int lastSnapshot, bool expected)
    {
        AutosaveSnapshotStore.ShouldSnapshot(dirty, current, lastSnapshot)
            .Should().Be(expected);
    }

    // ── Path resolution ───────────────────────────────────────────────────────

    [Fact]
    public void GetSnapshotPath_ContainsRecoveryDirectoryAndId()
    {
        var store = new AutosaveSnapshotStore(@"C:\FreeX\Recovery");

        store.GetSnapshotPath("recovery-1234-w0")
            .Should().Be(@"C:\FreeX\Recovery\recovery-1234-w0.fxl");
    }

    [Fact]
    public void GetSidecarPath_ContainsRecoveryDirectoryAndId()
    {
        var store = new AutosaveSnapshotStore(@"C:\FreeX\Recovery");

        store.GetSidecarPath("recovery-1234-w0")
            .Should().Be(@"C:\FreeX\Recovery\recovery-1234-w0.sidecar.json");
    }

    [Fact]
    public void CreateDefault_UsesProvidedApplicationDataPathProvider()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var provider = new TestApplicationDataPathProvider(root);

        var store = AutosaveSnapshotStore.CreateDefault(provider);

        store.GetSnapshotPath("recovery-1234-w0").Should().Be(Path.Combine(
            root,
            AppStoragePathPlanner.ProductDirectoryName,
            AutosaveSnapshotStore.RecoveryDirectoryName,
            "recovery-1234-w0.fxl"));
    }

    // ── Sidecar serialization round-trip ──────────────────────────────────────

    [Fact]
    public void SerializeSidecar_ThenDeserialize_RoundTrips()
    {
        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = @"C:\Users\alice\budget.xlsx",
            DisplayName = "budget",
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            SnapshotId = "recovery-42-w0"
        };

        var json = AutosaveSnapshotStore.SerializeSidecar(sidecar);
        var restored = AutosaveSnapshotStore.TryDeserializeSidecar(json);

        restored.Should().NotBeNull();
        restored!.OriginalFilePath.Should().Be(sidecar.OriginalFilePath);
        restored.DisplayName.Should().Be(sidecar.DisplayName);
        restored.SnapshotId.Should().Be(sidecar.SnapshotId);
    }

    [Fact]
    public void TryDeserializeSidecar_ReturnsNullForEmpty()
    {
        AutosaveSnapshotStore.TryDeserializeSidecar("").Should().BeNull();
        AutosaveSnapshotStore.TryDeserializeSidecar("   ").Should().BeNull();
        AutosaveSnapshotStore.TryDeserializeSidecar(null!).Should().BeNull();
    }

    [Fact]
    public void TryDeserializeSidecar_ReturnsNullForCorruptJson()
    {
        AutosaveSnapshotStore.TryDeserializeSidecar("not-json{{{{").Should().BeNull();
    }

    [Fact]
    public void TryDeserializeSidecar_ToleratesMissingFields()
    {
        // Minimal valid JSON object — extra fields and missing optional fields should not throw.
        var json = """{"snapshotId":"x"}""";
        var sidecar = AutosaveSnapshotStore.TryDeserializeSidecar(json);

        sidecar.Should().NotBeNull();
        sidecar!.SnapshotId.Should().Be("x");
        sidecar.OriginalFilePath.Should().BeNull();
    }

    // ── Candidate enumeration ─────────────────────────────────────────────────

    [Fact]
    public void EnumerateCandidates_EmptyWhenDirectoryAbsent()
    {
        var store = new AutosaveSnapshotStore(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        store.EnumerateCandidates().Should().BeEmpty();
    }

    [Fact]
    public void EnumerateCandidates_ReturnsCandidateWithValidSidecar()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        const string snapshotId = "recovery-99-w0";
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        var sidecarPath = store.GetSidecarPath(snapshotId);

        // Write a minimal .fxl placeholder and a sidecar.
        File.WriteAllText(snapshotPath, "placeholder");
        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = @"C:\test.xlsx",
            DisplayName = "test",
            SnapshotId = snapshotId
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));

        var candidates = store.EnumerateCandidates();

        candidates.Should().HaveCount(1);
        candidates[0].Sidecar.OriginalFilePath.Should().Be(@"C:\test.xlsx");
        candidates[0].Sidecar.DisplayName.Should().Be("test");
    }

    [Fact]
    public void EnumerateCandidates_SkipsSnapshotWithMissingSidecar()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        File.WriteAllText(store.GetSnapshotPath("recovery-1-w0"), "placeholder");
        // No sidecar written.

        store.EnumerateCandidates().Should().BeEmpty();
    }

    [Fact]
    public void EnumerateCandidates_SkipsCorruptSidecar()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        const string snapshotId = "recovery-2-w0";
        File.WriteAllText(store.GetSnapshotPath(snapshotId), "placeholder");
        File.WriteAllText(store.GetSidecarPath(snapshotId), "corrupt{{json");

        store.EnumerateCandidates().Should().BeEmpty();
    }

    // ── Delete operations ─────────────────────────────────────────────────────

    [Fact]
    public void DeleteSnapshot_RemovesBothFiles()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        const string snapshotId = "recovery-3-w0";
        File.WriteAllText(store.GetSnapshotPath(snapshotId), "placeholder");
        File.WriteAllText(store.GetSidecarPath(snapshotId), "{}");

        store.DeleteSnapshot(snapshotId);

        File.Exists(store.GetSnapshotPath(snapshotId)).Should().BeFalse();
        File.Exists(store.GetSidecarPath(snapshotId)).Should().BeFalse();
    }

    [Fact]
    public void DeleteCandidate_RemovesBothFiles()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        const string snapshotId = "recovery-4-w0";
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        var sidecarPath = store.GetSidecarPath(snapshotId);

        File.WriteAllText(snapshotPath, "placeholder");
        var sidecar = new AutosaveSidecar { SnapshotId = snapshotId };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));

        var candidate = store.EnumerateCandidates().Single();
        AutosaveSnapshotStore.DeleteCandidate(candidate);

        File.Exists(snapshotPath).Should().BeFalse();
        File.Exists(sidecarPath).Should().BeFalse();
    }

    [Fact]
    public void DeleteSnapshot_DoesNotThrowWhenFilesAbsent()
    {
        var store = new AutosaveSnapshotStore(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        var act = () => store.DeleteSnapshot("recovery-nonexistent-w0");
        act.Should().NotThrow();
    }

    private sealed class TestApplicationDataPathProvider(string path) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => path;
    }
}
