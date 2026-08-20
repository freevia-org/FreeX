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
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for sweep91 F1: the WPF host's Watch Window never refreshed on an ordinary
/// typed cell edit. WatchWindowDialog.cs documents four MainWindow "choke points" -- RecalculateWorkbook,
/// RecalculateIfAutomatic, RecalculateDirtyCells, RebuildDependenciesAndCalculate -- as the complete
/// live-refresh mechanism, but MainWindow's own private <c>RecalculateIfAutomatic</c>
/// (MainWindow.WorkbookUiState.cs) has zero production call sites: the real per-edit commit path
/// (CommitEdit/CommitEditAcrossSelection -> CompleteWorkbookSessionCellCommit,
/// MainWindow.Editing.cs) never reached any of the four choke points, so an open Watch Window kept
/// showing pre-edit values until the user manually clicked Refresh or pressed F9/Ctrl+Alt+F9. Unlike
/// R119's tests (which drive the private RecalculateIfAutomatic directly via reflection to prove a
/// different, already-wired concern), these tests deliberately go through the real formula-bar commit
/// path only, exactly as typing a value and pressing Enter does.
/// </summary>
public sealed class R152_WatchWindowLiveEditRefreshTests
{
    [Fact]
    public void CommitEdit_OrdinaryCellEditWithWatchWindowOpen_RefreshesWatchedDependentValueLive()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet = harness.Workbook.GetSheetAt(0);

            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 2, 1);
            sheet.SetCell(a1, new NumberValue(5));
            sheet.SetFormula(b1, "A1*2");

            harness.InvokePrivateHandler("CalcFullBtn_Click");
            sheet.GetValue(b1).Should().Be(new NumberValue(10));
            WatchWindowService.AddWatch(harness.Workbook, b1);

            // Open the Watch Window -- its own initial Refresh() shows B1's current value, "10".
            harness.InvokePrivateHandler("WatchWindowBtn_Click");
            harness.GetWatchedValueText(b1).Should().Be("10");

            // The exact user gesture the finding describes: select A1, type a new value into the
            // formula bar, and commit it via the real CommitEdit path (what pressing Enter in the
            // grid actually calls) -- WITHOUT ever touching the Watch Window's own Refresh/Add/
            // Delete buttons and WITHOUT invoking any private recalc helper via reflection.
            harness.CommitCellEdit(a1, "100");

            sheet.GetValue(b1).Should().Be(new NumberValue(200), "the grid model itself must recalculate B1");
            harness.GetWatchedValueText(b1).Should().Be(
                "200",
                "an ordinary typed cell edit committed through the real CommitEdit path must live-" +
                "refresh the open Watch Window's Value column, not just the grid");
        });
    }

    // No-regression sibling: this fix only adds a Refresh() call inside
    // CompleteWorkbookSessionCellCommit -- an ordinary commit with NO Watch Window open (the common
    // case) must keep succeeding exactly as before, with no null-reference from the new
    // `_watchWindowDialog?.Refresh()` call against a null dialog.
    [Fact]
    public void CommitEdit_WithNoWatchWindowOpen_StillCommitsSuccessfully()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var a1 = new CellAddress(sheet.Id, 1, 1);

            harness.CommitCellEdit(a1, "42");

            sheet.GetValue(a1).Should().Be(new NumberValue(42));
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        public MainWindow Window { get; }
        public Workbook Workbook { get; }

        private readonly MethodInfo _commitEdit;

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

            _commitEdit = typeof(MainWindow)
                .GetMethod("CommitEdit", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CommitEdit");
        }

        public void InvokePrivateHandler(string methodName) =>
            DialogSourceTestSupport.InvokePrivateHandler(Window, methodName);

        /// <summary>
        /// Commits <paramref name="text"/> into <paramref name="address"/> via the real formula-bar
        /// commit path (<c>CommitEdit</c>), exactly as an ordinary interactive edit would -- not via
        /// direct sheet mutation and not via reflectively invoking any private recalc helper.
        /// </summary>
        public void CommitCellEdit(CellAddress address, string text)
        {
            ((SheetGridView)Window.FindName("SheetGrid")).SelectedRange = new GridRange(address, address);
            ((TextBox)Window.FindName("FormulaBar")).Text = text;
            ((bool)_commitEdit.Invoke(Window, null)!).Should().BeTrue();
            PumpDispatcher();
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
