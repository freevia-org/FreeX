using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Integration tests for COUNTIFS over a named range (Cluster B bug fix).
/// Reproduces the Calc(2)!C6 scenario from ExcelExamples1.xlsx where
/// COUNTIFS(selected.depts,"&lt;&gt;0") must count text cells (dept names)
/// and not count numeric zeros or blank cells.
/// </summary>
public sealed class CountifsNamedRangeRecalcTests
{
    private static RecalcEngine Engine() =>
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

    // ── Basic: COUNTIFS(named_range, "<>0") via RecalcEngine ─────────────────

    [Fact]
    public void RecalcEngine_CountifsNamedRange_TextCellsCountedNotZeros()
    {
        // Arrange: single sheet with selected.depts = B1:K1
        // B1:G1 = 6 text dept names; H1:K1 = 0 (numeric zeros)
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Calc (2)");

        var depts = new[] { "Finance", "HR", "IT", "Marketing", "Operations", "Sales" };
        for (int i = 0; i < depts.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(2 + i)), new TextValue(depts[i]));
        for (int i = 0; i < 4; i++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(8 + i)), new NumberValue(0));

        workbook.DefineNamedRange("selected.depts", new GridRange(
            new CellAddress(sheet.Id, 1, 2),   // B1
            new CellAddress(sheet.Id, 1, 11)));  // K1

        // A1 holds the formula: COUNTIFS(selected.depts,"<>0")
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1),
            "COUNTIFS(selected.depts,\"<>0\")");

        // Act
        Engine().RecalculateAllFormulas(workbook);

        // Assert: 6 text cells match "<>0"; 4 numeric zeros do not
        sheet.GetValue(1, 1).Should().Be(new NumberValue(6),
            "text dept names are not equal to 0; numeric zeros are equal to 0");
    }

    [Fact]
    public void RecalcEngine_CountifsNamedRange_BlankCellsNotCounted()
    {
        // Blank cells should NOT match "<>0" (Excel treats blank as 0 for this purpose)
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Calc (2)");

        var depts = new[] { "Finance", "HR", "IT", "Marketing", "Operations", "Sales" };
        for (int i = 0; i < depts.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(2 + i)), new TextValue(depts[i]));
        // H1:K1 left as blank (no cell set)

        workbook.DefineNamedRange("selected.depts", new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 1, 11)));

        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1),
            "COUNTIFS(selected.depts,\"<>0\")");

        Engine().RecalculateAllFormulas(workbook);

        // 6 text + 4 blanks → only 6 text match "<>0"
        sheet.GetValue(1, 1).Should().Be(new NumberValue(6),
            "blank cells are excluded from '<>0' matching");
    }

    // ── Full C6 scenario: COUNTIFS(...) <> SUMPRODUCT(1/COUNTIFS(...)) ────────

    [Fact]
    public void RecalcEngine_C6Formula_ReturnsExpectedFalse()
    {
        // Reproduces: COUNTIFS(selected.depts,"<>0") <> SUMPRODUCT(1/COUNTIFS(people[Department],people[Department]))
        // Excel answer: FALSE (both sides = 6, 6 <> 6 = FALSE)
        //
        // Simplification: instead of a structured table reference, use a plain range
        // for the people[Department] side (same semantics for COUNTIFS).

        var workbook = new Workbook("Test");
        var calcSheet = workbook.AddSheet("Calc (2)");
        var dataSheet = workbook.AddSheet("Inputs");

        // --- Set up selected.depts (B5:K5 on Calc (2)) ---
        var depts = new[] { "Finance", "HR", "IT", "Marketing", "Operations", "Sales" };
        for (int i = 0; i < 6; i++)
            calcSheet.SetCell(new CellAddress(calcSheet.Id, 5, (uint)(2 + i)), new TextValue(depts[i]));
        for (int i = 0; i < 4; i++)
            calcSheet.SetCell(new CellAddress(calcSheet.Id, 5, (uint)(8 + i)), new NumberValue(0));

        workbook.DefineNamedRange("selected.depts", new GridRange(
            new CellAddress(calcSheet.Id, 5, 2),
            new CellAddress(calcSheet.Id, 5, 11)));

        // --- Set up people table: 100 rows in dataSheet column A (A1:A100) ---
        // Dept distribution (matching real data): Finance=28, HR=11, IT=15, Marketing=16, Operations=16, Sales=14
        var distribution = new (string dept, int count)[]
        {
            ("Finance", 28), ("HR", 11), ("IT", 15), ("Marketing", 16), ("Operations", 16), ("Sales", 14)
        };
        uint row = 1;
        foreach (var (dept, count) in distribution)
            for (int j = 0; j < count; j++)
                dataSheet.SetCell(new CellAddress(dataSheet.Id, row++, 1), new TextValue(dept));

        // --- Set up C6 formula on Calc (2) ---
        // Use cross-sheet range 'Inputs'!A1:A100 instead of people[Department]
        calcSheet.SetFormula(new CellAddress(calcSheet.Id, 6, 3),
            "COUNTIFS(selected.depts,\"<>0\")<>SUMPRODUCT(1/COUNTIFS('Inputs'!A1:A100,'Inputs'!A1:A100))");

        // Act
        Engine().RecalculateAllFormulas(workbook);

        // Act
        Engine().RecalculateAllFormulas(workbook);

        // Assert: should be FALSE (both sides are numerically equal after 15-sig-digit rounding)
        // COUNTIFS(selected.depts,"<>0") = 6 exactly
        // SUMPRODUCT(1/COUNTIFS(...)) = 5.999999999999998 in raw double, but rounds to 6 via G15
        // Excel rounds to 15 sig digits before comparison, so 6 <> 5.999999999999998 → FALSE
        calcSheet.GetValue(6, 3).Should().Be(new BoolValue(false),
            "COUNTIFS(selected.depts,'<>0') = 6 and SUMPRODUCT ≈ 6 (rounds to 6 at 15 sig digits), so 6 <> 6 = FALSE");
    }

    [Fact]
    public void RecalcEngine_CountifsSelectedDeptsOnly_ReturnsSix()
    {
        // Minimal test: just COUNTIFS(selected.depts,"<>0") on a recalc engine context
        // matching the real harness setup where B5:K5 has 6 text names + 4 zeros
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Calc (2)");

        var depts = new[] { "Finance", "HR", "IT", "Marketing", "Operations", "Sales" };
        for (int i = 0; i < 6; i++)
            sheet.SetCell(new CellAddress(sheet.Id, 5, (uint)(2 + i)), new TextValue(depts[i]));
        for (int i = 0; i < 4; i++)
            sheet.SetCell(new CellAddress(sheet.Id, 5, (uint)(8 + i)), new NumberValue(0));

        workbook.DefineNamedRange("selected.depts", new GridRange(
            new CellAddress(sheet.Id, 5, 2),
            new CellAddress(sheet.Id, 5, 11)));

        sheet.SetFormula(new CellAddress(sheet.Id, 6, 3),
            "COUNTIFS(selected.depts,\"<>0\")");

        Engine().RecalculateAllFormulas(workbook);

        sheet.GetValue(6, 3).Should().Be(new NumberValue(6));
    }
}
