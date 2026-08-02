using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R115-avalonia-startup-recovery-dedup-1: <c>App.OfferStartupRecoveryAsync</c> used to enumerate
/// every autosave recovery candidate, sort by timestamp, and unconditionally DELETE every candidate
/// but the single newest -- synchronously, before ever asking the user anything -- regardless of
/// whether the discarded candidates belonged to the same document or to a completely unrelated one
/// (see <c>MainWindow.WindowManagement.cs</c>'s <c>NewWindow()</c> plus <c>ReplaceSession</c> call
/// sites in <c>MainWindow.cs</c>: a sibling window can freely detach into its own independent
/// document via File &gt; Open / File &gt; New, each keeping its own autosave snapshot). The fix
/// ports the WPF host's <c>DeduplicateCandidatesByDocument</c>/<c>GetDocumentIdentityKey</c>
/// (App.xaml.cs) into Avalonia's <c>App.cs</c>: candidates are only ever collapsed when they
/// PROVABLY belong to the same document (matching <see cref="AutosaveSidecar.DocumentId"/>, i.e.
/// the same in-memory <c>Workbook.Id</c>); independent documents are each offered on their own
/// instead of being silently discarded.
///
/// These tests drive the REAL entry point (<c>App.OfferStartupRecoveryAsync</c>, invoked via
/// reflection since it is private) against REAL <see cref="AutosaveSnapshotStore"/>/
/// <see cref="AutosaveSnapshotCoordinator"/> types and a genuine modal <c>Window</c> recovery prompt
/// (<c>MainWindow.ShowRecoveryPromptAsync</c>'s <c>await dialog.ShowDialog(this)</c>) -- a real,
/// controllable async suspension point that the headless platform can drive via a synthetic key
/// press, mirroring <c>R68_OpenWorkbookBusyFlagTests</c>'s established pattern.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R115_StartupRecoveryDedupTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task R115_OfferStartupRecoveryAsync_IndependentDocumentsSharingADisplayName_AreEachOfferedNotSilentlyDiscarded()
    {
        await Session.Dispatch(async () =>
        {
            var tempDir = CreateTempRecoveryDirectory();
            var mainWindow = new MainWindow([]);
            try
            {
                mainWindow.Show();
                var store = new AutosaveSnapshotStore(tempDir);
                var now = DateTimeOffset.UtcNow;

                // Two INDEPENDENT documents (distinct DocumentId) from the same crashed process/
                // launch scope (see MainWindow.WindowManagement.cs's NewWindow(), which mints one
                // AvaloniaAutosaveCoordinator -- and therefore one snapshot -- per window even when
                // a sibling later detaches into its own unrelated document). Both still carry the
                // default "Book1" display name -- the worst case, since a naive name-based dedup
                // (ignoring DocumentId) would incorrectly collapse them into one.
                WriteRecoveryCandidate(store, "recovery-424242-aaaaaaaa-11111111", "doc-A", "Book1", now.AddMinutes(-10));
                WriteRecoveryCandidate(store, "recovery-424242-aaaaaaaa-22222222", "doc-B", "Book1", now);

                var before = store.EnumerateCandidates();
                before.Should().HaveCount(2, "both independent documents' snapshots must exist before recovery runs");

                var task = InvokeOfferStartupRecoveryAsync(mainWindow, store);

                await DrainInputAsync();

                // THE DEFECT: the old code deleted every candidate but the timestamp-newest
                // SYNCHRONOUSLY, before the very first user prompt was ever shown. Neither
                // independent document's snapshot may be gone yet.
                foreach (var candidate in before)
                {
                    File.Exists(candidate.SnapshotPath).Should().BeTrue(
                        $"document '{candidate.Sidecar.DocumentId}' must still be on disk -- it has not been " +
                        "offered/declined yet, so it must never be silently destroyed in favor of another " +
                        "unrelated document");
                }

                mainWindow.OwnedWindows.Should().ContainSingle("the first document's recovery prompt must be showing");
                DeclineDialog(mainWindow.OwnedWindows.Single());
                await DrainInputAsync();

                mainWindow.OwnedWindows.Should().ContainSingle(
                    "a SECOND, independent document must get its OWN recovery prompt instead of having been " +
                    "silently deleted before ever being offered");
                DeclineDialog(mainWindow.OwnedWindows.Single());
                await DrainInputAsync();

                await task;

                foreach (var candidate in before)
                {
                    File.Exists(candidate.SnapshotPath).Should().BeFalse(
                        "a declined candidate is deleted afterwards so it is never re-offered on the next launch");
                }
            }
            finally
            {
                mainWindow.AllowCloseWithoutDirtyPromptForParityCapture();
                mainWindow.Close();
                TryDeleteDirectory(tempDir);
            }

            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Sibling no-regression check: two snapshots that genuinely belong to the SAME document (same
    /// <see cref="AutosaveSidecar.DocumentId"/> -- e.g. two "New Window" siblings over one shared
    /// workbook, see <c>MainWindow.WindowManagement.cs</c>'s <c>NewWindow()</c>) must still collapse
    /// to a single offer of the newest snapshot, exactly as before this fix -- the fix must not
    /// regress into offering every raw file individually regardless of document identity.
    /// </summary>
    [Fact]
    public async Task R115_OfferStartupRecoveryAsync_SameDocumentSiblingSnapshots_StillCollapseToOneNewestOffer()
    {
        await Session.Dispatch(async () =>
        {
            var tempDir = CreateTempRecoveryDirectory();
            var mainWindow = new MainWindow([]);
            try
            {
                mainWindow.Show();
                var store = new AutosaveSnapshotStore(tempDir);
                var now = DateTimeOffset.UtcNow;

                // Same DocumentId -- genuine "New Window" siblings over one shared document.
                WriteRecoveryCandidate(store, "recovery-555-bbbbbbbb-11111111", "doc-X", "Book1", now.AddMinutes(-10));
                WriteRecoveryCandidate(store, "recovery-555-bbbbbbbb-22222222", "doc-X", "Book1", now);

                var before = store.EnumerateCandidates();
                before.Should().HaveCount(2);
                var olderSnapshotPath = before.OrderBy(c => c.Sidecar.TimestampUtc).First().SnapshotPath;
                var newerSnapshotPath = before.OrderBy(c => c.Sidecar.TimestampUtc).Last().SnapshotPath;

                var task = InvokeOfferStartupRecoveryAsync(mainWindow, store);

                await DrainInputAsync();

                // The older sibling snapshot is collapsed away immediately -- only one document
                // identity exists, so only its newest snapshot is ever offered.
                File.Exists(olderSnapshotPath).Should().BeFalse(
                    "same-document sibling snapshots must still collapse to the single newest one");
                File.Exists(newerSnapshotPath).Should().BeTrue("the newest same-document snapshot is the one offered");

                mainWindow.OwnedWindows.Should().ContainSingle("only ONE prompt for the single collapsed document identity");
                DeclineDialog(mainWindow.OwnedWindows.Single());
                await DrainInputAsync();

                await task;

                mainWindow.OwnedWindows.Should().BeEmpty("no further prompt follows once the single identity is resolved");
                File.Exists(newerSnapshotPath).Should().BeFalse("declined, so it is deleted and never re-offered");
            }
            finally
            {
                mainWindow.AllowCloseWithoutDirtyPromptForParityCapture();
                mainWindow.Close();
                TryDeleteDirectory(tempDir);
            }

            return true;
        }, CancellationToken.None);
    }

    private static Task InvokeOfferStartupRecoveryAsync(MainWindow mainWindow, AutosaveSnapshotStore store)
    {
        var method = typeof(App).GetMethod("OfferStartupRecoveryAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Task)method.Invoke(null, [mainWindow, store])!;
    }

    private static void DeclineDialog(global::Avalonia.Controls.Window dialog) =>
        // The dialog's own KeyDown handler closes (and disposes) it synchronously on Escape, so
        // only the press is raised here -- mirrors R68_OpenWorkbookBusyFlagTests.
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static string CreateTempRecoveryDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreeX.R115.Recovery." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    /// <summary>
    /// Writes a real snapshot + sidecar through the production <see cref="AutosaveSnapshotCoordinator"/>
    /// pipeline (atomic write, sidecar-then-snapshot ordering, etc. -- exactly what
    /// <c>AvaloniaAutosaveCoordinator</c> drives in the real app), then patches only the sidecar's
    /// timestamp (still via the real <see cref="AutosaveSidecar"/> DTO + <c>SerializeSidecar</c>) so
    /// tests can control recency deterministically without sleeping between writes.
    /// </summary>
    private static void WriteRecoveryCandidate(
        AutosaveSnapshotStore store,
        string snapshotId,
        string documentId,
        string displayName,
        DateTimeOffset timestampUtc)
    {
        var coordinator = new AutosaveSnapshotCoordinator(store, snapshotId);
        coordinator.TryEmergencySnapshot(new FakeSnapshotSource(documentId, displayName));

        var sidecarPath = store.GetSidecarPath(snapshotId);
        var sidecar = AutosaveSnapshotStore.TryDeserializeSidecar(File.ReadAllText(sidecarPath))!;
        sidecar.TimestampUtc = timestampUtc.ToString("O");
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
    }

    private sealed class FakeSnapshotSource(string documentId, string displayName) : IAutosaveSnapshotSource
    {
        public string? OriginalFilePath => null;
        public string DisplayName { get; } = displayName;
        public bool IsDirty => true;
        public int DirtyGeneration => 1;
        public string? DocumentId { get; } = documentId;

        public void WriteSnapshot(string snapshotPath) => File.WriteAllText(snapshotPath, "{}");
    }
}
