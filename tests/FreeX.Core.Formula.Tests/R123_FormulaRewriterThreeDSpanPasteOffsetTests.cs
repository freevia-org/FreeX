using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for the HIGH finding at FormulaRewriter.cs:402: RewriteRange's early-return
/// for 3-D sheet-span references (<c>EndSheetName</c> set, e.g. <c>Sheet1:Sheet3!A1</c>) only
/// special-cased <see cref="RenameSheetOp"/> and <see cref="DeleteSheetOp"/>; every other op --
/// including <see cref="PasteOffsetOp"/> and <see cref="PasteTransposeOp"/>, the ops used for
/// ordinary copy/paste and fill/autofill -- fell through to <c>return rr;</c> unchanged. In real
/// Excel, copying/filling a formula containing a 3-D span reference shifts the cell address exactly
/// like an ordinary relative reference while leaving the sheet span untouched. The fix adds an
/// explicit PasteOffsetOp/PasteTransposeOp branch ahead of the span early-return that rewrites
/// Start/End via the same <c>RewriteCellRef</c> machinery the non-span path already uses, without
/// touching SheetName/EndSheetName.
/// </summary>
public class R123_FormulaRewriterThreeDSpanPasteOffsetTests
{
    [Fact]
    public void PasteOffset_ThreeDSpanSingleCellRef_ShiftsCellAddress_KeepsSpan()
    {
        // Copy from B2, paste to B3 -> rowDelta=1, colDelta=0.
        // =SUM(Sheet1:Sheet3!A1) -> =SUM(Sheet1:Sheet3!A2), matching Excel: only the cell
        // address shifts, the sheet span text is untouched.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new PasteOffsetOp(1, 0),
            "Host");

        result.Should().Be("SUM(Sheet1:Sheet3!A2)");
    }

    [Fact]
    public void PasteOffset_ThreeDSpanRangeRef_ShiftsBothEndpoints_KeepsSpan()
    {
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1:B2)",
            new PasteOffsetOp(1, 1),
            "Host");

        result.Should().Be("SUM(Sheet1:Sheet3!B2:C3)");
    }

    [Fact]
    public void PasteOffset_ThreeDSpanRef_AbsoluteCell_Unchanged()
    {
        // Absolute row/col are immune to paste offset, same as an ordinary reference.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!$A$1)",
            new PasteOffsetOp(2, 2),
            "Host");

        result.Should().BeNull(); // no change
    }

    [Fact]
    public void PasteOffset_ThreeDSpanRef_OutOfBounds_BecomesRefError()
    {
        // Row 1, offset -2 -> row -1, out of bounds -> #REF!, same as an ordinary reference.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new PasteOffsetOp(-2, 0),
            "Host");

        result.Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void PasteTranspose_ThreeDSpanRef_AxisSwapsCellAddress_KeepsSpan()
    {
        // Transpose paste: relative offset from the source anchor is axis-swapped and
        // re-anchored at the destination anchor. Source anchor A1 (row1,col1) -> dest anchor A1.
        // A reference 1 row below the source anchor (A2) becomes 1 column right of the dest
        // anchor (B1) after transpose.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A2)",
            new PasteTransposeOp(SourceAnchorRow: 1, SourceAnchorCol: 1, DestAnchorRow: 1, DestAnchorCol: 1),
            "Host");

        result.Should().Be("SUM(Sheet1:Sheet3!B1)");
    }

    [Fact]
    public void PasteOffset_NonSpanSheetQualifiedRef_StillShifts()
    {
        // Sibling already-working case: an ordinary (non-3-D-span) sheet-qualified reference must
        // keep shifting exactly as before -- this fix must not regress the non-span path.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet2!A1)",
            new PasteOffsetOp(1, 0),
            "Host");

        result.Should().Be("SUM(Sheet2!A2)");
    }

    [Fact]
    public void RenameSheet_ThreeDSpanRef_StillRenamesSpanEndpoints()
    {
        // No-regression: RenameSheetOp handling for spans (added for a prior finding) must
        // continue to work now that PasteOffsetOp/PasteTransposeOp are handled ahead of it.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new RenameSheetOp("Sheet1", "Renamed"),
            "Host");

        result.Should().Be("SUM(Renamed:Sheet3!A1)");
    }

    [Fact]
    public void DeleteSheet_ThreeDSpanRef_StillCollapsesToRefError()
    {
        // No-regression: DeleteSheetOp handling for spans must continue to work now that
        // PasteOffsetOp/PasteTransposeOp are handled ahead of it.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new DeleteSheetOp("Sheet1"),
            "Host");

        result.Should().Be("SUM(#REF!)");
    }
}
