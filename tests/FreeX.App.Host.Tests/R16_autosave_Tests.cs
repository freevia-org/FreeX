using System.IO;
using System.IO.Compression;
using System.Windows;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for round-16 autosave/crash-recovery findings.
///
/// R16-autosave-recovery-deep-1: saving a document must invalidate the autosave snapshots of
/// EVERY window viewing that document (Excel "New Window" siblings each own an independent
/// per-window snapshot — see MainWindow.MultiWindow.cs / MainWindow.Autosave.cs), not just the
/// snapshot of the window that performed the save. Otherwise a later crash offers a sibling's
/// stale pre-save snapshot that could clobber the file the save just wrote.
///
/// R16-autosave-recovery-deep-2: cross-session recovery dedup (App.xaml.cs's
/// DeduplicateCandidatesByDocument / GetDocumentIdentityKey) must not silently delete an
/// older, never-recovered saved-path snapshot that came from a DIFFERENT launch/session than
/// the newer one — the two can hold different unsaved edits to the same file, and blindly
/// keeping only the newest destroys the older session's edits with zero content comparison.
/// </summary>
public sealed class R16_autosave_Tests
{
    private static MainWindow CreateWindow(
        WorkbookRef workbookRef,
        WorkbookWindowRegistry registry,
        WorkbookDocumentState documentState,
        ICommandBus commandBus,
        RecalcEngine recalcEngine,
        WorkbookSession? workbookSession = null)
    {
        var window = new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            commandBus,
            recalcEngine,
            [],
            workbookRef,
            workbookRef.Current,
            NullUserMessageService.Instance,
            documentState,
            windowRegistry: registry,
            workbookSession: workbookSession)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };

        return window;
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static int SnapshotFileCount(string recoveryDirectory) =>
        Directory.Exists(recoveryDirectory)
            ? Directory.GetFiles(recoveryDirectory, "*.fxl").Length
            : 0;

    [Fact]
    public void NotifyAutosaveSaved_InvalidatesSiblingWindowsSnapshotsForSameDocument()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R16.Recovery-");
        // MainWindow construction requires an STA thread (WPF), so run the whole scenario on the
        // shared STA harness the other WPF-host window tests use.
        StaTestRunner.Run(() =>
        {
            var store = new AutosaveSnapshotStore(temp.Path);

            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var registry = new WorkbookWindowRegistry();
            var documentState = new WorkbookDocumentState();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

            var primary = CreateWindow(workbookRef, registry, documentState, commandBus, recalcEngine);
            primary.AttachAutosaveService(new AutosaveService(store), store);
            primary.Show();
            primary.Activate();
            PumpDispatcher();

            // A "New Window" sibling over the same shared document — gets its own independent
            // autosave snapshot (per J25), just like MultiWindowAutosaveOwnershipTests exercises.
            var secondary = CreateWindow(
                workbookRef,
                registry,
                documentState,
                commandBus,
                recalcEngine,
                primary.Session.CreateSiblingView(1, 1));
            secondary.AttachAutosaveService(new AutosaveService(store), store);
            secondary.Show();
            secondary.Activate();
            PumpDispatcher();

            try
            {
                registry.Count.Should().Be(2);

                documentState.MarkDirty();
                primary.AutosaveServiceForCrashHandler!.OnTimerTick();
                secondary.AutosaveServiceForCrashHandler!.OnTimerTick();

                // Both windows have produced their own pre-save snapshot.
                SnapshotFileCount(temp.Path).Should().Be(2);

                // The primary window performs a clean save. This must invalidate not only its own
                // snapshot but also the secondary sibling's — the sibling's snapshot still reflects
                // the pre-save content and must not survive to be offered on a later crash.
                primary.NotifyAutosaveSaved();

                SnapshotFileCount(temp.Path).Should().Be(0,
                    "saving must delete every sibling view's autosave snapshot for the same document, " +
                    "not just the saving window's own snapshot");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(secondary);
                MainWindowTestCleanup.CloseWithoutSavePrompt(primary);
                PumpDispatcher();
            }
        });
    }

    // Real snapshots are OPC/ZIP packages; EnumerateCandidates validates that, so test snapshots
    // must be readable archives (matching AutosaveSnapshotStoreTests' WriteSnapshotZip pattern).
    private static void WriteSnapshotZip(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        zip.CreateEntry("[Content_Types].xml");
    }

    private static AutosaveRecoveryCandidate WriteCandidate(
        AutosaveSnapshotStore store,
        string snapshotId,
        string? originalFilePath,
        string? displayName,
        DateTimeOffset timestamp,
        string? documentId = null)
    {
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        var sidecarPath = store.GetSidecarPath(snapshotId);
        WriteSnapshotZip(snapshotPath);
        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = originalFilePath,
            DisplayName = displayName,
            TimestampUtc = timestamp.ToString("O"),
            SnapshotId = snapshotId,
            DocumentId = documentId
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
        return new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);
    }

    private static IReadOnlyList<AutosaveRecoveryCandidate> InvokeDeduplicate(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates)
    {
        var method = typeof(App).GetMethod(
            "DeduplicateCandidatesByDocument",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull();

        return (IReadOnlyList<AutosaveRecoveryCandidate>)method!.Invoke(null, [candidates])!;
    }

    [Fact]
    public void Deduplicate_SamePathFromDifferentLaunchScopes_KeepsBothInsteadOfDeletingOlder()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R16.Recovery-");
        var store = new AutosaveSnapshotStore(temp.Path);
        var now = DateTimeOffset.UtcNow;

        // Two DIFFERENT crashed sessions ("recovery-{processId}-{windowTag}") both left an
        // unrecovered snapshot for the SAME saved file path — e.g. session 1001 crashed with
        // unsaved edits, then a later, unrelated launch (2002) reopened the same file, made
        // DIFFERENT unsaved edits, and also crashed before either snapshot was ever offered.
        // These hold different unsaved edits to the same underlying file and must not be
        // collapsed into "keep the newer, silently delete the older".
        var olderDifferentSession = WriteCandidate(
            store, "recovery-1001-w0", @"C:\Users\alice\Report.fxl", "Report", now.AddMinutes(-30));
        var newerDifferentSession = WriteCandidate(
            store, "recovery-2002-w0", @"C:\Users\alice\Report.fxl", "Report", now);

        var deduped = InvokeDeduplicate([olderDifferentSession, newerDifferentSession]);

        deduped.Should().HaveCount(2,
            "unrecovered snapshots of the same saved path from different launch scopes hold " +
            "potentially divergent unsaved edits and must both be offered, not silently merged");
        File.Exists(olderDifferentSession.SnapshotPath).Should().BeTrue(
            "the older snapshot from a different session must not be destructively deleted " +
            "just because a newer snapshot for the same path exists from another launch");
        File.Exists(newerDifferentSession.SnapshotPath).Should().BeTrue();
    }

    [Fact]
    public void Deduplicate_SamePathFromSameLaunchScope_SameDocumentId_StillCollapsesToNewest()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R16.Recovery-");
        var store = new AutosaveSnapshotStore(temp.Path);
        var now = DateTimeOffset.UtcNow;

        // Two "New Window" siblings from the SAME crashed session/launch ("1001") over the same
        // saved document — proved by sharing the same DocumentId (both windows wrap the SAME
        // Workbook instance; see IAutosaveWorkbookSource.DocumentId) — is the legitimate
        // same-document, same-session case that must still collapse to a single (newest)
        // snapshot.
        var older = WriteCandidate(
            store, "recovery-1001-w0", @"C:\Users\alice\Report.fxl", "Report", now.AddMinutes(-1),
            documentId: "shared-report-workbook-id");
        var newer = WriteCandidate(
            store, "recovery-1001-w1", @"C:\Users\alice\Report.fxl", "Report", now,
            documentId: "shared-report-workbook-id");

        var deduped = InvokeDeduplicate([older, newer]);

        deduped.Should().ContainSingle(
            "sibling windows of the same document (same DocumentId) from the same crashed session still represent one document");
        deduped[0].SnapshotPath.Should().Be(newer.SnapshotPath);
        File.Exists(older.SnapshotPath).Should().BeFalse();
    }

    [Fact]
    public void Deduplicate_SamePathFromSameLaunchScope_DifferentDocumentId_KeepsBothInsteadOfDeleting()
    {
        // R82-services-autosave-recovery-5-1: File > Open has no "already open elsewhere" check
        // (MainWindow.Backstage.cs's OpenFileAsync), so the SAME running process/launch can have
        // TWO ORDINARY, INDEPENDENT windows over the same saved path (opened via two separate
        // File > Open actions), each with its own unrelated unsaved edits and its own Workbook
        // instance (different DocumentId). Before the fix, GetDocumentIdentityKey keyed purely on
        // launch scope + path, so these collapsed to one and DeduplicateCandidatesByDocument
        // silently deleted the older window's snapshot — permanently destroying its unsaved edits
        // with zero content comparison. They must now be kept as distinct candidates and both
        // offered, exactly like the different-launch-scope case above.
        using var temp = new TestTemporaryDirectory("FreeX.R16.Recovery-");
        var store = new AutosaveSnapshotStore(temp.Path);
        var now = DateTimeOffset.UtcNow;

        var windowA = WriteCandidate(
            store, "recovery-1001-a1b2c3d4-w0", @"C:\Users\alice\Budget.fxl", "Budget", now.AddMinutes(-1),
            documentId: "window-a-workbook-id");
        var windowB = WriteCandidate(
            store, "recovery-1001-a1b2c3d4-w1", @"C:\Users\alice\Budget.fxl", "Budget", now,
            documentId: "window-b-workbook-id");

        var deduped = InvokeDeduplicate([windowA, windowB]);

        deduped.Should().HaveCount(2,
            "two independent windows that merely happen to share a path/launch scope hold unrelated " +
            "unsaved edits and must both be offered, not silently merged");
        File.Exists(windowA.SnapshotPath).Should().BeTrue(
            "the older independent window's snapshot must not be destructively deleted just because " +
            "a newer snapshot for the same path exists from the same launch");
        File.Exists(windowB.SnapshotPath).Should().BeTrue();
    }

    [Fact]
    public void Deduplicate_SamePathFromSameLaunchScope_MissingDocumentId_KeepsBothInsteadOfDeleting()
    {
        // No-regression sibling: a snapshot written by a build that predates the DocumentId field
        // (or any other source that never populates it) must never be treated as provably the same
        // document as another candidate purely on launch scope + path — GetDocumentIdentityComponent
        // falls back to the (unique) snapshot path in that case, so these are kept distinct too.
        using var temp = new TestTemporaryDirectory("FreeX.R16.Recovery-");
        var store = new AutosaveSnapshotStore(temp.Path);
        var now = DateTimeOffset.UtcNow;

        var older = WriteCandidate(
            store, "recovery-1001-w0", @"C:\Users\alice\Report.fxl", "Report", now.AddMinutes(-1));
        var newer = WriteCandidate(
            store, "recovery-1001-w1", @"C:\Users\alice\Report.fxl", "Report", now);

        var deduped = InvokeDeduplicate([older, newer]);

        deduped.Should().HaveCount(2,
            "candidates with no DocumentId are never provably the same document, so they must not be merged");
        File.Exists(older.SnapshotPath).Should().BeTrue();
        File.Exists(newer.SnapshotPath).Should().BeTrue();
    }
}
