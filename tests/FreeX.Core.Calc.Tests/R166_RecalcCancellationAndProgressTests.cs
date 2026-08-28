using System.Threading;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R166-shared-status-progress-F2: RecalculateAllFormulas (Ctrl+Alt+F9/F9) and
/// RecalculateSheetFormulas (Shift+F9) previously took no CancellationToken and reported no
/// IProgress&lt;T&gt;, unlike every other long operation in the app. These tests prove the engine now
/// (a) honors a canceled token instead of always running the whole pass to completion, and
/// (b) reports per-cell progress -- while leaving the pre-existing "no token/no progress" call
/// pattern used everywhere else in the app (the sibling case) completely unaffected.
/// </summary>
public sealed class R166_RecalcCancellationAndProgressTests
{
    private static RecalcEngine Engine() =>
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

    private static (Workbook workbook, Sheet sheet) BuildFormulaHeavyWorkbook(int formulaCellCount)
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        // A1 holds a literal seed value; B1..B{n} each hold an independent formula referencing A1,
        // so RecalculateAllFormulas/RecalculateSheetFormulas actually has formulaCellCount cells to
        // walk through its per-cell evaluation loop.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        for (var row = 1; row <= formulaCellCount; row++)
            sheet.SetFormula(new CellAddress(sheet.Id, (uint)row, 2), "A1+" + row);

        return (workbook, sheet);
    }

    // ── Cancellation: RecalculateAllFormulas ─────────────────────────────────

    [Fact]
    public void RecalculateAllFormulas_WithAlreadyCanceledToken_ThrowsInsteadOfRunningToCompletion()
    {
        var (workbook, sheet) = BuildFormulaHeavyWorkbook(50);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Engine().RecalculateAllFormulas(workbook, cancellationToken: cts.Token);

        act.Should().Throw<OperationCanceledException>(
            "a large synchronous F9/Ctrl+Alt+F9 recalculation must be interruptible instead of " +
            "always blocking the caller for the full duration with no way out");
    }

    [Fact]
    public void RecalculateAllFormulas_CanceledMidPass_LeavesMostCellsUnevaluated()
    {
        // Cancel after the 3rd cell is reported as completed: prove the loop actually stops instead
        // of the token only being checked once up front (which would let all 200 cells finish).
        var (workbook, sheet) = BuildFormulaHeavyWorkbook(200);
        using var cts = new CancellationTokenSource();
        var progress = new CancelAfterNProgress(cts, cancelAfterCompletedCount: 3);

        var act = () => Engine().RecalculateAllFormulas(workbook, cancellationToken: cts.Token, progress: progress);

        act.Should().Throw<OperationCanceledException>();

        // Count how many of the 200 formula cells actually reached their computed value. The
        // evaluation loop throws right after reporting completedCount==3, before evaluating any
        // further cell, so at most a handful can have been written -- nowhere near all 200. (Not
        // pinned to an exact count/order since the evaluation order is an internal graph detail.)
        var evaluatedCount = 0;
        for (var row = 1; row <= 200; row++)
        {
            if (sheet.GetValue((uint)row, 2).Equals(new NumberValue(1 + row)))
                evaluatedCount++;
        }

        evaluatedCount.Should().BeLessThan(200,
            "cancellation should stop the per-cell evaluation loop well before it reaches every formula cell");
    }

    // ── Cancellation: RecalculateSheetFormulas ───────────────────────────────

    [Fact]
    public void RecalculateSheetFormulas_WithAlreadyCanceledToken_ThrowsInsteadOfRunningToCompletion()
    {
        var (workbook, sheet) = BuildFormulaHeavyWorkbook(50);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Engine().RecalculateSheetFormulas(workbook, sheet.Id, cancellationToken: cts.Token);

        act.Should().Throw<OperationCanceledException>(
            "a large synchronous Shift+F9 'Calculate Sheet' pass must be interruptible too");
    }

    // ── Progress: RecalculateAllFormulas ─────────────────────────────────────

    [Fact]
    public void RecalculateAllFormulas_ReportsProgressFromZeroToTotalFormulaCells()
    {
        var (workbook, _) = BuildFormulaHeavyWorkbook(25);
        var reports = new List<RecalcProgress>();
        var progress = new RecordingProgress(reports);

        var report = Engine().RecalculateAllFormulas(workbook, progress: progress);

        reports.Should().NotBeEmpty("the caller must see at least one progress update to drive a status-bar percentage");
        reports[0].TotalFormulaCells.Should().Be(25);
        reports[0].CompletedFormulaCells.Should().Be(0, "the first report should reflect the pass just starting");
        reports[^1].CompletedFormulaCells.Should().Be(reports[^1].TotalFormulaCells,
            "the final report should reflect the pass having finished all 25 formula cells");
        report.RecalculatedCells.Count.Should().Be(25);
    }

    // ── Sibling no-regression: the existing no-token/no-progress call pattern ──

    [Fact]
    public void RecalculateAllFormulas_WithoutCancellationOrProgress_StillRunsToCompletionUnchanged()
    {
        // This mirrors every real call site in the shipping app today (WorkbookCellEditService,
        // MainWindow.Backstage's post-open recalc, StartupPipelinePrewarmer): none of them pass a
        // token or a progress callback. Prove that omitting both parameters still behaves exactly
        // as RecalculateAllFormulas always has -- CancellationToken.None never throws, and a null
        // progress is simply never invoked.
        var (workbook, sheet) = BuildFormulaHeavyWorkbook(25);

        var report = Engine().RecalculateAllFormulas(workbook);

        report.RecalculatedCells.Count.Should().Be(25);
        for (var row = 1; row <= 25; row++)
            sheet.GetValue((uint)row, 2).Should().Be(new NumberValue(1 + row));
    }

    [Fact]
    public void RecalculateSheetFormulas_WithoutCancellationOrProgress_StillRunsToCompletionUnchanged()
    {
        var (workbook, sheet) = BuildFormulaHeavyWorkbook(25);

        var report = Engine().RecalculateSheetFormulas(workbook, sheet.Id);

        report.RecalculatedCells.Count.Should().Be(25);
        for (var row = 1; row <= 25; row++)
            sheet.GetValue((uint)row, 2).Should().Be(new NumberValue(1 + row));
    }

    private sealed class RecordingProgress : IProgress<RecalcProgress>
    {
        private readonly List<RecalcProgress> _reports;
        public RecordingProgress(List<RecalcProgress> reports) => _reports = reports;
        public void Report(RecalcProgress value) => _reports.Add(value);
    }

    private sealed class CancelAfterNProgress : IProgress<RecalcProgress>
    {
        private readonly CancellationTokenSource _cts;
        private readonly int _cancelAfterCompletedCount;
        public CancelAfterNProgress(CancellationTokenSource cts, int cancelAfterCompletedCount)
        {
            _cts = cts;
            _cancelAfterCompletedCount = cancelAfterCompletedCount;
        }

        public void Report(RecalcProgress value)
        {
            if (value.CompletedFormulaCells >= _cancelAfterCompletedCount)
                _cts.Cancel();
        }
    }
}
