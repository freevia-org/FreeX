using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R21-lookup-reference-deep-2: FORMULATEXT/ISFORMULA (and bare range formulas) must normalize a
/// reversed range (e.g. B5:A1) to its top-left corner (A1:B5) before reading the anchor cell,
/// exactly like Excel does — not trust the first-typed corner literally.
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

        // "B5:A1" must normalize to "A1:B5", so the anchor cell is A1 (the formula), not B5.
        _eval.Evaluate("=FORMULATEXT(B5:A1)", sheet).Should().Be(new TextValue("=1+1"));
    }

    [Fact]
    public void IsFormula_UsesNormalizedTopLeftCell_ForReversedRange()
    {
        var sheet = Sheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { FormulaText = "1+1", Value = new NumberValue(2) });
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(99));

        _eval.Evaluate("=ISFORMULA(B5:A1)", sheet).Should().Be(new BoolValue(true));
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
