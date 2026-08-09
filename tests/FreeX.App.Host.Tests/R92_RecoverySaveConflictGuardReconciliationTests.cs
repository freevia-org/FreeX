using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R92-meta-1: after crash recovery, <c>SetCurrentFilePathForRecovery</c>
/// (MainWindow.Autosave.cs) repoints <c>_currentFilePath</c> at the ORIGINAL file but, before the
/// fix, never reconciled <c>_currentFileSourceLastWriteTimeUtc</c> -- which was left holding the
/// write time captured from the SNAPSHOT file when <c>OpenRecoverySnapshotAsync</c> ran just
/// before it (see MainWindow.Backstage.cs's OpenFileAsync, which sets
/// <c>_currentFileSourceLastWriteTimeUtc = result.SourceLastWriteTimeUtc</c> from whatever path it
/// was given -- the snapshot path, in the recovery case).
///
/// The r91 save-conflict guard (MainWindow.Backstage.cs's SaveWorkbookToTargetAsync) then compares
/// <c>File.GetLastWriteTimeUtc(target.Path)</c> -- the ORIGINAL file's real on-disk time -- against
/// <c>_currentFileSourceLastWriteTimeUtc</c>. With the snapshot's write time left in that field,
/// the two are for DIFFERENT files and essentially never equal, so the guard fires on every save
/// of a recovered document, even when nobody touched the original file, popping the "modified by
/// someone else" warning (<c>ConfirmExternallyModifiedFileOverwrite</c>) on the ordinary
/// recover-then-save workflow. <c>NullUserMessageService.ShowMessage</c> always answers
/// <c>UserMessageResult.Ok</c> (mapped to <c>MessageBoxResult.OK</c>, never <c>Yes</c>), so when
/// the guard incorrectly fires, <c>ConfirmExternallyModifiedFileOverwrite</c> returns false and the
/// save is blocked -- giving these tests a deterministic true/false signal without any wall-clock
/// timing.
///
/// Exercises the REAL entry points: <c>OpenRecoverySnapshotAsync</c> (App.xaml.cs's startup
/// recovery call), <c>SetCurrentFilePathForRecovery</c> (the fix under test), and
/// <c>SaveWorkbookToTargetAsync</c> (the r91 guard's actual save path, reached via reflection only
/// because it is <c>private</c> -- there is no other seam into it from a test assembly).
/// </summary>
public sealed class R92_RecoverySaveConflictGuardReconciliationTests
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
    /// Runs an async delegate to completion on the shared STA dispatcher thread. Plain
    /// <c>.GetAwaiter().GetResult()</c> on that thread would deadlock the moment the delegate awaits
    /// something that resumes via the captured <c>DispatcherSynchronizationContext</c> (e.g. any
    /// awaited continuation posted back to this thread), because nothing would be pumping the
    /// dispatcher's queue while we block. Pushing a nested <see cref="DispatcherFrame"/> that exits
    /// once the task completes keeps the message loop actively pumping for the whole await chain,
    /// matching the pattern <see cref="PumpDispatcher"/> already uses for synchronous settle-down.
    /// </summary>
    private static void RunAsyncOnSta(Func<Task> asyncAction)
    {
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

            // Propagate any exception (and pick up the final result/state) now that the task is done.
            task.GetAwaiter().GetResult();
        });
    }

    private static void WriteNativeWorkbook(string path, string sheetName)
    {
        var workbook = new Workbook(System.IO.Path.GetFileNameWithoutExtension(path));
        workbook.AddSheet(sheetName);
        var adapter = new NativeJsonAdapter();
        using var stream = File.Create(path);
        adapter.Save(workbook, stream);
    }

    private static async Task<bool> InvokeSaveWorkbookToTargetAsync(MainWindow window, FileSaveTarget target)
    {
        var method = typeof(MainWindow).GetMethod(
            "SaveWorkbookToTargetAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("SaveWorkbookToTargetAsync is the real r91 save-conflict-guard entry point");

        var task = (Task<bool>)method!.Invoke(window, [target])!;
        return await task;
    }

    /// <summary>
    /// The primary regression scenario: recover a snapshot into a window (capturing the
    /// SNAPSHOT's write time), repoint the current file path at the ORIGINAL file via
    /// <c>SetCurrentFilePathForRecovery</c>, then save straight back to that original path with
    /// NOTHING having touched it externally in between. Before the fix, the stale
    /// snapshot-vs-original comparison always mismatches and the save is blocked by the spurious
    /// "externally modified" guard. After the fix, the expected write time is re-captured from the
    /// original file at recovery time, so the save proceeds untouched.
    /// </summary>
    [Fact]
    public void RecoverThenSaveToOriginalPath_WithNoExternalChange_SavesWithoutFalseConflictWarning()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R92.Recovery-");
        var originalPath = System.IO.Path.Combine(temp.Path, "Original.fxl");
        var snapshotPath = System.IO.Path.Combine(temp.Path, "Snapshot.fxl");

        // The original file as it sat on disk when FreeX crashed (a prior manual save).
        WriteNativeWorkbook(originalPath, "OriginalSheet");
        // The crash snapshot: a different file, inevitably written/stamped at a different instant
        // than the original -- this is the "two different files" mismatch the finding describes.
        WriteNativeWorkbook(snapshotPath, "SnapshotSheet");
        File.SetLastWriteTimeUtc(snapshotPath, DateTime.UtcNow.AddMinutes(-1));

        var adapters = new IFileAdapter[] { new NativeJsonAdapter() };
        var saved = false;

        RunAsyncOnSta(async () =>
        {
            var window = CreateWindow(adapters);
            window.Show();
            window.Activate();
            PumpDispatcher();

            try
            {
                // Real entry points, in the same order App.xaml.cs's OfferStartupRecovery uses them.
                await window.OpenRecoverySnapshotAsync(snapshotPath);
                window.SetCurrentFilePathForRecovery(originalPath);
                window.MarkWorkbookDirtyForRecovery();

                saved = await InvokeSaveWorkbookToTargetAsync(
                    window,
                    new FileSaveTarget(originalPath, new NativeJsonAdapter()));
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });

        saved.Should().BeTrue(
            "the original file was never touched externally after recovery, so the save-conflict " +
            "guard must not treat the unrelated snapshot's write time as a mismatch and block the save");
    }

    /// <summary>
    /// No-regression sibling: the guard must still fire when the original file GENUINELY changed
    /// on disk after recovery (e.g. another FreeX/Excel instance, or a colleague on a shared
    /// drive, wrote to it) -- the fix must reconcile the expected write time to the original's
    /// state AT RECOVERY TIME, not disable the guard altogether.
    /// </summary>
    [Fact]
    public void RecoverThenSaveToOriginalPath_WithGenuineExternalChangeAfterRecovery_StillWarns()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R92.Recovery-");
        var originalPath = System.IO.Path.Combine(temp.Path, "Original.fxl");
        var snapshotPath = System.IO.Path.Combine(temp.Path, "Snapshot.fxl");

        WriteNativeWorkbook(originalPath, "OriginalSheet");
        WriteNativeWorkbook(snapshotPath, "SnapshotSheet");
        File.SetLastWriteTimeUtc(snapshotPath, DateTime.UtcNow.AddMinutes(-1));

        var adapters = new IFileAdapter[] { new NativeJsonAdapter() };
        var saved = true;

        RunAsyncOnSta(async () =>
        {
            var window = CreateWindow(adapters);
            window.Show();
            window.Activate();
            PumpDispatcher();

            try
            {
                await window.OpenRecoverySnapshotAsync(snapshotPath);
                window.SetCurrentFilePathForRecovery(originalPath);
                window.MarkWorkbookDirtyForRecovery();

                // Someone else writes to the ORIGINAL file after recovery captured its write time.
                // File writes can share timer-tick resolution on some filesystems, so pin the new
                // write time explicitly rather than relying on wall-clock elapsed time.
                WriteNativeWorkbook(originalPath, "SomeoneElsesEdit");
                File.SetLastWriteTimeUtc(originalPath, DateTime.UtcNow.AddMinutes(5));

                saved = await InvokeSaveWorkbookToTargetAsync(
                    window,
                    new FileSaveTarget(originalPath, new NativeJsonAdapter()));
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });

        saved.Should().BeFalse(
            "the original file genuinely changed on disk after recovery, so the guard must still " +
            "warn instead of silently overwriting someone else's edit");
    }
}
