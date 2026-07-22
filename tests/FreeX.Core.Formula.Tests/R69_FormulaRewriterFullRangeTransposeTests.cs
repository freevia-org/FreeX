using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R69-commands-transpose-6-1: Paste Special &gt; Transpose did not rewrite a relative whole-column
/// (A:A) or whole-row (1:1) reference at all -- RewriteFullColumnRange/RewriteFullRowRange had no
/// PasteTransposeOp case, so the switch fell to "_ => range" and the reference was left literal even
/// though ordinary CellRefNodes are correctly axis-swapped by RewriteCellRefTranspose. Fixed by adding
/// PasteTransposeOp cases that convert a full-column ref into a full-row ref (and vice versa),
/// re-anchored from the paste origin the same way RewriteCellRefTranspose swaps a normal reference's
/// row/col axes.
/// </summary>
public class R69_FormulaRewriterFullRangeTransposeTests
{
    // Copy source anchored at A1 (row 1, col 1), pasted-transposed at D5 (row 5, col 4) --
    // matches the finding's "transpose-pasted from A1 to D5" example.
    private static readonly PasteTransposeOp FromA1ToD5 = new(
        SourceAnchorRow: 1, SourceAnchorCol: 1, DestAnchorRow: 5, DestAnchorCol: 4);

    [Fact]
    public void Transpose_FullColumnRange_RelativeRef_RewritesToFullRowRange()
    {
        // B:B is column 2. Transposing swaps axes: the column's own offset from the source
        // anchor's column (2 - 1 = 1) becomes a row offset from the destination anchor's row
        // (5 + 1 = 6) -- exactly the ROW branch of RewriteCellRefTranspose, just with no column
        // component to also rewrite (a full-column ref has none).
        var result = FormulaRewriter.Rewrite("SUM(B:B)", FromA1ToD5, "Sheet1");
        result.Should().Be("SUM(6:6)");
    }

    [Fact]
    public void Transpose_FullColumnRange_MultiColumnRef_RewritesBothEndpoints()
    {
        // B:C spans columns 2..3 → rows 6..7 after the same axis-swap re-anchor as above.
        var result = FormulaRewriter.Rewrite("SUM(B:C)", FromA1ToD5, "Sheet1");
        result.Should().Be("SUM(6:7)");
    }

    [Fact]
    public void Transpose_BinaryExpression_RewritesBothTerms()
    {
        // =A1+SUM(B:B) -- both the plain cell ref AND the full-column ref must be rewritten.
        // A1 (row 1, col 1): col branch -> DestAnchorCol(4) + (row1 - SourceAnchorRow1=0) = D;
        // row branch -> DestAnchorRow(5) + (col1 - SourceAnchorCol1=0) = 5 → D5.
        // B:B -> row 6, as above.
        var result = FormulaRewriter.Rewrite("A1+SUM(B:B)", FromA1ToD5, "Sheet1");
        result.Should().Be("D5+SUM(6:6)");
    }

    [Fact]
    public void Transpose_FullColumnRange_AbsoluteColumn_KeepsLiteralValueAsRow()
    {
        // $B:$B -- absolute column endpoints keep their literal numeric value (2) instead of being
        // recomputed from the offset, mirroring how RewriteCellRefTranspose leaves an absolute
        // axis' literal value untouched. Reinterpreted as a row, that's row 2, and the $ carries
        // over onto the row-absolute flags of the resulting full-row reference.
        var result = FormulaRewriter.Rewrite("SUM($B:$B)", FromA1ToD5, "Sheet1");
        result.Should().Be("SUM($2:$2)");
    }

    [Fact]
    public void Transpose_FullRowRange_RelativeRef_RewritesToFullColumnRange()
    {
        // 2:2 (row 2). Row offset from source anchor row (2 - 1 = 1) becomes a column offset
        // re-anchored at the destination column (4 + 1 = 5 → column E) -- the mirror image of the
        // full-column case.
        var result = FormulaRewriter.Rewrite("SUM(2:2)", FromA1ToD5, "Sheet1");
        result.Should().Be("SUM(E:E)");
    }

    [Fact]
    public void Transpose_FullRowRange_MultiRowRef_RewritesBothEndpoints()
    {
        // 2:3 spans rows 2..3 → columns E..F.
        var result = FormulaRewriter.Rewrite("SUM(2:3)", FromA1ToD5, "Sheet1");
        result.Should().Be("SUM(E:F)");
    }

    [Fact]
    public void Transpose_FullRowRange_AbsoluteRow_KeepsLiteralValueAsColumn()
    {
        // $2:$2 -- absolute row endpoints keep their literal numeric value (2), reinterpreted as
        // column 2 (column B), with the $ carrying over as column-absolute on the result.
        var result = FormulaRewriter.Rewrite("SUM($2:$2)", FromA1ToD5, "Sheet1");
        result.Should().Be("SUM($B:$B)");
    }

    // ── No-regression: ordinary (non-full) ranges must still transpose exactly as before ────────

    [Fact]
    public void Transpose_OrdinaryRange_StillAxisSwapsNormally()
    {
        // A1:B2 transposed from A1 to D5: A1 -> D5, B2 (col2,row2) -> col: DestAnchorCol4+(row2-1)=E,
        // row: DestAnchorRow5+(col2-1)=6 -> E6. So A1:B2 -> D5:E6.
        var result = FormulaRewriter.Rewrite("A1:B2", FromA1ToD5, "Sheet1");
        result.Should().Be("D5:E6");
    }

    [Fact]
    public void Transpose_PlainCellRef_Unaffected_ByFullRangeChange()
    {
        // Sanity check that the new full-column/full-row handling didn't disturb the existing
        // single-cell transpose path.
        var result = FormulaRewriter.Rewrite("B1", FromA1ToD5, "Sheet1");
        result.Should().Be("D6");
    }

    [Fact]
    public void Transpose_FullColumnRange_NoOtherOpTouchesIt()
    {
        // No-regression: a non-transpose op (plain paste offset) must NOT trigger the new
        // full-column transpose rewrite path -- it still uses RewriteFullColumnRangePaste.
        var result = FormulaRewriter.Rewrite("SUM(B:B)", new PasteOffsetOp(RowDelta: 0, ColDelta: 2), "Sheet1");
        result.Should().Be("SUM(D:D)");
    }
}
