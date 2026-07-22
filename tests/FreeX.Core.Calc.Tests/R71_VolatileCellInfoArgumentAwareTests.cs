using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R71-calc-volatile-recalc-4-2: CELL and INFO are only SOMETIMES volatile in Excel.
/// CELL("width", ...) reports static column-width metadata (not live application state), and
/// INFO(...) is non-volatile for a fixed set of constant info-types (directory/numfile/origin/
/// osversion/recalc/release/system). Before this fix, RecalcEngine's dependency registration
/// treated the bare function NAMES "CELL" and "INFO" as unconditionally volatile, so every such
/// formula was force-included in every recalc pass (via RecalcEngine's _volatileCells set)
/// regardless of what actually changed -- a helper column of =CELL("width",B1) over a large table
/// recomputed on every unrelated edit.
/// </summary>
public sealed class R71_VolatileCellInfoArgumentAwareTests
{
    private static RecalcEngine Engine() =>
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

    private static (Workbook Workbook, Sheet Sheet, RecalcEngine Engine) SetUp()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();
        return (workbook, sheet, engine);
    }

    [Fact]
    public void Cell_Width_IsNotVolatile_DoesNotRecalcOnUnrelatedEdit()
    {
        var (workbook, sheet, engine) = SetUp();

        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var z1 = new CellAddress(sheet.Id, 100, 1); // unrelated -- nothing depends on it

        sheet.SetCell(b1, new NumberValue(10));
        sheet.SetFormula(a1, "CELL(\"width\",B1)");
        sheet.SetCell(z1, new NumberValue(1));

        engine.RecalculateAllFormulas(workbook);

        // Edit an unrelated cell that A1's formula does not reference at all.
        sheet.SetCell(z1, new NumberValue(2));
        var report = engine.Recalculate(workbook, [z1]);

        report.RecalculatedCells.Should().NotContain(a1,
            "CELL(\"width\", ...) reports static layout metadata and must not be treated as volatile");
    }

    [Fact]
    public void Cell_Format_IsStillVolatile_RecalculatesOnUnrelatedEdit()
    {
        var (workbook, sheet, engine) = SetUp();

        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var z1 = new CellAddress(sheet.Id, 100, 1);

        sheet.SetCell(b1, new NumberValue(10));
        sheet.SetFormula(a2, "CELL(\"format\",B1)");
        sheet.SetCell(z1, new NumberValue(1));

        engine.RecalculateAllFormulas(workbook);

        sheet.SetCell(z1, new NumberValue(2));
        var report = engine.Recalculate(workbook, [z1]);

        report.RecalculatedCells.Should().Contain(a2,
            "CELL(\"format\", ...) is not in the fixed non-volatile exemption and must stay volatile");
    }

    [Fact]
    public void Info_NumFile_IsNotVolatile_DoesNotRecalcOnUnrelatedEdit()
    {
        var (workbook, sheet, engine) = SetUp();

        var a3 = new CellAddress(sheet.Id, 3, 1);
        var z1 = new CellAddress(sheet.Id, 100, 1);

        sheet.SetFormula(a3, "INFO(\"numfile\")");
        sheet.SetCell(z1, new NumberValue(1));

        engine.RecalculateAllFormulas(workbook);

        sheet.SetCell(z1, new NumberValue(2));
        var report = engine.Recalculate(workbook, [z1]);

        report.RecalculatedCells.Should().NotContain(a3,
            "INFO(\"numfile\") is a fixed constant info-type and must not be treated as volatile");
    }

    [Fact]
    public void Info_Recalc_IsNotVolatile_DoesNotRecalcOnUnrelatedEdit()
    {
        var (workbook, sheet, engine) = SetUp();

        var a4 = new CellAddress(sheet.Id, 4, 1);
        var z1 = new CellAddress(sheet.Id, 100, 1);

        sheet.SetFormula(a4, "INFO(\"recalc\")");
        sheet.SetCell(z1, new NumberValue(1));

        engine.RecalculateAllFormulas(workbook);

        sheet.SetCell(z1, new NumberValue(2));
        var report = engine.Recalculate(workbook, [z1]);

        report.RecalculatedCells.Should().NotContain(a4,
            "INFO(\"recalc\") is a fixed constant info-type and must not be treated as volatile");
    }

    /// <summary>
    /// Sibling no-regression: a genuinely dynamic volatile function (OFFSET) must keep recalculating
    /// on every pass exactly as before this change -- the CELL/INFO argument-awareness must not leak
    /// into the unconditional volatility of every other function name.
    /// </summary>
    [Fact]
    public void Offset_IsStillVolatile_RecalculatesOnUnrelatedEdit()
    {
        var (workbook, sheet, engine) = SetUp();

        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a5 = new CellAddress(sheet.Id, 5, 1);
        var z1 = new CellAddress(sheet.Id, 100, 1);

        sheet.SetCell(b1, new NumberValue(10));
        sheet.SetFormula(a5, "OFFSET(B1,0,0)");
        sheet.SetCell(z1, new NumberValue(1));

        engine.RecalculateAllFormulas(workbook);

        sheet.SetCell(z1, new NumberValue(2));
        var report = engine.Recalculate(workbook, [z1]);

        report.RecalculatedCells.Should().Contain(a5,
            "OFFSET must remain unconditionally volatile, unaffected by the CELL/INFO argument-aware exemption");
    }
}
