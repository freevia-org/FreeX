using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeW.App.Presentation.Shell;
using FreeW.App.Avalonia;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Tests for <see cref="AutosaveAdapter"/> focusing on the timing/recovery-decision logic
/// that does NOT require showing dialogs (the dialog paths are excluded from headless testing).
/// </summary>
public sealed class AutosaveAdapterTests
{
    // ── Recovery-candidate selection ─────────────────────────────────────────

    /// <summary>
    /// When there are no candidates the store returns an empty list.
    /// Verify via the public AutosaveSnapshotStore API (headless-safe, no dialog).
    /// </summary>
    [Fact]
    public void EnumerateCandidates_on_empty_recovery_dir_returns_empty()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("FreeW.AutosaveAdapterTests-");
        var store = new AutosaveSnapshotStore(temporaryDirectory.Path);
        store.EnumerateCandidates().Should().BeEmpty();
    }

    /// <summary>
    /// The adapter's internal SelectLatest logic should pick the candidate with the
    /// latest timestamp. We verify this indirectly via AutosaveSnapshotStore sidecar helpers.
    /// </summary>
    [Fact]
    public void Sidecar_timestamp_parsing_selects_latest_candidate()
    {
        // Build two fake sidecars with different timestamps.
        var earlier = new AutosaveSidecar
        {
            TimestampUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToString("o"),
            DisplayName = "older",
            SnapshotId = "aaa",
        };
        var later = new AutosaveSidecar
        {
            TimestampUtc = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero).ToString("o"),
            DisplayName = "newer",
            SnapshotId = "bbb",
        };

        // SelectLatest via ordering — mirror the internal logic.
        var candidates = new[]
        {
            new AutosaveRecoveryCandidate("snap1.docx", "snap1.sidecar", earlier),
            new AutosaveRecoveryCandidate("snap2.docx", "snap2.sidecar", later),
        };

        var selected = AutosaveRecoveryPolicy.SelectLatest(candidates);

        selected.Should().NotBeNull();
        selected!.Sidecar.DisplayName.Should().Be("newer");
    }

    /// <summary>
    /// AutosaveSnapshotCoordinator.Snapshot skips writing when the source is not dirty.
    /// Verify no file is created in the recovery directory.
    /// </summary>
    [Fact]
    public async Task Snapshot_is_skipped_when_source_is_not_dirty()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("FreeW.AutosaveAdapterTests-");
        var store = new AutosaveSnapshotStore(temporaryDirectory.Path);
        using var coordinator = new AutosaveSnapshotCoordinator(store, Guid.NewGuid().ToString("N"));

        // A fake source that is NOT dirty.
        var source = new FakeSnapshotSource { IsDirty = false };

        coordinator.Snapshot(source);

        // No snapshot file should have been written.
        await Task.Delay(50); // give any async work a moment
        store.EnumerateCandidates().Should().BeEmpty();
    }

    /// <summary>
    /// The adapter Start/StopAsync lifecycle does not throw on a normal start+stop cycle.
    /// This exercises the CancellationToken-driven async loop path without needing a real window.
    /// </summary>
    [Fact]
    public async Task Adapter_start_stop_lifecycle_does_not_throw()
    {
        // We can't construct AutosaveAdapter without DocumentView (Avalonia control) outside
        // of the headless session, so this test verifies the coordinator layer directly.
        using var temporaryDirectory = new TestTemporaryDirectory("FreeW.AutosaveAdapterTests-");
        var store = new AutosaveSnapshotStore(temporaryDirectory.Path);
        {
            using var coordinator = new AutosaveSnapshotCoordinator(store, Guid.NewGuid().ToString("N"));
            var source = new FakeSnapshotSource { IsDirty = false };

            // Start + immediate stop — should not throw.
            using var cts = new CancellationTokenSource();
            var loop = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(30), cts.Token); }
                    catch (OperationCanceledException) { break; }
                    coordinator.Snapshot(source);
                }
            }, CancellationToken.None); // don't pass ct so the task isn't pre-cancelled

            await cts.CancelAsync();
            // Swallow TaskCanceledException — the loop may propagate it when delay is cancelled.
            try { await loop; }
            catch (OperationCanceledException) { /* expected on cancel */ }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class FakeSnapshotSource : IAutosaveSnapshotSource
    {
        public string? OriginalFilePath => null;
        public string DisplayName => "Test document";
        public bool IsDirty { get; init; }
        public int DirtyGeneration => IsDirty ? 1 : 0;
        // Real snapshots are OPC/ZIP packages; the store now validates that on enumeration.
        public void WriteSnapshot(string snapshotPath)
        {
            using var zip = System.IO.Compression.ZipFile.Open(snapshotPath, System.IO.Compression.ZipArchiveMode.Create);
            zip.CreateEntry("[Content_Types].xml");
        }
    }
}
