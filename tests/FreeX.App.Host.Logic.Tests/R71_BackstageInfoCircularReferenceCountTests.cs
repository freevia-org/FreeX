using System.Reflection;
using Free.Shared.AppServices;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R71-meta-1: File &gt; Info's circular-reference count
/// (<c>MainWindow.UpdateInfoView</c> in <c>src/FreeX.App.Host/MainWindow.Backstage.cs</c>) read
/// <c>_recalcEngine.CyclicCells</c> with no preceding recalculation. Under Manual calculation, a
/// freshly typed circular formula is never recalculated until F9/save/an automatic-mode edit, so
/// the Info pane reported zero circular references for a workbook that plainly has one, while
/// Formulas &gt; Error Checking (<c>ErrorCheckBtn_Click</c>, which calls <c>RecalculateWorkbook()</c>
/// first) reported the real count for the identical workbook state. The fix makes
/// <c>UpdateInfoView</c> recalculate first too, so both surfaces agree.
/// </summary>
public sealed class R71_BackstageInfoCircularReferenceCountTests
{
    [Fact]
    public void UpdateInfoView_ManualCalc_UnrecalculatedCircularFormula_PopulatesCyclicCells()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;
            var sheet = workbook.GetSheetAt(0);

            // Manual calculation mode, and the circular formulas are set directly on the sheet
            // (bypassing the command system) so nothing recalculates them yet -- mirroring a user
            // who just typed A1=B1/B1=A1 under Manual calc without pressing F9.
            workbook.CalculationMode = WorkbookCalculationMode.Manual;
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 1, 2);
            sheet.SetFormula(a1, "B1");
            sheet.SetFormula(b1, "A1");

            // Before the fix, this stayed empty because UpdateInfoView never recalculated.
            harness.RecalcEngine.CyclicCells.Should().BeEmpty(
                "nothing has recalculated the freshly typed circular formulas yet");

            harness.UpdateInfoView();

            harness.RecalcEngine.CyclicCells.Should().NotBeEmpty(
                "File > Info must recalculate before reading CyclicCells so it reports the same " +
                "circular-reference count as Formulas > Error Checking for the same workbook state");
        });
    }

    [Fact]
    public void UpdateInfoView_NoCircularReference_LeavesCyclicCellsEmpty()
    {
        // Sibling/no-regression case: a workbook with no circular reference must still report zero
        // after File > Info recalculates -- the fix must not manufacture false positives.
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;
            var sheet = workbook.GetSheetAt(0);

            workbook.CalculationMode = WorkbookCalculationMode.Manual;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
            sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A1*2");

            harness.UpdateInfoView();

            harness.RecalcEngine.CyclicCells.Should().BeEmpty(
                "a workbook with no circular reference must report zero even after File > Info recalculates");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        public MainWindow Window { get; }
        public Workbook Workbook { get; }
        public RecalcEngine RecalcEngine { get; }

        public MainWindowHarness()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            RecalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
            Window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                RecalcEngine,
                [],
                workbookRef,
                initialWorkbook,
                new RecordingUserMessageService());

            Window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied workbook
            // with a fresh one -- capture the *live* workbook afterward (see R22/R46/R49 harnesses).
            Workbook = workbookRef.Current;
        }

        public void UpdateInfoView()
        {
            var method = typeof(MainWindow).GetMethod("UpdateInfoView", BindingFlags.Instance | BindingFlags.NonPublic, [])
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateInfoView");
            method.Invoke(Window, []);
        }

        public void Dispose()
        {
            Window.SuppressNextClosePrompt();
            Window.Close();
            PumpDispatcher();
        }
    }

    // r446: delegates to the one fixed implementation -- see R49MainWindowTestHarness.
    private static void PumpDispatcher() => R49MainWindowTestHarness.PumpDispatcher();

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
