using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R21-lookup-reference-deep-2: FORMULATEXT/ISFORMULA (and bare range formulas) must normalize a
/// reversed range (e.g. B5:A1) to its top-left corner (A1:B5) before reading the anchor cell,
/// exactly like Excel does — not trust the first-typed corner literally.
///
/// R27-information-functions-deep-2 made FORMULATEXT/ISFORMULA spill one result per cell for a
/// genuinely multi-cell bounded range (matching Excel) instead of always collapsing to a single
/// top-left scalar, so "B5:A1" (which normalizes to the 5-row x 2-col A1:B5) now returns a
/// RangeValue here. These tests were updated to assert the top-left element of that array is
/// still anchored at A1 (not B5) — the original normalization guarantee, just expressed against
/// the new array-shaped result — and that the far corner (B5) is *not* mistaken for a formula.
/// </summary>
public sealed class R21_FormulaTextRange_ReversedRangeNormalization
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void FormulaText_UsesNormalizedTopLeftCell_ForReversedRange()
    {
        var sheet = Sheet();
        // A1 has a formula (with a cached value), B5 is a plain constant with no formula.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { FormulaText = "1+1", Value = new NumberValue(2) });
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(99));

        // "B5:A1" must normalize to "A1:B5", so the anchor (top-left, i.e. At(1,1)) cell is A1
        // (the formula), not B5. The far corner (B5, a plain constant) must report #N/A, not A1's
        // formula text.
        var result = _eval.Evaluate("=FORMULATEXT(B5:A1)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.At(1, 1).Should().Be(new TextValue("=1+1"));
        result.At(5, 2).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void IsFormula_UsesNormalizedTopLeftCell_ForReversedRange()
    {
        var sheet = Sheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { FormulaText = "1+1", Value = new NumberValue(2) });
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(99));

        // Same normalization guarantee for ISFORMULA: the anchor is A1 (true), and the far corner
        // B5 (a plain constant, not a formula) must report false, not A1's true.
        var result = _eval.Evaluate("=ISFORMULA(B5:A1)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.At(1, 1).Should().Be(new BoolValue(true));
        result.At(5, 2).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void BareRangeFormula_ReturnsNormalizedTopLeftCellValue_ForReversedRange()
    {
        var sheet = Sheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { FormulaText = "1+1", Value = new NumberValue(2) });
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(99));

        // A bare "=B5:A1" formula must return A1's value (2), not B5's (99).
        _eval.Evaluate("=B5:A1", sheet).Should().Be(new NumberValue(2));
    }

    private static Sheet Sheet() => new(SheetId.New(), "S");
}
