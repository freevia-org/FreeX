using System;
using System.IO;
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
/// Regression coverage for R120-avalonia-startup-recovery-newer-original-1: the Avalonia shell's
/// startup recovery (<c>App.OfferStartupRecoveryAsync</c>) used to enumerate + dedup candidates
/// (<see cref="R115_StartupRecoveryDedupTests"/>) but never checked whether the candidate's
/// ORIGINAL on-disk file had been saved more recently than the crash snapshot itself. The WPF host
/// (<c>FreeX.App.Host.App.xaml.cs</c>'s <c>FilterCandidatesWithNewerOriginal</c>/
/// <c>IsOriginalNewerThanSnapshot</c>, covered on that shell by
/// <c>R74_RecoveryStaleOriginalSkipTests</c>) has always guarded against this: if the user saved the
/// document normally after the crash that produced the snapshot, offering that snapshot would let
/// the user unknowingly clobber their own newer manual save with stale recovered content -- Excel
/// never offers recovery in this situation. This fix ports the same
/// <c>FilterCandidatesWithNewerOriginal</c>/<c>IsOriginalNewerThanSnapshot</c> pair into Avalonia's
/// <c>App.cs</c> and wires it into <c>OfferStartupRecoveryAsync</c> right after
/// <c>DeduplicateCandidatesByDocument</c>, exactly like the WPF host's <c>OfferStartupRecovery</c>.
///
/// The first two facts drive the ported filter directly via reflection (mirroring
/// <c>R74_RecoveryStaleOriginalSkipTests</c>'s unit-level coverage). The third fact drives the REAL
/// entry point, <c>App.OfferStartupRecoveryAsync</c>, end to end against a genuine
/// <see cref="AutosaveSnapshotStore"/> to prove the filter is actually wired in -- not just present
/// and unreachable -- so a stale-original candidate is silently skipped without ever prompting the
/// user, matching <see cref="R115_StartupRecoveryDedupTests"/>'s harness pattern.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R120_RecoveryStaleOriginalSkipTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static IReadOnlyList<AutosaveRecoveryCandidate> InvokeFilter(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates) =>
        AutosaveRecoveryCandidateProcessor.FilterSupersededByNewerOriginal(candidates);

    private static AutosaveRecoveryCandidate WriteCandidate(
        AutosaveSnapshotStore store,
        string snapshotId,
        string? originalFilePath,
        string? displayName,
        DateTimeOffset timestamp)
    {
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        var sidecarPath = store.GetSidecarPath(snapshotId);
        // FreeX snapshots are plain JSON (NativeJsonAdapter.Save), not a ZIP/OPC package -- see
        // AutosaveSnapshotStore.IsReadableSnapshot's format-detection doc comment.
        File.WriteAllText(snapshotPath, "{}");
        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = originalFilePath,
            DisplayName = displayName,
            TimestampUtc = timestamp.ToString("O"),
            SnapshotId = snapshotId
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
        return new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);
    }

    [Fact]
    public void Filter_DropsCandidateWhoseOriginalWasSavedAfterTheSnapshot()
    {
        var tempDir = CreateTempRecoveryDirectory();
        try
        {
            var store = new AutosaveSnapshotStore(tempDir);
            var originalPath = Path.Combine(tempDir, "Book1.fxl");
            File.WriteAllText(originalPath, "newer-manual-save");

            var snapshotTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
            // The on-disk original was saved AFTER the crash snapshot was taken.
            File.SetLastWriteTimeUtc(originalPath, snapshotTimeUtc.AddMinutes(5).UtcDateTime);

            var stale = WriteCandidate(store, "recovery-120-1-w0", originalPath, "Book1", snapshotTimeUtc);

            var filtered = InvokeFilter([stale]);

            filtered.Should().BeEmpty(
                "the on-disk original is newer than the crash snapshot, so recovering it would clobber the newer manual save");
            File.Exists(stale.SnapshotPath).Should().BeFalse(
                "a superseded candidate is deleted, not left to be silently re-checked (and re-skipped) forever");
            File.Exists(stale.SidecarPath).Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Sibling no-regression check: a candidate whose original is missing (never saved), or whose
    /// on-disk original predates the snapshot, must still be offered exactly as before this fix --
    /// the filter must not overreach and start blocking recovery that was always legitimate.
    /// </summary>
    [Fact]
    public void Filter_KeepsCandidatesThatAreNotSupersededByANewerOriginal()
    {
        var tempDir = CreateTempRecoveryDirectory();
        try
        {
            var store = new AutosaveSnapshotStore(tempDir);
            var now = DateTimeOffset.UtcNow;

            var olderOriginalPath = Path.Combine(tempDir, "Book2.fxl");
            File.WriteAllText(olderOriginalPath, "stale-on-disk-copy");
            File.SetLastWriteTimeUtc(olderOriginalPath, now.AddMinutes(-30).UtcDateTime);
            var okCandidate = WriteCandidate(store, "recovery-120-2-w0", olderOriginalPath, "Book2", now);

            var missingOriginalPath = Path.Combine(tempDir, "DoesNotExist.fxl");
            var missingOriginalCandidate = WriteCandidate(store, "recovery-120-2-w1", missingOriginalPath, "Book3", now);

            var filtered = InvokeFilter([okCandidate, missingOriginalCandidate]);

            filtered.Should().HaveCount(2,
                "neither a snapshot newer than its on-disk original nor a candidate with no original on disk is superseded");
            File.Exists(okCandidate.SnapshotPath).Should().BeTrue();
            File.Exists(missingOriginalCandidate.SnapshotPath).Should().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Drives the REAL entry point end to end: with a stale-original candidate on disk,
    /// <c>App.OfferStartupRecoveryAsync</c> must never even prompt the user for it -- proving the
    /// filter is actually wired into the startup path, not merely present and unreachable.
    /// </summary>
    [Fact]
    public async Task R120_OfferStartupRecoveryAsync_StaleOriginalCandidate_IsSkippedWithoutPrompting()
    {
        await Session.Dispatch(async () =>
        {
            var tempDir = CreateTempRecoveryDirectory();
            var mainWindow = new MainWindow([]);
            try
            {
                mainWindow.Show();
                var store = new AutosaveSnapshotStore(tempDir);

                var originalPath = Path.Combine(tempDir, "Overwritten.fxl");
                File.WriteAllText(originalPath, "newer-manual-save");
                var snapshotTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
                File.SetLastWriteTimeUtc(originalPath, snapshotTimeUtc.AddMinutes(5).UtcDateTime);

                var stale = WriteCandidate(store, "recovery-777-cccccccc-11111111", originalPath, "Overwritten", snapshotTimeUtc);

                var method = typeof(App).GetMethod("OfferStartupRecoveryAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
                var task = (Task)method.Invoke(null, [mainWindow, store])!;

                await DrainInputAsync();

                // Capture whether a prompt appeared BEFORE dismissing anything: if a regression ever
                // removes the newer-original filter, a real modal recovery prompt appears here and
                // `await task` below would never complete without it being dismissed first -- so any
                // such prompt is declined immediately (safety net) so the test fails fast on the
                // assertion below instead of hanging the whole run on an un-dismissed modal dialog.
                var promptWasShown = mainWindow.OwnedWindows.Count > 0;
                if (promptWasShown)
                {
                    DeclineDialog(mainWindow.OwnedWindows.Single());
                    await DrainInputAsync();
                }

                await task;

                promptWasShown.Should().BeFalse(
                    "a candidate superseded by a newer manual save on disk must never be offered to the user at all");
                File.Exists(stale.SnapshotPath).Should().BeFalse(
                    "the superseded candidate is deleted so it is never re-checked on a future launch");
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

    private static void DeclineDialog(global::Avalonia.Controls.Window dialog) =>
        // The dialog's own KeyDown handler closes (and disposes) it synchronously on Escape, so
        // only the press is raised here -- mirrors R115_StartupRecoveryDedupTests/R68_OpenWorkbookBusyFlagTests.
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static string CreateTempRecoveryDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreeX.R120.Recovery." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
