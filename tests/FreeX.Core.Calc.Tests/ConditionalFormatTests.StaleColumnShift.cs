using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// ── Regression coverage for group B-stale-colnumber (finding J3) ──────────────
//
// ViewportConditionalFormatEvaluator.Formulas.cs has its own independent copy of
// the relative-reference shifter used for Formula-type conditional formats that
// don't reduce to a bare comparison or an AND-of-comparisons (e.g. ISBLANK, or
// any other function call). Its ShiftCellRefOrError previously built the shifted
// CellRefNode via `cr with { Row = ..., ColumnName = ... }`, which leaves the
// ColumnNumber backing field stale (a record `with` expression does not re-run
// the field initializer that computes ColumnNumber from ColumnName). Because the
// evaluator resolves a CellRefNode via ColumnNumber (not ColumnName), this
// silently re-checked the ANCHOR's column instead of the cell actually being
// rendered, for any multi-column AppliesTo range with a non-comparison formula
// rule and a column-relative reference.
public partial class ConditionalFormatTests
{
    [Fact]
    public void Formula_Rule_NonComparisonFunction_ShiftsRelativeColumnReference()
    {
        var (wb, sheet) = MakeWorkbook();

        // Anchor A1 is blank; C1 holds a value, so ISBLANK(C1) must be FALSE for C1
        // and ISBLANK(A1) (the un-shifted, buggy read) would be TRUE.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(42)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            // ISBLANK is a function call, not a comparison — it does not reduce to the
            // TryCreateSimpleComparison/TryCreateSimpleAnd fast path, so it exercises the
            // general AST-shift path (ShiftCellRefOrError) directly.
            FormulaText = "ISBLANK(A1)",
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(255, 0, 0), "A1 (anchor) is blank");
        GetCell(vp, 1, 2).Style!.FillColor.Should().Be(new CellColor(255, 0, 0), "B1 (shifted to ISBLANK(B1)) is blank");
        GetCell(vp, 1, 3).Style!.FillColor.Should().NotBe(
            new CellColor(255, 0, 0),
            "C1 holds a value, so the correctly-shifted ISBLANK(C1) must be FALSE " +
            "even though the stale-ColumnNumber bug would have re-checked blank A1 and matched");
    }

    [Fact]
    public void FullColumnRangeRef_NonComparisonFunction_ShiftsRelativeColumn()
    {
        var (wb, sheet) = MakeWorkbook();

        // Column A sums to 0 (blank); column C sums to a non-zero value. SUM(A:A)=0 is
        // a function call over a full-column range wrapped in a comparison against a
        // literal, so it exercises ShiftFullColumnRangeRef (not the simple-comparison
        // fast path, since the comparison's left operand is a function call, not a bare
        // cell reference).
        sheet.SetCell(new CellAddress(sheet.Id, 50, 3), Cell.FromValue(new NumberValue(7)));
        // C1 must have an explicit (zero) value so it is a non-blank cell. A blank cell
        // whose conditional format does not match is intentionally omitted from the
        // viewport's cell list (nothing to render over the default style/blank content),
        // so without this the C1 assertion below would find no matching DisplayCell at
        // all instead of a non-red one. C1=0 does not change SUM(C:C), which stays 7.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(0)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "SUM(A:A)=0",
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        // A1 (anchor, dc=0): SUM(A:A)=0 → TRUE (column A is entirely blank).
        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(255, 0, 0), "SUM(A:A)=0 is true at the anchor");
        // C1 (dc=2): must shift to SUM(C:C)=0, which is FALSE since C50=7. Under the
        // stale-ColumnNumber bug, the shifted range's StartColumnNumber/EndColumnNumber
        // would still point at column A, so this would incorrectly match TRUE.
        GetCell(vp, 1, 3).Style!.FillColor.Should().NotBe(
            new CellColor(255, 0, 0),
            "C1 must evaluate the shifted SUM(C:C)=0, which is false because C50=7");
    }
}
