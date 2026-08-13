using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Free.Shared.AppServices;
using FreeX.App.Presentation.Editing;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for two round-54 findings in the WPF host:
///
///  - R54-commands-cut-paste-move-4-1 (<c>MainWindow.ClipboardCommands.cs</c>
///    <c>TryCreateCutMoveCommand</c>): a cross-sheet Cut+Paste (cut on one sheet, paste on
///    another) must route through <see cref="MoveRangeCommand"/>'s cross-sheet move -- so a
///    formula elsewhere that referenced the cut cell follows it to the new sheet/address --
///    instead of being silently downgraded to the generic copy-paste-and-clear combo, which
///    never repoints other formulas.
///
///  - R54-render-copy-cut-marquee-4-1 (<c>MainWindow.CommandExecution.cs</c>
///    <c>TryExecuteEditCells</c> and <c>MainWindow.ClipboardCommands.cs</c>
///    <c>ExecuteClearSelection</c>): committing an ordinary cell edit, or Delete/Clear
///    Contents, while a Copy/Cut marching-ants marquee is active must cancel that marquee,
///    matching Excel -- otherwise a later Paste could silently move/copy a source range using
///    stale (already-overwritten) contents while the marquee still visually implies the
///    original copied/cut values are staged.
/// </summary>
public sealed class R54_ClipboardMarqueeAndCutMoveTests
{
    [Fact]
    public void CrossSheetCutThenPaste_MovesCellAndUpdatesReferencingFormula()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet1 = harness.Workbook.GetSheetAt(0);
            var sheet2 = harness.Workbook.AddSheet("Sheet2");

            var a1 = new CellAddress(sheet1.Id, 1, 1);
            var c3 = new CellAddress(sheet2.Id, 3, 3);
            var b2 = new CellAddress(sheet2.Id, 2, 2);
            sheet1.SetCell(a1, new NumberValue(42));
            sheet2.SetFormula(c3, "Sheet1!A1");

            harness.SetSelectedRange(new GridRange(a1, a1));
            harness.InvokeClickHandler("CutBtn_Click");

            harness.SwitchToSheet(sheet2.Id);
            harness.SetSelectedRange(new GridRange(b2, b2));
            harness.InvokeClickHandler("PasteBtn_Click");

