using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R39-formula-information-2-1: ISFORMULA/FORMULATEXT previously returned FALSE/#N/A for the
/// non-anchor cells of a dynamic-array spill, because those cells have no Cell record of their
/// own — a spill member's value lives only in the sheet's spill overlay (Sheet._spillValues), so
/// TryGetCell resolved it to null and both functions treated that identically to a genuinely
/// blank cell. Excel treats every cell covered by a spill as part of the anchor's formula:
/// ISFORMULA(a spilled cell) is TRUE and FORMULATEXT returns the anchor's formula text. Fixed in
/// FormulaEvaluator.References.cs' TryGetCell by falling back to the spill anchor's own formula
/// cell (via Sheet.TryGetArrayExtent) when the direct cell lookup misses.
/// </summary>
public sealed class R39_IsFormulaFormulaText_SpillMemberTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheetWithSpilledSequence(uint anchorRow, uint anchorCol, int count)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var anchorAddr = new CellAddress(sheet.Id, anchorRow, anchorCol);
        sheet.SetFormula(anchorAddr, $"SEQUENCE({count})");

        var cells = new ScalarValue[count, 1];
        for (var i = 0; i < count; i++)
            cells[i, 0] = new NumberValue(i + 1);
        var rv = new RangeValue(cells, anchorRow, anchorCol);
        sheet.SetSpillRange(anchorAddr, rv);
        return sheet;
    }

    [Fact]
    public void IsFormula_SpillMemberCells_ReturnTrue()
    {
        // A1 = SEQUENCE(3), spilling into A1:A3. A2/A3 are spill members with no Cell record of
        // their own — Excel still reports ISFORMULA(A2)/ISFORMULA(A3) as TRUE.
        var sheet = MakeSheetWithSpilledSequence(1, 1, 3);

        _eval.Evaluate("=ISFORMULA(A1)", sheet).Should().Be(new BoolValue(true));
        _eval.Evaluate("=ISFORMULA(A2)", sheet).Should().Be(new BoolValue(true));
        _eval.Evaluate("=ISFORMULA(A3)", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void FormulaText_SpillMemberCells_ReturnAnchorFormulaText()
    {
        var sheet = MakeSheetWithSpilledSequence(1, 1, 3);

        _eval.Evaluate("=FORMULATEXT(A1)", sheet).Should().Be(new TextValue("=SEQUENCE(3)"));
        _eval.Evaluate("=FORMULATEXT(A2)", sheet).Should().Be(new TextValue("=SEQUENCE(3)"));
        _eval.Evaluate("=FORMULATEXT(A3)", sheet).Should().Be(new TextValue("=SEQUENCE(3)"));
    }

    // --- Sibling already-working cases, unchanged by this fix ---

    [Fact]
    public void IsFormula_GenuinelyBlankCell_StillReturnsFalse()
    {
        var sheet = MakeSheetWithSpilledSequence(1, 1, 3);

        // B1 has no formula, no value, and no spill membership at all — must stay FALSE/#N/A,
        // proving the spill-fallback only kicks in for actual spill members, not every blank cell.
        _eval.Evaluate("=ISFORMULA(B1)", sheet).Should().Be(new BoolValue(false));
        _eval.Evaluate("=FORMULATEXT(B1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void IsFormula_PlainConstantCell_StillReturnsFalse()
    {
        var sheet = MakeSheetWithSpilledSequence(1, 1, 3);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(42)));

        // B1 holds a plain constant (not a spill member, not a formula) — must stay FALSE/#N/A.
        _eval.Evaluate("=ISFORMULA(B1)", sheet).Should().Be(new BoolValue(false));
        _eval.Evaluate("=FORMULATEXT(B1)", sheet).Should().Be(ErrorValue.NA);
    }
}
