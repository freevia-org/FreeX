namespace Free.Shared.AppServices.Tests;

/// <summary>
/// Round152 finding: TryWriteSnapshot used to overwrite the sidecar (with a fresh timestamp)
/// BEFORE calling <see cref="IAutosaveSnapshotSource.WriteSnapshot"/>. Since WriteSnapshot "May
/// throw" and the whole method is a swallow-everything best-effort operation, a mid-write
/// exception on any tick after the first left an on-disk sidecar claiming brand-new content while
/// the actual snapshot file still held the PRIOR successful tick's payload -- an internally
/// inconsistent pair that Document Recovery would surface verbatim (fresh timestamp, stale data)
/// with no warning. The fix writes the snapshot content first and only updates the sidecar after
/// that succeeds, so a thrown exception leaves both files exactly as they were after the last
/// successful tick.
/// </summary>
public sealed class AutosaveSnapshotCoordinatorWriteOrderTests
{
    private sealed class ThrowsOnDemandSource : IAutosaveSnapshotSource
    {
        private readonly string _payload;

        public ThrowsOnDemandSource(string payload)
        {
            _payload = payload;
        }

        public string? OriginalFilePath => @"C:\docs\test.fxl";
        public string DisplayName => "test";
        public bool IsDirty { get; set; } = true;
        public int DirtyGeneration { get; set; }
        public bool ShouldThrow { get; set; }

        public void WriteSnapshot(string snapshotPath)
        {
            if (ShouldThrow)
                throw new IOException("simulated transient disk failure");

            File.WriteAllText(snapshotPath, _payload);
        }
    }

    [Fact]
    public void TryWriteSnapshot_WhenContentWriteThrows_SidecarStaysMatchedToOldContent()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var store = new AutosaveSnapshotStore(root);
            using var coordinator = new AutosaveSnapshotCoordinator(store, "w0");

            // Tick 1: succeeds, produces a matched (snapshot, sidecar) pair.
            var source = new ThrowsOnDemandSource("{\"gen\":5}") { DirtyGeneration = 5 };
            coordinator.Snapshot(source);

            var snapshotPath = store.GetSnapshotPath("w0");
            var sidecarPath = store.GetSidecarPath("w0");
            File.Exists(snapshotPath).Should().BeTrue();
            File.Exists(sidecarPath).Should().BeTrue();

            var contentAfterFirstTick = File.ReadAllText(snapshotPath);
            var sidecarAfterFirstTick = File.ReadAllText(sidecarPath);

            // Tick 2: generation advances (new edits happened) but the content write fails.
            source.DirtyGeneration = 7;
            source.ShouldThrow = true;
            coordinator.Snapshot(source);

            // The snapshot content must be unchanged -- the failed write never touched it.
            File.ReadAllText(snapshotPath).Should().Be(contentAfterFirstTick,
                "a failed WriteSnapshot must not corrupt or blank the previously committed content");

            // BEFORE the fix, the sidecar was overwritten with a fresh timestamp even though the
            // write failed, producing a fresh-metadata/stale-payload mismatch. After the fix, the
            // sidecar must be byte-for-byte unchanged, because execution never reaches the sidecar
            // write when WriteSnapshot throws.
            File.ReadAllText(sidecarPath).Should().Be(sidecarAfterFirstTick,
                "the sidecar must not advance its timestamp for content that was never written");

            // A subsequent successful tick must still work normally and bring both files forward
            // together.
            source.ShouldThrow = false;
            var secondPayload = "{\"gen\":7}";
            var successSource = new ThrowsOnDemandSourceWrapper(source, secondPayload);
            coordinator.Snapshot(successSource);

            File.ReadAllText(snapshotPath).Should().Be(secondPayload);
            File.ReadAllText(sidecarPath).Should().NotBe(sidecarAfterFirstTick,
                "a successful tick must advance the sidecar to match the new content");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Wraps a ThrowsOnDemandSource so the successful "tick 3" can write different payload text
    // while reusing the same DirtyGeneration/IsDirty/OriginalFilePath/DisplayName plumbing.
    private sealed class ThrowsOnDemandSourceWrapper : IAutosaveSnapshotSource
    {
        private readonly ThrowsOnDemandSource _inner;
        private readonly string _payload;

        public ThrowsOnDemandSourceWrapper(ThrowsOnDemandSource inner, string payload)
        {
            _inner = inner;
            _payload = payload;
        }

        public string? OriginalFilePath => _inner.OriginalFilePath;
        public string DisplayName => _inner.DisplayName;
        public bool IsDirty => _inner.IsDirty;
        public int DirtyGeneration => _inner.DirtyGeneration;

        public void WriteSnapshot(string snapshotPath) => File.WriteAllText(snapshotPath, _payload);
    }

    /// <summary>
    /// Sibling no-regression case: the ordinary happy path (no exception) must still atomically
    /// advance BOTH the snapshot content and the sidecar together on every successful tick --
    /// the reordering must not leave the sidecar behind under normal operation.
    /// </summary>
    [Fact]
    public void TryWriteSnapshot_OnSuccess_AdvancesBothSnapshotAndSidecarTogether()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var store = new AutosaveSnapshotStore(root);
            using var coordinator = new AutosaveSnapshotCoordinator(store, "w0");

            var source = new ThrowsOnDemandSource("{\"gen\":1}") { DirtyGeneration = 1 };
            coordinator.Snapshot(source);

            var snapshotPath = store.GetSnapshotPath("w0");
            var sidecarPath = store.GetSidecarPath("w0");

            File.Exists(snapshotPath).Should().BeTrue();
            File.Exists(sidecarPath).Should().BeTrue();
            File.ReadAllText(snapshotPath).Should().Be("{\"gen\":1}");

            var candidates = store.EnumerateCandidates();
            candidates.Should().ContainSingle(c => c.SnapshotPath == snapshotPath,
                "a successful tick must leave a matched pair that EnumerateCandidates recognizes");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "FreeXAutosaveCoordinatorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
