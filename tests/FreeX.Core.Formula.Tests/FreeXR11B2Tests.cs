using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-11 fix bucket R2 regression tests.
/// </summary>
public class FreeXR11B2Tests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    /// <summary>
    /// R11-formula-eval-1: a text-valued ordering criterion ("&lt;m") must only ever be satisfied
    /// by an actual text cell. A1=5 (number), A2="apple" (text): Excel's COUNTIF(A1:A2,"&lt;m")
    /// counts only "apple" (1 match) because numbers never satisfy a text ordering threshold —
    /// FreeX previously coerced the number to its text form ("5") and matched it too (2 matches).
    /// </summary>
    [Fact]
    public void Countif_TextOrderingCriteria_ExcludesNumericCells()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new TextValue("apple")));

        _eval.Evaluate("=COUNTIF(A1:A2,\"<m\")", sheet).Should().Be(new NumberValue(1));
    }

    /// <summary>
    /// Same root cause via SUMIF: a numeric A-cell must never satisfy a text-valued "&lt;" criterion,
    /// so its paired B-value must be excluded from the sum. A1=5 (number, text form "5" sorts
    /// before "m" ordinally so it wrongly matched pre-fix), A2="apple" (real text, correctly
    /// matches). Excel's SUMIF(A1:A2,"&lt;m",B1:B2) must only include the "apple" row.
    /// </summary>
    [Fact]
    public void Sumif_TextOrderingCriteria_ExcludesNumericCellsSumRange()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)), (1, 2, new NumberValue(100)),
            (2, 1, new TextValue("apple")), (2, 2, new NumberValue(7)));

        _eval.Evaluate("=SUMIF(A1:A2,\"<m\",B1:B2)", sheet).Should().Be(new NumberValue(7));
    }
}
