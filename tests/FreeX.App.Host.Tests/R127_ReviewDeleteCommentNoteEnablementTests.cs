using System.Reflection;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R127-review-delete-enablement-1: Review &gt; Delete Comment / Delete
/// Note must grey out live as the active cell changes, matching Excel, instead of staying
/// permanently enabled (the default <see cref="RibbonCommandState.IsEnabled"/>) until some other
/// review mutation happens to call <c>RefreshReviewCommentNoteCommandStates</c>. Before the fix,
/// <c>SetActiveCell</c> (MainWindow.Selection.cs) never called that refresh, so moving off a cell
/// that had a note/comment left "Delete Note"/"Delete Comment" enabled even after landing on a
/// cell with nothing to delete. Drives the real private <c>SetActiveCell</c> choke point via
/// reflection -- the same product entry point every plain cell click/arrow-key move uses -- so the
/// test exercises the actual fixed code path, not a hand-built model.
/// </summary>
public sealed class R127_ReviewDeleteCommentNoteEnablementTests
{
    private sealed class DocumentPlaceholderWindow(WorkbookId documentId) : IWorkbookWindow
    {
        public WorkbookId DocumentId { get; } = documentId;
        public void ApplyWindowTitleSuffix(string suffix) { }
        public void RefreshFromSharedWorkbook() { }
        public void RefreshTitleBar() { }
        public void ActivateWindow() { }
        public void SetWindowVisible(bool visible) { }
        public WorkbookScrollOffset GetScrollOffset() => default;
        public void SetScrollOffset(WorkbookScrollOffset offset) { }
        public void TileToWorkArea(Rect bounds) { }
        public void ApplyFormulaBarVisibility(bool visible) { }
        public void ApplySaveInProgress(bool inProgress) { }
    }

    /// <summary>Records every ShowInfo call so the click-handler no-op path can be asserted on
    /// without popping a real WPF message box.</summary>
    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public List<(string Message, string Title)> InfoCalls { get; } = [];

        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") =>
            InfoCalls.Add((message, title));

        public bool AskYesNo(string message, string title = "Confirm") => true;

        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Yes;
    }

    private static (MainWindow Window, Workbook Workbook, Sheet Sheet) CreateAdoptedWindow(
        IUserMessageService? messageService = null)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var workbookRef = new WorkbookRef { Current = workbook };
        var registry = new WorkbookWindowRegistry();
        registry.Register(new DocumentPlaceholderWindow(workbook.Id));

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
            messageService ?? NullUserMessageService.Instance,
            new WorkbookDocumentState(),
            windowRegistry: registry)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };

        window.Show();
        window.Activate();
        PumpDispatcher();

        return (window, workbook, sheet);
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>Drives the real private SetActiveCell -- the choke point every plain cell
    /// click/arrow-key move goes through -- via reflection.</summary>
    private static void SetActiveCell(MainWindow window, CellAddress address)
    {
        var method = typeof(MainWindow).GetMethod(
            "SetActiveCell", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(window, [address]);
    }

    private static bool GetRibbonEnabled(MainWindow window, string commandId)
    {
        var field = typeof(MainWindow).GetField("_ribbonState", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        var store = (IRibbonStateStore)field!.GetValue(window)!;
        return store.GetState(commandId).IsEnabled;
    }

    private static void InvokeClick(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(window, [window, new RoutedEventArgs()]);
    }

    /// <summary>
    /// Fail-before/pass-after: moving the active cell from one that has a note onto one that does
    /// not must live-disable "Delete Note" (and moving onto a threaded-comment cell must live-
    /// enable "Delete Comment"). Before the fix this never happened on plain selection changes --
    /// enablement stayed stuck at whatever RefreshReviewCommentNoteCommandStates last computed (or
    /// at its true default, IsEnabled: true, if that method had never run at all).
    /// </summary>
    [Fact]
    public void ReviewDeleteNoteAndDeleteComment_TrackActiveCellOnPlainSelectionChange() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b2 = new CellAddress(sheet.Id, 2, 2);
            var c3 = new CellAddress(sheet.Id, 3, 3);

            sheet.Comments[a1] = "a note";
            sheet.ThreadedComments[b2] = new ThreadedComment("a comment");

            // Selecting the note cell must live-enable Delete Note.
            SetActiveCell(window, a1);
            GetRibbonEnabled(window, "Delete Note").Should().BeTrue(
                "the active cell A1 has a note");
            GetRibbonEnabled(window, "Delete Comment").Should().BeFalse(
                "A1 has no threaded comment");

            // Moving to the threaded-comment cell must live-enable Delete Comment and live-disable
            // Delete Note -- this is the transition the bug missed entirely.
            SetActiveCell(window, b2);
            GetRibbonEnabled(window, "Delete Comment").Should().BeTrue(
                "the active cell B2 has a threaded comment");
            GetRibbonEnabled(window, "Delete Note").Should().BeFalse(
                "B2 has no note, so Delete Note must grey out now that the selection moved off A1");

            // Moving to a cell with neither must grey out both, matching Excel.
            SetActiveCell(window, c3);
            GetRibbonEnabled(window, "Delete Note").Should().BeFalse(
                "C3 has no note");
            GetRibbonEnabled(window, "Delete Comment").Should().BeFalse(
                "C3 has no threaded comment");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    /// <summary>
    /// No-regression sibling: with the enablement now correct, a stale/forced click on Delete Note
    /// while nothing is selected to delete (e.g. via the worksheet context menu, or any other
    /// route that can still reach the handler) must surface a message instead of silently
    /// no-op'ing, matching Avalonia's DeleteActiveCellNote failure path.
    /// </summary>
    [Fact]
    public void ReviewDeleteCommentBtnClick_NoNoteAtActiveCell_ShowsMessageInsteadOfSilentNoOp() =>
        StaTestRunner.Run(() =>
    {
        var messages = new RecordingUserMessageService();
        var (window, workbook, sheet) = CreateAdoptedWindow(messages);
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            SetActiveCell(window, a1);

            GetRibbonEnabled(window, "Delete Note").Should().BeFalse("A1 has no note");

            InvokeClick(window, "ReviewDeleteCommentBtn_Click");

            messages.InfoCalls.Should().ContainSingle(
                "clicking Delete Note with nothing to delete must surface feedback, not fail silently");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });
}