            sheet2.GetCell(b2)!.Value.Should().Be(
                new NumberValue(42),
                "the cut cell's value must land at the cross-sheet paste destination");
            sheet1.GetCell(a1).Should().BeNull(
                "the source cell must be MOVED away by a cross-sheet Cut+Paste, not merely copied and left behind");
            sheet2.GetCell(c3)!.FormulaText.Should().Be(
                "Sheet2!B2",
                "a formula that referenced the cut cell must follow the move across sheets, exactly like " +
                "the pre-existing same-sheet Cut+Paste move and the Avalonia-facing WorkbookSession equivalent");
        });
    }

    [Fact]
    public void SameSheetCutThenPaste_StillMovesCellsNormally()
    {
        // Sibling no-regression check: relaxing the cross-sheet guard in TryCreateCutMoveCommand
        // must not disturb the pre-existing, already-covered same-sheet Cut+Paste move behavior.
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet1 = harness.Workbook.GetSheetAt(0);
            var a1 = new CellAddress(sheet1.Id, 1, 1);
            var b1 = new CellAddress(sheet1.Id, 1, 2);
            var d1 = new CellAddress(sheet1.Id, 1, 4);
            sheet1.SetCell(a1, new NumberValue(5));
            sheet1.SetFormula(b1, "A1");

            harness.SetSelectedRange(new GridRange(b1, b1));
            harness.InvokeClickHandler("CutBtn_Click");

            harness.SetSelectedRange(new GridRange(d1, d1));
            harness.InvokeClickHandler("PasteBtn_Click");

            sheet1.GetCell(d1)!.FormulaText.Should().Be("A1", "the moved formula keeps its own reference unchanged");
            sheet1.GetCell(b1).Should().BeNull("the source cell was moved away, not merely cleared");
        });
    }

    [Fact]
    public void CommittingCellEdit_CancelsActiveCutMarquee()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet1 = harness.Workbook.GetSheetAt(0);
            var a1 = new CellAddress(sheet1.Id, 1, 1);
            var a2 = new CellAddress(sheet1.Id, 2, 1);
            var a3 = new CellAddress(sheet1.Id, 3, 1);
            sheet1.SetCell(a1, new NumberValue(1));
            sheet1.SetCell(a2, new NumberValue(2));
            sheet1.SetCell(a3, new NumberValue(3));

            harness.SetSelectedRange(new GridRange(a1, a3));
            harness.InvokeClickHandler("CutBtn_Click");

            harness.Grid.ClipboardRange.Should().NotBeNull("Cut must start an active marching-ants marquee");
            harness.Grid.ClipboardIsCut.Should().BeTrue();

            // An unrelated, ordinary cell edit is committed (e.g. typing 99 into A2 and pressing
            // Enter) -- in real Excel this immediately cancels the active Cut marquee.
            harness.CommitCellEdit(a2, new NumberValue(99));

            harness.Grid.ClipboardRange.Should().BeNull(
                "committing a normal cell edit must cancel an active Copy/Cut marquee (R54-render-copy-cut-marquee-4-1)");
            harness.Grid.ClipboardIsCut.Should().BeFalse();
            harness.HasInternalClipboard.Should().BeFalse(
                "the stale internal clipboard payload must also be dropped so a later Paste cannot silently move it");
        });
    }

    [Fact]
    public void ClearContentsCommit_CancelsActiveCutMarquee()
    {
        // Sibling coverage for the same finding's twin scenario ("... or Delete/Clear Contents").
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet1 = harness.Workbook.GetSheetAt(0);
            var a1 = new CellAddress(sheet1.Id, 1, 1);
            var a2 = new CellAddress(sheet1.Id, 2, 1);
            sheet1.SetCell(a1, new NumberValue(1));
            sheet1.SetCell(a2, new NumberValue(2));

            harness.SetSelectedRange(new GridRange(a1, a1));
            harness.InvokeClickHandler("CutBtn_Click");
            harness.Grid.ClipboardRange.Should().NotBeNull();

            // Select an unrelated cell and invoke Delete / Clear Contents.
            harness.SetSelectedRange(new GridRange(a2, a2));
            harness.InvokeExecuteClearSelection();

            harness.Grid.ClipboardRange.Should().BeNull(
                "Delete/Clear Contents must cancel an active Copy/Cut marquee (R54-render-copy-cut-marquee-4-1)");
            harness.Grid.ClipboardIsCut.Should().BeFalse();
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly FieldInfo _clipboardSessionField;
        private readonly MethodInfo _selectSingleSheetTab;
        private readonly MethodInfo _updateViewport;
        private readonly MethodInfo _tryExecuteEditCells;
        private readonly MethodInfo _executeClearSelection;

        public MainWindow Window { get; }
        public Workbook Workbook { get; }
        public GridView Grid => (GridView)Window.FindName("SheetGrid");

        public MainWindowHarness()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            Window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                new RecordingUserMessageService());

            Window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied
            // workbook with a fresh one via CreateNewWorkbook() -- capture the *live* workbook
            // afterward so the test operates on the same Workbook instance MainWindow's own
            // handlers actually use.
            Workbook = workbookRef.Current;

            _clipboardSessionField = typeof(MainWindow)
                .GetField("_workbookClipboardSession", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_workbookClipboardSession");
            _selectSingleSheetTab = typeof(MainWindow)
                .GetMethod("SelectSingleSheetTab", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(SheetId)])
                ?? throw new MissingMethodException(nameof(MainWindow), "SelectSingleSheetTab");
            _updateViewport = typeof(MainWindow)
                .GetMethod("UpdateViewport", BindingFlags.Instance | BindingFlags.NonPublic, [])
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateViewport");
            _tryExecuteEditCells = typeof(MainWindow)
                .GetMethod(
                    "TryExecuteEditCells",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    [typeof(IReadOnlyList<(CellAddress, Cell)>), typeof(string)])
                ?? throw new MissingMethodException(nameof(MainWindow), "TryExecuteEditCells");
            _executeClearSelection = typeof(MainWindow)
                .GetMethod("ExecuteClearSelection", BindingFlags.Instance | BindingFlags.NonPublic, [])
                ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteClearSelection");
        }

        public bool HasInternalClipboard =>
            ((WorkbookClipboardSession)_clipboardSessionField.GetValue(Window)!).HasContent;

        public void SetSelectedRange(GridRange range)
        {
            Grid.SelectedRanges = null;
            Grid.SelectedRange = range;
        }

        public void SwitchToSheet(SheetId sheetId)
        {
            _selectSingleSheetTab.Invoke(Window, [sheetId]);
            _updateViewport.Invoke(Window, []);
            PumpDispatcher();
        }

        public void InvokeClickHandler(string methodName)
        {
            var method = typeof(MainWindow).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic,
                [typeof(object), typeof(RoutedEventArgs)]);
            method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
            method!.Invoke(Window, [Window, new RoutedEventArgs()]);
            PumpDispatcher();
        }

        public void CommitCellEdit(CellAddress address, ScalarValue value)
        {
            var edits = new List<(CellAddress, Cell)> { (address, Cell.FromValue(value)) };
            _tryExecuteEditCells.Invoke(Window, [edits, "Edit Cell"]);
            PumpDispatcher();
        }

        public void InvokeExecuteClearSelection()
        {
            _executeClearSelection.Invoke(Window, []);
            PumpDispatcher();
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in Window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            Window.SuppressNextClosePrompt();
            Window.Close();
            PumpDispatcher();
        }
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
