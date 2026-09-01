using System.Reflection;
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

// freex-cell-editing-modes-F2: View > New Window opens a second MainWindow over the SAME shared
// Workbook instance. When window A performs a structural command (Insert/Delete Rows/Columns/
// Cells) that shifts every cell address at/below the target, the registry notifies every OTHER
// window of the document via WorkbookWindowRegistry.NotifyWorkbookChanged ->
// IWorkbookWindow.RefreshFromSharedWorkbook (MainWindow.MultiWindow.cs). That handler used to
// re-read sheet tabs/viewport/toolbar/status/title without ever checking whether the RECEIVING
// window itself had a still-open in-cell/Formula Bar edit anchored at an address the shift just
// moved different content into -- so the receiving window's later CommitEdit() (MainWindow.
// Editing.cs) still wrote to the stale physical address, silently clobbering whatever the shift
// had just placed there. The fix (ReconcilePendingEditWithSharedWorkbookChange, MainWindow.
// MultiWindow.cs) compares the edit's target address's CURRENT content against a baseline
// snapshot taken when the edit began (_pendingEditBaselineText, captured in ShowInlineEditor /
// CaptureFormulaEditCell) and cancels the edit only when that address's content no longer
// matches -- leaving an edit on an UNCHANGED address completely alone, so two sibling windows can
// still independently edit different cells while unrelated commands commit elsewhere (Excel "New
// Window" independence).
public sealed class R170_CellEditingModesSiblingWindowReconcileTests
{
    [Fact]
    public void StructuralInsertInPrimaryWindow_CancelsSecondaryWindowsStaleOpenEditOnTheShiftedCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = SiblingWindowHarness.Create();

            var a5 = new CellAddress(harness.SheetId, 5, 1);
            harness.SetCellText(a5, "original-A5");

            // Window 2 ("New Window" sibling): F2/double-click A5 and start typing a replacement
            // value without pressing Enter (mirrors clicking straight into the Formula Bar --
            // R160-formula-editing-F2's own harness technique for the same edit-open state).
            harness.Secondary.SetActiveCellForTest(a5);
            harness.Secondary.EditActiveCellInFormulaBarForTest();
            harness.Secondary.FormulaBoxTextForTest = "typed-in-window-2";
            harness.Secondary.FormulaEditCellForTest.Should().Be(a5,
                "the harness must reproduce the same open-edit state the user gesture leaves behind");

            // Window 1: select row 5 and Insert Sheet Rows above it -- shifts A5's old content
            // down to A6. This runs through TryExecuteWorksheetStructure -> CompleteWorksheetSession
            // Command -> NotifyOtherWindowsOfWorkbookChange, which calls the shared registry, which
            // in turn invokes Window 2's RefreshFromSharedWorkbook() synchronously.
            harness.Primary.SetActiveCellForTest(a5);
            harness.InsertRowAboveInPrimary();

            // Before the fix: Window 2's stale open edit on A5 survives this notification
            // completely untouched, so pressing Enter afterwards would commit "typed-in-window-2"
            // onto A5 -- which the insert just turned into the freshly-blanked row -- instead of
            // the cell the user was actually looking at when they typed.
            harness.Secondary.FormulaEditCellForTest.Should().BeNull(
                "the receiving window's stale open edit must be reconciled (cancelled) once the " +
                "shared workbook's structural shift moves different content under its target address");
            var secondaryInlineEditor = harness.Secondary.InlineEditorForTest;
            (secondaryInlineEditor is null || secondaryInlineEditor.Visibility == Visibility.Collapsed).Should().BeTrue(
                "the inline editor (if it was the active editor) must be hidden once the open edit is cancelled");

            // The shift itself must still have happened correctly: A6 holds the original A5 content.
            harness.CellText(new CellAddress(harness.SheetId, 6, 1)).Should().Be(
                "original-A5",
                "Insert Sheet Rows must still shift existing content down exactly as before");

