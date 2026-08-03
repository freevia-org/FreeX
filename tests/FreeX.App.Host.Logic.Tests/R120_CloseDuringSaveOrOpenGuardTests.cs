using System.ComponentModel;
using System.Reflection;
using System.Windows.Threading;
using Free.Shared.AppServices;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R120-app-host-close-during-save-open (src/FreeX.App.Host/MainWindow.WorkbookLifecycle.cs).
/// Before the fix, <c>MainWindow_Closing</c> decided whether to let the window close purely from
/// <c>_suppressClosePrompt || !_workbookDirty || DocumentSharedWithOtherWindows()</c> -- it never
/// consulted <c>_isSavingFile</c>/<c>_isOpeningFile</c>. A Save-As or File&gt;Open that is actively
/// running on a background thread against a workbook that happens to read as clean at that instant
/// (a brand-new Book1, or an already-saved workbook not yet re-edited) hit the <c>!_workbookDirty</c>
/// fast path: the Closing handler did not cancel, so <c>PrepareActiveWorkbookForFinalClose()</c> ran
/// and the window closed immediately while the save/open Task was still writing/reading the file.
/// Under the default WPF <c>ShutdownMode.OnLastWindowClose</c>, closing the last window shuts the
/// whole process down mid-I/O. The fix adds the same <c>_isSaving || _isOpening</c> guard the
/// Avalonia shell already has at the top of its own <c>MainWindow_Closing</c>
/// (FreeX.App.Avalonia/MainWindow.cs).
/// </summary>
public sealed class R120_CloseDuringSaveOrOpenGuardTests
{
    [Fact]
    public void Closing_WhileSavingOnACleanWorkbook_CancelsTheClose()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ClosingGuardHarness.Create();
            harness.SetIsSavingFile(true);
            // The exact scenario from the defect: a freshly-constructed window's workbook is
            // clean by default (WorkbookDocumentState starts !IsDirty), matching a brand-new
            // Book1 or an already-saved workbook whose save/open Task is still in flight --
            // MarkWorkbookSaved only runs after the awaited write completes, so _workbookDirty
            // reads false the entire time. That is exactly the window in which the bug let the
            // close proceed.
            harness.IsWorkbookDirty().Should().BeFalse(
                "this harness must reproduce the defect's clean-while-saving scenario, not an " +
                "already-dirty workbook that would take a different path through the gate");

            var e = harness.InvokeClosing();

            e.Cancel.Should().BeTrue(
                "a save is actively running on a background thread against this window's workbook; " +
                "closing now would let Application.Shutdown() (OnLastWindowClose) tear the process " +
                "down mid-write");
        });
    }

    [Fact]
    public void Closing_WhileOpeningOnACleanWorkbook_CancelsTheClose()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ClosingGuardHarness.Create();
            harness.SetIsOpeningFile(true);

            var e = harness.InvokeClosing();

            e.Cancel.Should().BeTrue(
                "a File>Open is actively running on a background thread against this window; " +
                "closing now would let the process shut down mid-read");
        });
    }

    /// <summary>
    /// No-regression sibling: once the save/open in-flight flags are back to idle, an ordinary
    /// clean, non-shared window must still take the pre-existing fast-close path (no dialog, no
    /// cancel) exactly as before this fix -- only the "operation in flight" case changed.
    /// </summary>
    [Fact]
    public void Closing_WhenIdleAndClean_StillClosesImmediately()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ClosingGuardHarness.Create();
            harness.SetIsSavingFile(false);
            harness.SetIsOpeningFile(false);

            var e = harness.InvokeClosing();

            e.Cancel.Should().BeFalse(
                "an idle, clean, unshared window must keep closing immediately -- the new guard " +
                "must only trigger while a save/open is actually in flight");
        });
    }

    private sealed class ClosingGuardHarness : IDisposable
    {
        private readonly MethodInfo _closingMethod;
        private readonly FieldInfo _isSavingFileField;
        private readonly FieldInfo _isOpeningFileField;
        private readonly PropertyInfo _workbookDirtyProperty;

        private ClosingGuardHarness(MainWindow window)
        {
            Window = window;
            _closingMethod = typeof(MainWindow).GetMethod(
                "MainWindow_Closing", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "MainWindow_Closing");
            _isSavingFileField = typeof(MainWindow).GetField(
                "_isSavingFile", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_isSavingFile");
            _isOpeningFileField = typeof(MainWindow).GetField(
                "_isOpeningFile", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_isOpeningFile");
            _workbookDirtyProperty = typeof(MainWindow).GetProperty(
                "_workbookDirty", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "_workbookDirty");
        }

        public MainWindow Window { get; }

        public void SetIsSavingFile(bool value) => _isSavingFileField.SetValue(Window, value);

        public void SetIsOpeningFile(bool value) => _isOpeningFileField.SetValue(Window, value);

        public bool IsWorkbookDirty() => (bool)_workbookDirtyProperty.GetValue(Window)!;

        /// <summary>
        /// Invokes the private async-void Closing handler and returns the CancelEventArgs it was
        /// given. Every path the guard exercises (the new save/open check and the pre-existing
        /// !_workbookDirty fast path) completes synchronously before the first await, so by the
        /// time Invoke returns, e.Cancel already reflects the handler's decision.
        /// </summary>
        public CancelEventArgs InvokeClosing()
        {
            var e = new CancelEventArgs(false);
            _closingMethod.Invoke(Window, [null, e]);
            return e;
        }

        public static ClosingGuardHarness Create()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var adapters = new IFileAdapter[]
            {
                new TestFileAdapter(save: (_, _) => { }, extension: ".fxjson")
            };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                adapters,
                workbookRef,
                initialWorkbook,
                new RecordingUserMessageService());

            window.Show();
            PumpDispatcher();

            return new ClosingGuardHarness(window);
        }

        public void Dispose()
        {
            // Restore idle state so the real Closing handler can tear the window down cleanly
            // regardless of which flag a given test set.
            SetIsSavingFile(false);
            SetIsOpeningFile(false);
            Window.SuppressNextClosePrompt();
            Window.Close();
            PumpDispatcher();
        }

        private static void PumpDispatcher()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    /// <summary>A no-op message service -- the guard's own warning dialog must not block the test.</summary>
    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => true;

        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Yes;
    }
}
