using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for the R115 finding: Save (Ctrl+S / Save As) serializes the LIVE
/// Workbook instance on a background thread (<see cref="Free.Shared.AppServices.WorkbookProgressStageRunner.RunStageAsync{T,TPhase,TProgressUpdate}"/>,
/// invoked from <c>WorkbookSaveService.SaveAsync</c>). Before the fix, <c>SaveWorkbookToTargetAsync</c>
/// (MainWindow.Backstage.cs) only disabled THIS window's own input via
/// <c>SetFileOperationInputEnabled(false)</c> -- an Excel "New Window" sibling sharing the exact
/// same Workbook instance (see <c>AdoptSharedWorkbook</c> in MainWindow.MultiWindow.cs) stayed
/// fully interactive for the whole background serialize, so a keystroke landing there while the
/// background thread enumerated the shared Sheet cell dictionaries could tear them structurally
/// mid-enumeration.
///
/// Exercises the REAL entry point: <c>SaveWorkbookToTargetAsync</c>, reached via reflection only
/// because it is <c>private</c> (same seam <see cref="R92_RecoverySaveConflictGuardReconciliationTests"/>
/// uses). A custom <see cref="IFileAdapter"/> blocks the background <c>Task.Run</c> mid-serialize
/// on a real wait handle so the test can observe (and control) the exact window where the
/// original bug existed, without relying on wall-clock timing.
/// </summary>
public sealed class R115_SaveGateSiblingWindowRaceTests
{
    private sealed class SaveTempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public SaveTempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            // The save just completed and released its FileStream synchronously, but on Windows a
            // just-written file can stay transiently locked a moment longer (antivirus/indexer scan --
            // especially on a machine busy running many concurrent builds), so a same-instant
            // recursive delete can spuriously see IOException. Retry generously rather than fail the
            // test on an unrelated OS-level timing flake.
            //
            // If an assertion earlier in the test body fails (e.g. this is a genuine pre-fix
            // regression run), `release` is never signaled, so GatedFileAdapter.Save's background
            // thread is still parked on its own internal 10s wait-handle timeout when this runs --
            // that background thread still holds tempPath open until it unblocks and its enclosing
            // `using var file = new FileStream(...)` disposes. 60 attempts (6s) is not always enough
            // to outlast that 10s window, and when it isn't, THIS IOException replaces the real
            // FluentAssertions exception during stack unwinding (a `finally`/`using` that itself
            // throws supersedes the exception it was unwinding for) -- hiding the actual failure
            // behind an unrelated cleanup error. 300 attempts (up to 30s) safely outlasts that
            // worst case so the genuine assertion failure is what actually surfaces.
            const int attempts = 300;
            for (var attempt = 1; Directory.Exists(Path); attempt++)
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                }
                catch (IOException) when (attempt < attempts)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }

    /// <summary>
    /// A file adapter whose <see cref="Save"/> blocks the calling (background <c>Task.Run</c>)
    /// thread on a real wait handle until the test releases it -- giving the test a deterministic
    /// window in which the background serialize is "in flight" without any wall-clock guessing.
    /// </summary>
    private sealed class GatedFileAdapter(ManualResetEventSlim entered, ManualResetEventSlim release) : IFileAdapter
    {
        private readonly NativeJsonAdapter _inner = new();

        public string Extension => ".fxl";
        public string FormatName => "R115 Gated Test Format";

        public Workbook Load(Stream stream) => _inner.Load(stream);

        public void Save(Workbook workbook, Stream stream)
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Test did not release the gated save in time.");
            _inner.Save(workbook, stream);
        }
    }

    private static MainWindow CreateWindow(
        WorkbookRef workbookRef,
        WorkbookWindowRegistry registry,
        WorkbookDocumentState documentState)
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
        var window = new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            commandBus,
            new RecalcEngine(graph, evaluator),
            [],
            workbookRef,
            workbookRef.Current,
            NullUserMessageService.Instance,
            documentState,
            windowRegistry: registry)
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

    private static Task<bool> InvokeSaveWorkbookToTargetAsync(MainWindow window, FileSaveTarget target)
    {
        var method = typeof(MainWindow).GetMethod(
            "SaveWorkbookToTargetAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("SaveWorkbookToTargetAsync is the real save entry point this finding concerns");
        return (Task<bool>)method!.Invoke(window, [target])!;
    }

    /// <summary>
    /// Blocks (via <see cref="DispatcherFrame"/> pumping) until <paramref name="task"/> completes,
    /// without deadlocking on the continuation that resumes via the STA dispatcher's
    /// <c>SynchronizationContext</c> once the gated background <c>Task.Run</c> finishes.
    /// </summary>
    private static bool WaitForSaveResult(Task<bool> task)
    {
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

        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Whether <paramref name="window"/>'s save-input gate (<c>SetFileOperationInputEnabled</c>,
    /// applied through <c>AdjustSaveGate</c>/<c>ApplySaveInProgress</c>) is currently engaged.
    /// <c>SetFileOperationInputEnabled</c> disables every DIRECT child of <c>RootGrid</c> EXCEPT
    /// <c>StatusBarRoot</c> (so the status-bar cancel affordance stays live) -- it never touches
    /// <c>RootGrid.IsEnabled</c> itself, which is an entirely separate blunt on/off switch used by
    /// unrelated code paths (e.g. import/print in <c>MainWindow.DataCommands.cs</c> /
    /// <c>MainWindow.PrintExport.cs</c>). Asserting on <c>RootGrid.IsEnabled</c> directly would
    /// therefore always read <c>true</c> regardless of whether the save gate is engaged --
    /// checking the actual children this mechanism toggles is the only way to observe it.
    /// </summary>
    private static bool IsFileOperationInputBlocked(MainWindow window) =>
        window.RootGrid.Children
            .Cast<UIElement>()
            .Where(child => !ReferenceEquals(child, window.StatusBarRoot))
            .All(child => !child.IsEnabled);

    /// <summary>
    /// The primary regression scenario. Two windows share one document (Excel "New Window"): the
    /// primary saves it while the secondary sits idle. While the background serialize is in
    /// flight, the secondary's input surface must be blocked exactly like the primary's own --
    /// before the fix, it stayed fully enabled. Once the save completes, both windows must be
    /// re-enabled.
    /// </summary>
    [Fact]
    public void Save_BlocksSiblingWindowSharingTheDocument_ForDurationOfBackgroundSerialize()
    {
        StaTestRunner.Run(() =>
        {
            using var temp = new SaveTempDirectory();
            var savePath = System.IO.Path.Combine(temp.Path, "Shared.fxl");

            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var registry = new WorkbookWindowRegistry();
            var documentState = new WorkbookDocumentState();

            var primary = CreateWindow(workbookRef, registry, documentState);
            primary.Show();
            primary.Activate();
            PumpDispatcher();

            var secondary = CreateWindow(workbookRef, registry, documentState);
            secondary.Show();
            secondary.Activate();
            PumpDispatcher();

            try
            {
                registry.Count.Should().Be(2);
                secondary.DocumentId.Should().Be(primary.DocumentId, "New Window siblings share the same document");

                var entered = new ManualResetEventSlim(false);
                var release = new ManualResetEventSlim(false);
                var adapter = new GatedFileAdapter(entered, release);

                var saveTask = InvokeSaveWorkbookToTargetAsync(primary, new FileSaveTarget(savePath, adapter));

                entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
                    "the background Task.Run should have reached the (gated) adapter.Save call");

                // *** The assertion this whole finding is about ***: while the background thread
                // is mid-serialize, the SIBLING window's input surface must be blocked too, not
                // just the saving window's own. (SetFileOperationInputEnabled disables RootGrid's
                // CHILDREN, not RootGrid itself, so the check must inspect those children --
                // RootGrid.IsEnabled is a different, unrelated on/off switch used elsewhere and
                // would never reflect this gate either way.)
                IsFileOperationInputBlocked(secondary).Should().BeTrue(
                    "a New Window sibling shares the exact Workbook instance being serialized in the " +
                    "background, so it must be blocked from editing for the duration of the save");
                IsFileOperationInputBlocked(primary).Should().BeTrue("the saving window blocks its own input, as before");

                release.Set();
                var saved = WaitForSaveResult(saveTask);

                saved.Should().BeTrue();
                IsFileOperationInputBlocked(secondary).Should().BeFalse("the sibling's input gate must release once the save completes");
                IsFileOperationInputBlocked(primary).Should().BeFalse("the saving window's own input gate must release once the save completes");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(secondary);
                MainWindowTestCleanup.CloseWithoutSavePrompt(primary);
                PumpDispatcher();
            }
        });
    }

    /// <summary>
    /// No-regression sibling: a window over a COMPLETELY DIFFERENT document (its own independent
    /// Workbook instance, not a "New Window" view of the one being saved) must stay fully
    /// interactive throughout -- the fix must scope the gate to windows sharing the saving
    /// window's document, not broadcast it to every open window in the process.
    /// </summary>
    [Fact]
    public void Save_DoesNotGateAWindowOverADifferentDocument()
    {
        StaTestRunner.Run(() =>
        {
            using var temp = new SaveTempDirectory();
            var savePath = System.IO.Path.Combine(temp.Path, "Shared.fxl");

            var sharedWorkbook = new Workbook("Book1");
            sharedWorkbook.AddSheet("Sheet1");
            var sharedRef = new WorkbookRef { Current = sharedWorkbook };
            var registry = new WorkbookWindowRegistry();
            var sharedDocumentState = new WorkbookDocumentState();

            var saving = CreateWindow(sharedRef, registry, sharedDocumentState);
            saving.Show();
            saving.Activate();
            PumpDispatcher();

            // An entirely unrelated document, opened independently (its own WorkbookRef/document
            // state) -- NOT a New Window view of `sharedWorkbook`.
            var otherWorkbook = new Workbook("Book2");
            otherWorkbook.AddSheet("Sheet1");
            var otherRef = new WorkbookRef { Current = otherWorkbook };
            var unrelated = CreateWindow(otherRef, registry, new WorkbookDocumentState());
            unrelated.Show();
            unrelated.Activate();
            PumpDispatcher();

            try
            {
                registry.Count.Should().Be(2);
                unrelated.DocumentId.Should().NotBe(saving.DocumentId, "this window views an unrelated document");

                var entered = new ManualResetEventSlim(false);
                var release = new ManualResetEventSlim(false);
                var adapter = new GatedFileAdapter(entered, release);

                var saveTask = InvokeSaveWorkbookToTargetAsync(saving, new FileSaveTarget(savePath, adapter));
                entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

                // Confirm the SAVING window's own gate really did engage (otherwise this test would
                // trivially "pass" even if BroadcastSaveInProgress scoping were broken, since an
                // ungated `unrelated` window looks identical to a correctly-scoped one).
                IsFileOperationInputBlocked(saving).Should().BeTrue("the saving window blocks its own input");
                IsFileOperationInputBlocked(unrelated).Should().BeFalse(
                    "a window over an unrelated document must stay fully interactive during another document's save");

                release.Set();
                var saved = WaitForSaveResult(saveTask);
                saved.Should().BeTrue();
                IsFileOperationInputBlocked(saving).Should().BeFalse("the saving window's own gate releases once the save completes");
                IsFileOperationInputBlocked(unrelated).Should().BeFalse();
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(unrelated);
                MainWindowTestCleanup.CloseWithoutSavePrompt(saving);
                PumpDispatcher();
            }
        });
    }
}