            // Finishing the (now-cancelled) edit must be a no-op rather than clobbering A5 with the
            // stale typed text -- CommitEdit falls back to the current selection when no edit is
            // pending, so assert directly on the shifted-in cell's content instead.
            harness.Secondary.CommitEditForTest();
            harness.CellText(a5).Should().NotBe(
                "typed-in-window-2",
                "the stale edit must never land on the address the structural shift moved different content into");
        });
    }

    [Fact]
    public void UnrelatedCommitInPrimaryWindow_LeavesSecondaryWindowsOpenEditOnADifferentCellUntouched()
    {
        // Sibling/no-regression case: Excel "New Window" independence lets two sibling windows
        // edit DIFFERENT cells at once. An ordinary (non-structural) commit in Window 1 also
        // notifies Window 2 via the exact same RefreshFromSharedWorkbook path, but since it never
        // touches Window 2's own edited address, the reconciliation must leave Window 2's open
        // edit completely alone -- exactly as before this fix.
        StaTestRunner.Run(() =>
        {
            using var harness = SiblingWindowHarness.Create();

            var b2 = new CellAddress(harness.SheetId, 2, 2);
            var c10 = new CellAddress(harness.SheetId, 10, 3);

            harness.Secondary.SetActiveCellForTest(b2);
            harness.Secondary.EditActiveCellInFormulaBarForTest();
            harness.Secondary.FormulaBoxTextForTest = "still-typing-in-window-2";

            harness.Primary.SetActiveCellForTest(c10);
            harness.Primary.FormulaBoxTextForTest = "unrelated-edit";
            harness.Primary.CommitEditForTest().Should().BeTrue(
                "an ordinary unrelated cell commit in the other window must still succeed normally");

            harness.Secondary.FormulaEditCellForTest.Should().Be(b2,
                "an unrelated commit elsewhere must not disturb this window's own open edit on a " +
                "different, untouched cell");
            harness.Secondary.FormulaBoxTextForTest.Should().Be("still-typing-in-window-2");

            harness.Secondary.CommitEditForTest().Should().BeTrue(
                "the untouched edit must still be committable normally afterwards");
            harness.CellText(b2).Should().Be("still-typing-in-window-2");
        });
    }

    private sealed class SiblingWindowHarness : IDisposable
    {
        private readonly Sheet _sheet;

        private SiblingWindowHarness(MainWindow primary, MainWindow secondary, Sheet sheet)
        {
            Primary = primary;
            Secondary = secondary;
            _sheet = sheet;
        }

        public MainWindow Primary { get; }

        public MainWindow Secondary { get; }

        public SheetId SheetId => _sheet.Id;

        public void SetCellText(CellAddress address, string text) =>
            _sheet.SetCell(address, Cell.FromValue(new TextValue(text)));

        public string? CellText(CellAddress address) =>
            _sheet.GetCell(address)?.Value is TextValue text ? text.Value : null;

        public void InsertRowAboveInPrimary()
        {
            Primary.InsertRowBtn_Click(Primary, new RoutedEventArgs());
            PumpDispatcher();
        }

        private static void PumpDispatcher()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

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

        public static SiblingWindowHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var registry = new WorkbookWindowRegistry();
            var documentState = new WorkbookDocumentState();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

            var primary = CreateWindow(workbookRef, registry, documentState, commandBus, recalcEngine);
            primary.Show();
            primary.Activate();
            PumpDispatcher();

            var secondary = CreateWindow(
                workbookRef,
                registry,
                documentState,
                commandBus,
                recalcEngine,
                primary.Session.CreateSiblingView(1, 1));
            secondary.Show();
            secondary.Activate();
            PumpDispatcher();

            return new SiblingWindowHarness(primary, secondary, workbookRef.Current.Sheets[0]);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(Secondary);
            MainWindowTestCleanup.CloseWithoutSavePrompt(Primary);
            PumpDispatcher();
        }
    }
}
