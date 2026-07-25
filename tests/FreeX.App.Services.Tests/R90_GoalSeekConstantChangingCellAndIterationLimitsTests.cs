using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R90-app-goalseek-whatif-5-1 / R90-app-goalseek-whatif-5-2: exercises
/// <see cref="WorkbookSession.ExecuteGoalSeek"/> -- the real product entry point shared by both
/// desktop shells' Goal Seek command -- to confirm (a) Goal Seek refuses to run when the changing
/// cell itself holds a formula instead of a constant (matching Excel, which requires a value
/// there), and (b) it honors the workbook's configured Maximum Iterations / Maximum Change
/// settings instead of always using GoalSeekService.Seek's hardcoded 1000/1e-6 defaults.
/// </summary>
public sealed class R90_GoalSeekConstantChangingCellAndIterationLimitsTests
{
    [Fact]
    public void ExecuteGoalSeek_RejectsFormulaChangingCellWithoutOverwritingIt()
    {
        // A1=5 (price), A2="=A1*1.1" (marked-up price, formula), A3="=A2*10" (revenue).
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetFormula(a2, "A1*1.1");
        sheet.SetFormula(a3, "A2*10");
        var session = CreateSession(workbook);
        session.RecalculateWorkbook();

        var result = session.ExecuteGoalSeek(new GoalSeekRequest(a3, 100, a2));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(WorkbookGoalSeekStatus.InvalidRequest);
        result.ErrorMessage.Should().Be("Goal Seek changing cell must contain a constant value, not a formula.");
        result.SeekResult.Should().BeNull();
        result.EditResult.Should().BeNull();
        // The changing cell's formula must survive completely untouched.
        sheet.GetCell(a2)!.FormulaText.Should().Be("A1*1.1");
        GetNumber(sheet, a2).Should().BeApproximately(5.5, 1e-9);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    // No-regression sibling: a plain constant changing cell (the overwhelmingly common case) must
    // still be accepted and mutated by Goal Seek exactly as before.
    [Fact]
    public void ExecuteGoalSeek_AcceptsConstantChangingCell()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1*3");
        var session = CreateSession(workbook);

        var result = session.ExecuteGoalSeek(new GoalSeekRequest(b1, 12, a1));

        result.Success.Should().BeTrue();
        result.Status.Should().Be(WorkbookGoalSeekStatus.Applied);
        GetNumber(sheet, a1).Should().BeApproximately(4, 1e-4);
    }

    [Fact]
    public void ExecuteGoalSeek_HonorsWorkbookMaxIterationsAndReportsNonConvergence()
    {
        // A slowly-converging formula (tiny per-step response) that would normally converge well
        // within GoalSeekService.Seek's default 1000-iteration budget, but not within a
        // deliberately tiny workbook-configured Maximum Iterations of 1.
        var workbook = new Workbook("Book")
        {
            MaxCalculationIterations = 1,
            MaxCalculationChange = 1e-12
        };
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1*A1*A1*A1*A1");
        var session = CreateSession(workbook);

        var result = session.ExecuteGoalSeek(new GoalSeekRequest(b1, 100000, a1));

        result.Status.Should().Be(WorkbookGoalSeekStatus.NotConverged);
        result.Converged.Should().BeFalse();
        result.SeekResult.Should().NotBeNull();
        result.SeekResult!.Iterations.Should().BeLessThanOrEqualTo(1);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    // No-regression sibling: when the workbook doesn't configure Maximum Iterations/Change
    // (null, the common case), Goal Seek must still fall back to the same defaults it always used
    // and converge on an easy linear formula.
    [Fact]
    public void ExecuteGoalSeek_FallsBackToDefaultLimitsWhenWorkbookSettingsAreUnset()
    {
        var workbook = new Workbook("Book");
        workbook.MaxCalculationIterations.Should().BeNull();
        workbook.MaxCalculationChange.Should().BeNull();
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1*3");
        var session = CreateSession(workbook);

        var result = session.ExecuteGoalSeek(new GoalSeekRequest(b1, 12, a1));

        result.Success.Should().BeTrue();
        GetNumber(sheet, a1).Should().BeApproximately(4, 1e-4);
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static double GetNumber(Sheet sheet, CellAddress address) =>
        sheet.GetValue(address).Should().BeOfType<NumberValue>().Subject.Value;
}
