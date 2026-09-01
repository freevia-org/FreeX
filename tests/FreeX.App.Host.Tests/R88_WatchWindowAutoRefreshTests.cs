using System.Collections;
using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R88-app-formula-auditing-5-1: the Watch Window is a modeless, non-closed dialog whose Value
/// column only used to update from its own Add/Refresh/Delete button handlers -- an ordinary cell
/// edit elsewhere on the sheet (the whole point of the feature) left it showing a stale value
/// forever. MainWindow's recalculation choke points (<c>RecalculateIfAutomatic</c> and siblings)
/// now call <see cref="WatchWindowDialog.Refresh"/> after every recalculation.
/// </summary>
public sealed class R88_WatchWindowAutoRefreshTests
{
    [Fact]
    public void AutomaticModeCellEdit_RefreshesTheOpenWatchWindowsValueColumnWithoutManualRefresh()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;
            var sheet = workbook.GetSheetAt(0);
            workbook.CalculationMode.Should().Be(WorkbookCalculationMode.Automatic);

            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 2, 1);
            sheet.SetCell(a1, new NumberValue(5));
            sheet.SetFormula(b1, "A1*2");

            // Seed the dependency graph and cached value via one full recalc, then watch B1.
            harness.InvokePrivateHandler("CalcFullBtn_Click");
            sheet.GetValue(b1).Should().Be(new NumberValue(10));
            WatchWindowService.AddWatch(workbook, b1);

            // Open the Watch Window -- this also performs its own initial Refresh(), so B1 starts
            // out showing "10".
            harness.InvokePrivateHandler("WatchWindowBtn_Click");
            harness.GetWatchedValueText(b1).Should().Be("10");

            // The exact failure scenario: edit A1 elsewhere on the sheet and let the workbook
            // recalculate automatically (Automatic mode is the default and the scenario the finding
            // describes), WITHOUT ever touching the Watch Window's own Refresh/Add/Delete buttons.
            sheet.SetCell(a1, new NumberValue(100));
            harness.InvokeRecalculateIfAutomatic([a1]);
            sheet.GetValue(b1).Should().Be(new NumberValue(200));

            harness.GetWatchedValueText(b1).Should().Be(
                "200",
                "the open Watch Window must track B1's live value across an ordinary automatic-mode " +
                "recalculation, not only when the user manually clicks Add/Refresh/Delete Watch");
        });
    }

    // No-regression sibling: RecalculateIfAutomatic must keep gating on the workbook's calculation
    // mode exactly as before -- the new watch-window refresh must not fire (and must not force a
    // recalculation) when the workbook is in Manual mode, matching Excel's F9-required behaviour.
    [Fact]
    public void ManualModeCellEdit_DoesNotRefreshTheWatchWindowUntilCalculateNow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;
            var sheet = workbook.GetSheetAt(0);

            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 2, 1);
            sheet.SetCell(a1, new NumberValue(5));
            sheet.SetFormula(b1, "A1*2");

            harness.InvokePrivateHandler("CalcFullBtn_Click");
            sheet.GetValue(b1).Should().Be(new NumberValue(10));
            WatchWindowService.AddWatch(workbook, b1);

            harness.InvokePrivateHandler("WatchWindowBtn_Click");
            harness.GetWatchedValueText(b1).Should().Be("10");

            workbook.CalculationMode = WorkbookCalculationMode.Manual;
            sheet.SetCell(a1, new NumberValue(100));
            harness.InvokeRecalculateIfAutomatic([a1]);

            harness.GetWatchedValueText(b1).Should().Be(
                "10",
                "Manual calculation mode must not auto-recalculate (or auto-refresh the Watch " +
                "Window) until the user explicitly asks for a recalculation, exactly as before this fix");

            // Ctrl+Alt+F9 ("Calculate Full") must still bring both the cell and the Watch Window's
            // Value column up to date once the user does ask for a recalculation. Plain F9
            // (CalcNowBtn_Click) is deliberately NOT used here: its dirty-only scope only
            // re-evaluates formulas the tracked dependency graph already knows are dirty, and A1 was
            // changed via a direct sheet.SetCell (bypassing the graph) rather than a tracked edit --
            // see R79_CalculateNowVsCalculateFullRecalcScopeTests for that distinct, already-covered
            // scope difference. Calculate Full re-evaluates every formula regardless of dirty state.
            harness.InvokePrivateHandler("CalcFullBtn_Click");
            sheet.GetValue(b1).Should().Be(new NumberValue(200));
            harness.GetWatchedValueText(b1).Should().Be("200");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        public MainWindow Window { get; }
        public Workbook Workbook { get; }

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
                NullUserMessageService.Instance);

            Window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied workbook
            // with a fresh one via CreateNewWorkbook() -- capture the *live* workbook afterward so
            // the test operates on the same Workbook instance MainWindow's handlers use.
            Workbook = workbookRef.Current;
        }

        public void InvokePrivateHandler(string methodName) =>
            DialogSourceTestSupport.InvokePrivateHandler(Window, methodName);

        public void InvokeRecalculateIfAutomatic(IReadOnlyList<CellAddress> changedCells)
        {
            Window.RecalculateIfAutomatic(changedCells);
        }

        /// <summary>
        /// Reads the Value column currently displayed for <paramref name="address"/> in the open
        /// Watch Window, reaching the dialog's private <c>_listView</c> field/row records by
        /// reflection since the dialog exposes no public accessor for its rows.
        /// </summary>
        public string? GetWatchedValueText(CellAddress address)
        {
            var dialog = typeof(MainWindow)
                .GetField("_watchWindowDialog", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(Window);
            dialog.Should().NotBeNull("WatchWindowBtn_Click must have opened the Watch Window first");

            var listView = (ListView)typeof(WatchWindowDialog)
                .GetField("_listView", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;

            foreach (var row in (IEnumerable)listView.ItemsSource)
            {
                var rowType = row.GetType();
                var rowAddress = (CellAddress)rowType.GetProperty("Address")!.GetValue(row)!;
                if (rowAddress.Equals(address))
                    return (string?)rowType.GetProperty("Value")!.GetValue(row);
            }

            return null;
        }

        public void Dispose()
        {
            foreach (System.Windows.Window ownedWindow in Window.OwnedWindows.Cast<System.Windows.Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(Window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
