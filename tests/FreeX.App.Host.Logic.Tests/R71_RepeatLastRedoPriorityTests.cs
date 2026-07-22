using System.Reflection;
using Free.Shared.AppServices;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R71-services-undo-redo-4-1: Excel treats F4/Repeat-Last as REDO
/// whenever a redo is pending (redo takes priority over repeat). Without the CanRedo gate in
/// <c>MainWindow.ExecuteRepeatLast</c> (<c>src/FreeX.App.Host/MainWindow.CommandExecution.cs</c>),
/// F4 after an Undo would re-invoke the stale repeatable factory against whatever is now selected
/// AND destroy the pending redo entry (a plain <c>CommandBus.Execute</c> clears the redo stack),
/// permanently losing the undone change.
/// </summary>
public sealed class R71_RepeatLastRedoPriorityTests
{
    [Fact]
    public void RepeatLast_AfterUndo_PerformsRedoInsteadAndLeavesNewSelectionUntouched()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b5 = new CellAddress(sheet.Id, 5, 2);

            harness.Select(a1, a1);
            harness.ApplyBold();

            sheet.GetStyleOnly(1, 1).Should().NotBeNull();
            harness.Workbook.GetStyle(sheet.GetStyleOnly(1, 1)!.Value).Bold.Should().BeTrue();

            harness.Undo().Should().BeTrue();
            sheet.GetStyleOnly(1, 1).Should().BeNull("undo must revert the bold applied to A1");
            harness.CommandBus.CanRedo(harness.Workbook.Id).Should().BeTrue();

            // Select a different range before pressing F4 -- this is the scenario that exposed the
            // bug: the stale repeatable factory closes over "the current selection" at replay time.
            harness.Select(b5, b5);

            harness.RepeatLast();

            // Redo took priority: A1's bold comes back...
            sheet.GetStyleOnly(1, 1).Should().NotBeNull();
            harness.Workbook.GetStyle(sheet.GetStyleOnly(1, 1)!.Value).Bold.Should().BeTrue();

            // ...and B5 (the now-current selection) was never touched by a stale repeat.
            sheet.GetStyleOnly(5, 2).Should().BeNull();

            // The redo entry was consumed (not destroyed): nothing left to redo, but the undo that
            // would revert the just-replayed bold is available again.
            harness.CommandBus.CanRedo(harness.Workbook.Id).Should().BeFalse();
            harness.CommandBus.CanUndo(harness.Workbook.Id).Should().BeTrue();
        });
    }

    [Fact]
    public void RepeatLast_WithNoPendingRedo_StillRepeatsAgainstCurrentSelection()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b5 = new CellAddress(sheet.Id, 5, 2);

            harness.Select(a1, a1);
            harness.ApplyBold();
            harness.CommandBus.CanRedo(harness.Workbook.Id).Should().BeFalse();

            harness.Select(b5, b5);
            harness.RepeatLast();

            // Normal Repeat behavior is unchanged when there is no pending redo: B5 gets bolded too.
            sheet.GetStyleOnly(1, 1).Should().NotBeNull();
            harness.Workbook.GetStyle(sheet.GetStyleOnly(1, 1)!.Value).Bold.Should().BeTrue();
            sheet.GetStyleOnly(5, 2).Should().NotBeNull();
            harness.Workbook.GetStyle(sheet.GetStyleOnly(5, 2)!.Value).Bold.Should().BeTrue();
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;

        public Workbook Workbook { get; }
        public CommandBus CommandBus { get; }

        public MainWindowHarness()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            CommandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            _window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                CommandBus,
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                new RecordingUserMessageService());

            _window.Show();
            PumpDispatcher();

            // MainWindow_Loaded replaces the constructor-supplied workbook with a fresh one (see
            // R22/R46/R49/R71-RetireWorkbookCallSites harnesses) -- capture the *live* workbook.
            Workbook = workbookRef.Current;
        }

        public void Select(CellAddress start, CellAddress end) =>
            _window.SheetGrid.SelectedRange = new GridRange(start, end);

        public void ApplyBold() => Invoke(_window, "ApplyStyleDiff", new StyleDiff(Bold: true));

        public bool Undo() => (bool)InvokeReturning(_window, "ExecuteUndo")!;

        public void RepeatLast()
        {
            Invoke(_window, "ExecuteRepeatLast");
            PumpDispatcher();
        }

        public void Dispose()
        {
            _window.SuppressNextClosePrompt();
            _window.Close();
            PumpDispatcher();
        }
    }

    private static void Invoke(MainWindow window, string methodName, params object?[] args) =>
        InvokeReturning(window, methodName, args);

    private static object? InvokeReturning(MainWindow window, string methodName, params object?[] args)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), methodName);
        return method.Invoke(window, args);
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// No-op <see cref="IUserMessageService"/> for tests that construct <see cref="MainWindow"/>
    /// directly and don't want real WPF MessageBox windows popping up.
    /// </summary>
    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => false;
        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Ok;
    }
}
