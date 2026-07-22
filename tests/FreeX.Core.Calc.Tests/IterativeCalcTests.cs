using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Tests for the iterative (circular-reference) calculation path in RecalcEngine.
/// When Workbook.IterativeCalculation is TRUE the engine must converge cyclic cells instead of
/// stamping #CIRCULAR!.  When it is FALSE (the default) the original behaviour is preserved.
/// </summary>
public class IterativeCalcTests
{
    private static (RecalcEngine engine, Workbook workbook, Sheet sheet) Setup(
        bool iterative = false,
        int? maxIterations = null,
        double? maxChange = null)
    {
        var workbook = new Workbook();
        workbook.IterativeCalculation = iterative;
        if (maxIterations.HasValue) workbook.MaxCalculationIterations = maxIterations.Value;
        if (maxChange.HasValue) workbook.MaxCalculationChange = maxChange.Value;
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        return (engine, workbook, sheet);
    }

    // -------------------------------------------------------------------------
    // IterativeCalculation = TRUE: converging cycles
    // -------------------------------------------------------------------------

    /// <summary>
    /// A1 = B1 + 1, B1 = A1 * 0.5.
    /// Fixed point: A1=2, B1=1 (because 2 = 1+1 and 1 = 2*0.5).
    /// Excel converges this with default settings.
    /// </summary>
    [Fact]
    public void IterativeCalc_TwoCellCycle_ConvergesToFixedPoint()
    {
        var (engine, wb, sheet) = Setup(iterative: true, maxIterations: 1000, maxChange: 0.0001);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetFormula(a1, "B1+1");
        sheet.SetFormula(b1, "A1*0.5");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty("iterative mode should not produce #CIRCULAR! cells");
        report.Errors.Should().NotContain(e => e.Error == "#CIRCULAR!");

        var a1Val = ((NumberValue)sheet.GetValue(a1.Row, a1.Col)).Value;
        var b1Val = ((NumberValue)sheet.GetValue(b1.Row, b1.Col)).Value;
        a1Val.Should().BeApproximately(2.0, 0.01, "A1=B1+1 converges to 2");
        b1Val.Should().BeApproximately(1.0, 0.01, "B1=A1*0.5 converges to 1");
    }

    /// <summary>
    /// A1 = A1/2 + 1 (self-reference).  Fixed point: A1 = 2 (because 2 = 2/2 + 1).
    /// </summary>
    [Fact]
    public void IterativeCalc_SelfReference_ConvergesToFixedPoint()
    {
        var (engine, wb, sheet) = Setup(iterative: true, maxIterations: 500, maxChange: 0.0001);
        var a1 = new CellAddress(sheet.Id, 1, 1);

        sheet.SetFormula(a1, "A1/2+1");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        var val = ((NumberValue)sheet.GetValue(a1.Row, a1.Col)).Value;
        val.Should().BeApproximately(2.0, 0.01, "A1=A1/2+1 converges to 2");
    }

    // -------------------------------------------------------------------------
    // IterativeCalculation = TRUE: divergent cycle — must TERMINATE, no hang
    // -------------------------------------------------------------------------

    /// <summary>
    /// A1 = A1 + 1 diverges (no fixed point).  The engine must still terminate after
    /// MaxCalculationIterations passes and return the last finite iterate — not hang.
    /// </summary>
    [Fact]
    public void IterativeCalc_DivergentCycle_TerminatesAfterMaxIterations()
    {
        const int maxIter = 10;
        var (engine, wb, sheet) = Setup(iterative: true, maxIterations: maxIter, maxChange: 0.0);
        var a1 = new CellAddress(sheet.Id, 1, 1);

        sheet.SetFormula(a1, "A1+1");

        // Should complete without hanging (test runner enforces a timeout implicitly).
        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty("iterative mode should not produce #CIRCULAR!");
        // The value must be a finite number (the last iterate), not an error.
        var cellVal = sheet.GetValue(a1.Row, a1.Col);
        cellVal.Should().BeOfType<NumberValue>("last iterate must be a number, not an error");
        var numericVal = ((NumberValue)cellVal).Value;
        double.IsFinite(numericVal).Should().BeTrue("last iterate must be finite");
        // After maxIter passes starting from 0, the value equals maxIter.
        numericVal.Should().BeApproximately(maxIter, 0.5,
            $"A1=A1+1 starting at 0 reaches {maxIter} after {maxIter} passes");
    }

    // -------------------------------------------------------------------------
    // IterativeCalculation = FALSE (default): #CIRCULAR! behaviour preserved
    // -------------------------------------------------------------------------

    /// <summary>
    /// With IterativeCalculation=FALSE (the default), a mutual cycle A1=B1, B1=A1 must
    /// still be reported circular, but — matching Excel — the cell VALUE seeds to 0 rather
    /// than a fabricated #CIRCULAR! error, so downstream arithmetic/IFERROR reads a real number.
    /// </summary>
    [Fact]
    public void NonIterative_Default_CircularReferenceSeedsZero()
    {
        var (engine, wb, sheet) = Setup(iterative: false); // default
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetFormula(a1, "B1");
        sheet.SetFormula(b1, "A1");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().NotBeEmpty("a plain cycle with IterativeCalculation=false must be reported");
        // Both cells must seed to 0 (Excel's non-iterative circular-reference behaviour), not a
        // fabricated error value.
        var a1Val = sheet.GetValue(a1.Row, a1.Col);
        var b1Val = sheet.GetValue(b1.Row, b1.Col);
        var zero = new NumberValue(0);
        (a1Val == zero && b1Val == zero)
            .Should().BeTrue("every cell in a non-iterative circular reference must seed to 0, not #CIRCULAR!");
    }

    // -------------------------------------------------------------------------
    // Normal acyclic workbook — no regression
    // -------------------------------------------------------------------------

    /// <summary>
    /// A standard acyclic workbook (A1=1, B1=A1*2, C1=B1+A1) must recalculate identically
    /// regardless of whether the iterative-calc branch is compiled in.
    /// </summary>
    [Fact]
    public void AcyclicWorkbook_RecalcsCorrectly_NoRegression()
    {
        var (engine, wb, sheet) = Setup(iterative: false);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);

        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1*2");
        sheet.SetFormula(c1, "B1+A1");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        report.Errors.Should().BeEmpty();
        sheet.GetValue(b1.Row, b1.Col).Should().Be(new NumberValue(2));
        sheet.GetValue(c1.Row, c1.Col).Should().Be(new NumberValue(3));
    }

    /// <summary>
    /// Same acyclic scenario but with IterativeCalculation=true — the iterative branch should
    /// not interfere with non-cyclic cells.
    /// </summary>
    [Fact]
    public void AcyclicWorkbook_WithIterativeEnabled_RecalcsCorrectly()
    {
        var (engine, wb, sheet) = Setup(iterative: true);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);

        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetFormula(b1, "A1+10");
        sheet.SetFormula(c1, "B1*2");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        report.Errors.Should().BeEmpty();
        sheet.GetValue(b1.Row, b1.Col).Should().Be(new NumberValue(15));
        sheet.GetValue(c1.Row, c1.Col).Should().Be(new NumberValue(30));
    }
}
