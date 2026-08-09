using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// R124-formula-shift-1 (CF path): ViewportConditionalFormatEvaluator.Formulas.cs carries its own
// copy of HasRelativeReferences/ShiftAst (independent of FormulaEvaluator.Shifting.cs, since the
// two projects share no InternalsVisibleTo). Before this fix, that copy had the identical gap: a
// relative reference nested inside a UnionNode ("(A1,C1)") was never detected as relative, so
// GetShiftedConditionalFormatFormula returned the rule's literal, anchor-cell-only formula for
// EVERY cell in the applied range instead of re-anchoring per cell.
public partial class ConditionalFormatTests
{
    [Fact]
    public void Formula_Rule_UnionInsideFormula_ShiftsPerCell_ThroughRealViewportEvaluator()
    {
        var (wb, sheet) = MakeWorkbook();

        // Anchor row 1: A1=10 (nonzero -> anchor row satisfies the rule).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(0)));
        // Row 2: both A2 and C2 are 0, so the correctly-shifted rule (A2,C2) must NOT match.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), Cell.FromValue(new NumberValue(0)));
        // Row 3: A3=0 but C3=10, so the correctly-shifted rule (A3,C3) must match again.
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), Cell.FromValue(new NumberValue(10)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "SUM((A1,C1))>5", // relative union — must re-anchor per row
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        var red = new CellColor(255, 0, 0);
        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(red, "A1=10,C1=0 -> SUM=10>5 true at the anchor row");
        GetCell(vp, 2, 1).Style!.FillColor.Should().NotBe(red,
            "the union must re-anchor to (A2,C2)=0,0 -> SUM=0, not stay frozen at the anchor's (A1,C1)=10,0");
        GetCell(vp, 3, 1).Style!.FillColor.Should().Be(red, "the union must re-anchor to (A3,C3)=0,10 -> SUM=10>5 true");
    }

    // ── No-regression sibling ────────────────────────────────────────────────────────────────

    [Fact]
    public void Formula_Rule_UnionOfAbsoluteReferences_SameForAllCells_NoRegression()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(0)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "SUM(($A$1,$C$1))>5", // absolute union — must NOT re-anchor
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        var red = new CellColor(255, 0, 0);
        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(red);
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(red, "$A$1/$C$1 are absolute, so every row shares the anchor's result");
        GetCell(vp, 3, 1).Style!.FillColor.Should().Be(red, "$A$1/$C$1 are absolute, so every row shares the anchor's result");
    }
}
