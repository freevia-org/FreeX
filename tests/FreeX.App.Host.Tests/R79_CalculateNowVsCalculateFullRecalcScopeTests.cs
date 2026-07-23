using System.Reflection;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R79-calc-volatile-recalc-5-1 / R79-calc-volatile-recalc-5-3: F9 ("Calculate Now" /
/// CalcNowBtn_Click), Ctrl+Alt+F9 ("Calculate Full" / CalcFullBtn_Click), and Ctrl+Alt+Shift+F9
/// ("Rebuild Dependencies and Calculate") must have distinct, escalating recalc scopes matching
/// Excel -- not all collapse to the same full rebuild-and-evaluate-everything pass.
/// </summary>
public sealed class R79_CalculateNowVsCalculateFullRecalcScopeTests
{
    [Fact]
    public void CalcNowBtn_Click_DoesNotReevaluateAFormulaCellWhoseChangeTheGraphNeverObserved_UnlikeCalcFullBtn_Click()
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

            // Seed the dependency graph/volatile tracking via one full recalc -- steady state,
            // matching what Automatic mode already guarantees before a user ever presses F9.
            harness.InvokePrivateHandler("CalcFullBtn_Click");
            sheet.GetValue(b1).Should().Be(new NumberValue(10));

            // Mutate A1 directly (bypassing RecalculateIfAutomatic/any command), simulating a
            // precedent change the recalc engine's tracked dependency graph never observed --
            // B1's cached value is now stale relative to A1.
            sheet.SetCell(a1, new NumberValue(9));

            harness.InvokePrivateHandler("CalcNowBtn_Click");
            sheet.GetValue(b1).Should().Be(
                new NumberValue(10),
                "plain F9's dirty-only scope must not force-reevaluate a formula cell that the tracked dependency graph has no reason to consider dirty");

            harness.InvokePrivateHandler("CalcFullBtn_Click");
            sheet.GetValue(b1).Should().Be(
                new NumberValue(18),
                "Ctrl+Alt+F9's Calculate Full scope must re-evaluate every formula cell regardless of dirty state, picking up A1's new value");
        });
    }

    [Fact]
    public void RebuildDependenciesAndCalculate_StillFullyRecalculatesAfterRemovingTheRedundantRebuildCall()
    {
        // No-regression sibling for R79-calc-volatile-recalc-5-3: removing the redundant explicit
        // RebuildFormulaDependencies call (RecalculateAllFormulas already does it internally) must
        // not weaken Ctrl+Alt+Shift+F9's correctness -- it must still pick up an out-of-graph change.
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

            sheet.SetCell(a1, new NumberValue(9));

            harness.InvokeVoid("RebuildDependenciesAndCalculate");
            sheet.GetValue(b1).Should().Be(
                new NumberValue(18),
                "Ctrl+Alt+Shift+F9 must still rebuild the dependency graph and fully recalculate correctly with only one (not two) RebuildFormulaDependencies passes");
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

        public void InvokeVoid(string methodName)
        {
            var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, [])
                ?? throw new MissingMethodException(nameof(MainWindow), methodName);
            method.Invoke(Window, []);
        }

        public void InvokePrivateHandler(string methodName) =>
            DialogSourceTestSupport.InvokePrivateHandler(Window, methodName);

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
