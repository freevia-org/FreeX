using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-13 fix bucket S1: <see cref="FormulaReferenceStyleService"/> R1C1 &lt;-&gt; A1 display/entry bugs.
/// One focused test per finding id (see r13-S1.md).
/// </summary>
public sealed class FreeXR13S1Tests
{
    /// <summary>
    /// R13-r1c1-mode-1: with R1C1 display enabled, a function call like LOG10( must never be
    /// mis-parsed as a cell reference (col "LOG" = 8509, row "10") just because it looks like a
    /// column+row token that happens to fall inside the grid. Only the following '(' is what makes
    /// this ambiguous with a genuine reference, so the fix rejects a match when followed by '('.
    /// </summary>
    [Fact]
    public void ToR1C1_DoesNotConvertFunctionNameThatLooksLikeColumnRowIntoAReference()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToR1C1("LOG10(A1)", anchor);

        result.Should().Be("LOG10(R[-2]C[-2])");
    }

    /// <summary>
    /// R13-r1c1-mode-2: entering a relative R1C1 reference in R1C1 mode whose resolved row/column
    /// falls outside the grid (e.g. R[-5] from row 2 lands on row -3) must store a valid A1 formula
    /// with a #REF! error token, matching R1C1FormulaConverter.FormatA1's file-format behavior -
    /// not leave the raw, unparseable R1C1 text embedded in the stored A1 formula.
    /// </summary>
    [Fact]
    public void ToA1_ConvertsOutOfRangeRelativeR1C1ReferenceToRefError()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 2, 1);

        var result = FormulaReferenceStyleService.ToA1("R[-5]C+1", anchor);

        result.Should().Be("#REF!+1");
    }

    /// <summary>
    /// R13-r1c1-mode-3: whole-row/whole-column references must be representable in both directions.
    /// Entering the R1C1 single-axis absolute forms "C1"/"R2" must produce real A1 whole-column/row
    /// ranges (not a truncated/unchanged token), and displaying a whole-column/row A1 range in R1C1
    /// mode must collapse to the single-axis form, not pass the A1 text through unconverted.
    /// </summary>
    [Fact]
    public void ToA1AndToR1C1_RepresentWholeColumnAndWholeRowReferences()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        FormulaReferenceStyleService.ToA1("SUM(C1)", anchor).Should().Be("SUM($A:$A)");
        FormulaReferenceStyleService.ToA1("SUM(R2)", anchor).Should().Be("SUM($2:$2)");

        FormulaReferenceStyleService.ToR1C1("SUM(A:A)", anchor).Should().Be("SUM(C1)");
        FormulaReferenceStyleService.ToR1C1("SUM(2:2)", anchor).Should().Be("SUM(R2)");
    }
}
