using System.IO;
using System.Windows;
using System.Windows.Threading;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.Host;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R165-shared-autosave-recovery-F1: before this fix, App.xaml.cs's
/// <c>OfferStartupRecovery</c> wired <c>RestoreAsync</c> as three unconditional, sequential steps
/// (<c>OpenRecoverySnapshotAsync</c> -&gt; <c>SetCurrentFilePathForRecovery</c> -&gt;
/// <c>MarkWorkbookDirtyForRecovery</c>) with no check of any success signal between them. Because
/// <c>OpenRecoverySnapshotAsync</c> delegated straight to <c>OpenFileAsync</c>, which never threw
/// and never reported failure, a snapshot whose FULL load failed (corrupt content, a
/// SchemaVersion/MinimumReaderVersion mismatch after an auto-update, a transient file lock) still
/// left the target window repointed at the original file and marked dirty over its untouched
/// pre-existing (blank) session -- and <c>StartupRecoveryWorkflow.RestoreAndRetireCandidateAsync</c>
/// then deleted the ONE surviving copy of the crash-time edits in its <c>finally</c> block
/// regardless of whether anything actually loaded, since <c>RestoreAsync</c> never threw either.
///
/// These tests exercise the REAL production call sites: <c>App.RestoreRecoveryCandidateAsync</c>
/// (the method <c>OfferStartupRecovery</c>'s <c>RestoreAsync</c> delegate wires directly to) and,
/// through it, <c>MainWindow.OpenRecoverySnapshotAsync</c>. The full-flow test additionally drives
/// the shared <c>StartupRecoveryWorkflow.RunAsync</c> with the real
/// <c>AutosaveSnapshotStore.DeleteCandidate</c> retirement callback, matching
/// <c>OfferStartupRecovery</c>'s own wiring, to prove the candidate file genuinely survives on disk.
/// </summary>
public sealed class R165_FailedRecoveryPreservesSnapshotTests
{
    private static MainWindow CreateWindow(IEnumerable<IFileAdapter> fileAdapters)
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        var workbookRef = new WorkbookRef { Current = workbook };
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
        var window = new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            commandBus,
            new RecalcEngine(graph, evaluator),
            fileAdapters,
            workbookRef,
            workbookRef.Current,
            NullUserMessageService.Instance)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };

        return window;
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// Runs an async delegate to completion on the shared STA dispatcher thread -- matches
    /// R92_RecoverySaveConflictGuardReconciliationTests' helper of the same name/shape, needed
    /// because a plain <c>.GetAwaiter().GetResult()</c> on this thread would deadlock the moment the
    /// delegate awaits a continuation posted back via the captured
    /// <c>DispatcherSynchronizationContext</c>.
    /// </summary>
    private static T RunAsyncOnSta<T>(Func<Task<T>> asyncAction)
    {
        var result = default(T)!;
        StaTestRunner.Run(() =>
        {
            var task = asyncAction();
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                task.ContinueWith(
                    _ => frame.Continue = false,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.FromCurrentSynchronizationContext());
                Dispatcher.PushFrame(frame);
            }

            result = task.GetAwaiter().GetResult();
        });
        return result;
    }

    private static AutosaveRecoveryCandidate WriteFailingCandidate(string temp, string originalPath)
    {
        var snapshotPath = Path.Combine(temp, "Snapshot.fxl");
        var sidecarPath = Path.Combine(temp, "Snapshot.fxl.sidecar.json");
        // A .fxl snapshot resolves to NativeJsonAdapter by extension, but this content is not valid
        // JSON at all -- WorkbookFileWorkflow.OpenAsync's load catches the parse exception and
        // returns Outcome.Failed with Context: null (matching the finding's cited failure trigger:
        // ValidateSchemaHeader / NativeJsonAdapter throwing on a snapshot the app cannot fully read).
        File.WriteAllText(snapshotPath, "{ this is not valid json, the crash truncated the write");
        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = originalPath,
            DisplayName = "Book1",
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            SnapshotId = "recovery-165-failing"
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
        return new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);
    }

    /// <summary>
    /// The primary regression scenario, at the exact call site <c>OfferStartupRecovery</c> wires:
    /// a snapshot whose load fails must report failure, leave the window's pre-existing session
    /// completely untouched (not repointed at the original file, not marked dirty), and the ORIGINAL
    /// file on disk must never be at risk from a later Ctrl+S against a blank document wearing the
    /// original's name.
    /// </summary>
    [Fact]
    public void RestoreRecoveryCandidateAsync_WhenLoadFails_ReportsFailureAndLeavesWindowUntouched()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R165.Recovery-");
        var originalPath = Path.Combine(temp.Path, "Original.fxl");
        File.WriteAllText(originalPath, "unrelated pre-crash saved content");

        var adapters = new IFileAdapter[] { new NativeJsonAdapter() };
        var candidate = WriteFailingCandidate(temp.Path, originalPath);

        var (restored, currentFilePath, isDirty) = RunAsyncOnSta(async () =>
        {
            var window = CreateWindow(adapters);
            window.Show();
            window.Activate();
            PumpDispatcher();

            try
            {
                var ok = await App.RestoreRecoveryCandidateAsync(window, candidate);
                var source = (IAutosaveWorkbookSource)window;
                return (ok, source.CurrentFilePath, source.IsWorkbookDirty);
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });

        restored.Should().BeFalse("the snapshot content was unreadable, so the load genuinely failed");
        currentFilePath.Should().BeNull(
            "a failed load must not repoint CurrentFilePath at the original file -- doing so on a " +
            "still-blank window would make the next Ctrl+S silently overwrite Original.fxl");
        isDirty.Should().BeFalse(
            "a failed load must not mark the untouched, still-blank window dirty");

        // The original file on disk must be completely unaffected by the failed recovery attempt.
        File.ReadAllText(originalPath).Should().Be("unrelated pre-crash saved content");
    }

    /// <summary>
    /// End-to-end through the same shared workflow OfferStartupRecovery drives: a failed restore
    /// must not be retired. <c>StartupRecoveryWorkflow.RunAsync</c> is driven with the REAL
    /// <c>AutosaveSnapshotStore.DeleteCandidate</c> so this proves the snapshot+sidecar genuinely
    /// remain on disk afterward -- the file-system-level assertion the round's directive requires,
    /// not just an in-memory event trace.
    /// </summary>
    [Fact]
    public void OfferStartupRecoveryWiring_WhenLoadFails_LeavesSnapshotOnDisk()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R165.Recovery-");
        var originalPath = Path.Combine(temp.Path, "Original.fxl");
        File.WriteAllText(originalPath, "unrelated pre-crash saved content");

        var adapters = new IFileAdapter[] { new NativeJsonAdapter() };
        var candidate = WriteFailingCandidate(temp.Path, originalPath);

        var accepted = RunAsyncOnSta(async () =>
        {
            var window = CreateWindow(adapters);
            window.Show();
            window.Activate();
            PumpDispatcher();

            try
            {
                var host = new StartupRecoveryWorkflowHost<MainWindow>(
                    PrimaryTarget: window,
                    OfferAsync: (_, _) => ValueTask.FromResult(true),
                    CreateAdditionalTargetAsync: _ => throw new InvalidOperationException(
                        "only one candidate is offered in this test"),
                    RestoreAsync: (target, c, _) => App.RestoreRecoveryCandidateAsync(target, c),
                    ExecuteRestoreAsync: (operation, _) => new ValueTask(operation()),
                    DeleteCandidate: AutosaveSnapshotStore.DeleteCandidate);

                return await StartupRecoveryWorkflow.RunAsync([candidate], host);
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });

        accepted.Should().BeTrue("the offer was accepted even though the underlying load failed");
        File.Exists(candidate.SnapshotPath).Should().BeTrue(
            "a failed load must not let StartupRecoveryWorkflow delete the only surviving copy of " +
            "the crash-time edits");
        File.Exists(candidate.SidecarPath).Should().BeTrue();
    }

    /// <summary>
    /// No-regression sibling: a snapshot that DOES load successfully must still repoint
    /// CurrentFilePath at the original file, mark the window dirty, and be retired (deleted) --
    /// exactly the pre-fix behavior, just now correctly gated on an actual success signal instead of
    /// running unconditionally.
    /// </summary>
    [Fact]
    public void OfferStartupRecoveryWiring_WhenLoadSucceeds_RepointsMarksDirtyAndDeletesSnapshot()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R165.Recovery-");
        var originalPath = Path.Combine(temp.Path, "Original.fxl");
        var snapshotPath = Path.Combine(temp.Path, "Snapshot.fxl");
        var sidecarPath = Path.Combine(temp.Path, "Snapshot.fxl.sidecar.json");

        var adapters = new IFileAdapter[] { new NativeJsonAdapter() };

        var snapshotWorkbook = new Workbook("Snapshot");
        snapshotWorkbook.AddSheet("SnapshotSheet");
        using (var stream = File.Create(snapshotPath))
        {
            new NativeJsonAdapter().Save(snapshotWorkbook, stream);
        }
        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = originalPath,
            DisplayName = "Book1",
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            SnapshotId = "recovery-165-succeeding"
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
        var candidate = new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);

        var (accepted, currentFilePath, isDirty) = RunAsyncOnSta(async () =>
        {
            var window = CreateWindow(adapters);
            window.Show();
            window.Activate();
            PumpDispatcher();

            try
            {
                var host = new StartupRecoveryWorkflowHost<MainWindow>(
                    PrimaryTarget: window,
                    OfferAsync: (_, _) => ValueTask.FromResult(true),
                    CreateAdditionalTargetAsync: _ => throw new InvalidOperationException(
                        "only one candidate is offered in this test"),
                    RestoreAsync: (target, c, _) => App.RestoreRecoveryCandidateAsync(target, c),
                    ExecuteRestoreAsync: (operation, _) => new ValueTask(operation()),
                    DeleteCandidate: AutosaveSnapshotStore.DeleteCandidate);

                var ok = await StartupRecoveryWorkflow.RunAsync([candidate], host);
                var source = (IAutosaveWorkbookSource)window;
                return (ok, source.CurrentFilePath, source.IsWorkbookDirty);
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });

        accepted.Should().BeTrue();
        currentFilePath.Should().Be(originalPath, "a successful recovery must still repoint Save at the original file");
        isDirty.Should().BeTrue("a successful recovery must still mark the window dirty");
        File.Exists(candidate.SnapshotPath).Should().BeFalse("a successfully-restored candidate must still be retired");
        File.Exists(candidate.SidecarPath).Should().BeFalse();
    }
}
