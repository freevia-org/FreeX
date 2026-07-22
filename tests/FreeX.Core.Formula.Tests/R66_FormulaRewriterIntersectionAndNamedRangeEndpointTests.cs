using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R66-meta-1 (r65 twin): the r65-added <see cref="IntersectionNode"/> and
/// <see cref="NamedRangeEndpointNode"/> (see R65_ExplicitIntersectionOperatorTests and
/// R65_NamedRangeColonEndpointTests) fell into FormulaRewriter.RewriteNode's "_ => node"
/// catch-all, so their CellRef/RangeRef operands were never shifted on Insert/Delete
/// Rows/Cols, Paste-with-offset, Move Range, or Sheet/Table rename -- e.g.
/// "=SUM(A1:C10 A5:E5)" and "=SUM(StartCell:B10)" kept their pre-insert references after a
/// row insert. Fixed by adding cases that recurse RewriteNode into both operands and rebuild,
/// mirroring how BinaryOpNode/FunctionCallNode already recurse.
/// </summary>
public sealed class R66_FormulaRewriterIntersectionAndNamedRangeEndpointTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void InsertRows_IntersectionNode_BothOperandsShift()
    {
        // Insert 1 row above row 1 on Sheet1 -- every ref on Sheet1 shifts down by one row.
        var result = FormulaRewriter.Rewrite(
            "SUM(A1:C10 A5:E5)", new InsertRowsOp("Sheet1", 1, 1), "Sheet1");

        result.Should().Be("SUM(A2:C11 A6:E6)");
    }

    [Fact]
    public void InsertRows_IntersectionNode_RewrittenFormula_StillEvaluatesCorrectIntersection()
    {
        // A1:D6 filled with row*10+col. A2:C11 (clamped to sheet) intersected with A6:E6
        // (the ranges the rewrite above produces) overlaps at row 6 columns A-C: A6=61,B6=62,C6=63.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint r = 1; r <= 6; r++)
            for (uint c = 1; c <= 5; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), new NumberValue(r * 10 + c));

        var rewritten = FormulaRewriter.Rewrite(
            "SUM(A1:C10 A5:E5)", new InsertRowsOp("Sheet1", 1, 1), "Sheet1");
        rewritten.Should().Be("SUM(A2:C11 A6:E6)");

        _eval.Evaluate("=" + rewritten, sheet).Should().Be(new NumberValue(61 + 62 + 63));
    }

    [Fact]
    public void InsertRows_NamedRangeEndpointNode_CellRefEndpointShifts_NameEndpointUnchanged()
    {
        // StartCell:B10 -- StartCell is a name (stays as-is), B10 is a CellRefNode endpoint that
        // must shift like any other cell reference.
        var result = FormulaRewriter.Rewrite(
            "SUM(StartCell:B10)", new InsertRowsOp("Sheet1", 1, 1), "Sheet1");

        result.Should().Be("SUM(StartCell:B11)");
    }

    [Fact]
    public void InsertRows_NamedRangeEndpointNode_BothCellRefEndpoints_BothShift()
    {
        var result = FormulaRewriter.Rewrite(
            "A1:EndName", new InsertRowsOp("Sheet1", 1, 1), "Sheet1");

        // A1:EndName -- A1 is the CellRefNode start endpoint and shifts; EndName is a name and
        // is left untouched apart from the lexer's normal identifier uppercasing (mirrors
        // PlainNamedRange behavior).
        result.Should().Be("A2:ENDNAME");
    }

    // --- No-regression siblings -------------------------------------------------------------

    [Fact]
    public void InsertRows_PlainRangeRef_StillShiftsUnchanged()
    {
        var result = FormulaRewriter.Rewrite("SUM(A1:C10)", new InsertRowsOp("Sheet1", 1, 1), "Sheet1");
        result.Should().Be("SUM(A2:C11)");
    }

    [Fact]
    public void InsertRows_PlainNamedRange_NoColonEndpoint_StillUnchanged()
    {
        var result = FormulaRewriter.Rewrite("SUM(PlainRange)", new InsertRowsOp("Sheet1", 1, 1), "Sheet1");
        result.Should().BeNull(); // a bare name carries no coordinates to shift
    }

    [Fact]
    public void InsertRows_IntersectionNode_AboveInsertPoint_NoChange()
    {
        // Insert below both operands -- neither side is affected, so Rewrite reports no change.
        var result = FormulaRewriter.Rewrite(
            "SUM(A1:C2 A1:E1)", new InsertRowsOp("Sheet1", 100, 1), "Sheet1");

        result.Should().BeNull();
    }
}
