using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R36-formula-lookup-reference-3-1: OFFSET's base-reference argument previously only accepted a
/// plain cell/range reference (CellRefNode/RangeRefNode/full-row/full-col/named-range/nested
/// OFFSET-or-INDIRECT) — a spill (#) reference such as OFFSET(A1#,1,0), where A1 is a dynamic-array
/// spill anchor, fell into the `default` branch of EvaluateOffsetReference's base-argument switch
/// and returned #VALUE!, even though Excel treats A1# as a normal reference whose extent is the
/// anchor's current spill range. Fixed by resolving the "ANCHORARRAY" FunctionCallNode the parser
/// produces for A1# (WrapSpillAnchor in Parser.cs) via EvaluateAnchorArray, alongside the existing
/// nested OFFSET/INDIRECT base-argument case.
/// </summary>
public sealed class R36_OffsetSpillAnchorBaseTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheetWithSpill(uint anchorRow, uint anchorCol, params double[] spillValues)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var anchorAddr = new CellAddress(sheet.Id, anchorRow, anchorCol);
        for (var i = 0; i < spillValues.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, anchorRow + (uint)i, anchorCol),
                Cell.FromValue(new NumberValue(spillValues[i])));

        var cells = new ScalarValue[spillValues.Length, 1];
        for (var i = 0; i < spillValues.Length; i++)
            cells[i, 0] = new NumberValue(spillValues[i]);
        var rv = new RangeValue(cells, anchorRow, anchorCol);
        sheet.SetSpillRange(anchorAddr, rv);
        return sheet;
    }

    [Fact]
    public void Offset_WithSpillAnchorBase_ReturnsSingleCellWithinSpillExtent()
    {
        // A1 spills 10 (A1), 20 (A2), 30 (A3). OFFSET(A1#,1,0,1,1) starts from the spill's top-left
        // (A1) shifted down 1 row -> A2, matching Excel treating A1# as the base reference (the
        // 3x1 spill rectangle), not #VALUE!.
        var sheet = MakeSheetWithSpill(1, 1, 10, 20, 30);

        var result = _eval.Evaluate("=OFFSET(A1#,1,0,1,1)", sheet);

        result.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Offset_WithSpillAnchorBase_DefaultsHeightWidthToSpillExtent()
    {
        // Omitting height/width, OFFSET keeps the base's own shape (the 3x1 spill extent) and just
        // shifts it — so OFFSET(A1#,1,0) covers A2:A4, and A4 is blank (0).
        var sheet = MakeSheetWithSpill(1, 1, 10, 20, 30);

        var result = _eval.Evaluate("=SUM(OFFSET(A1#,1,0))", sheet);

        result.Should().Be(new NumberValue(20 + 30 + 0));
    }

    [Fact]
    public void Offset_WithSpillAnchorBase_AnchorNotASpill_ReturnsRefError()
    {
        // A1# on a cell that isn't actually a spill anchor is #REF! (same as bare =A1# would be),
        // not a crash or #VALUE!.
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(42)));

        var result = _eval.Evaluate("=OFFSET(A1#,1,0)", sheet);

        result.Should().Be(ErrorValue.Ref);
    }

    // --- Sibling already-working case, unchanged by this fix ---

    [Fact]
    public void Offset_WithPlainCellBase_StillWorks()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(99)));

        var result = _eval.Evaluate("=OFFSET(A1,0,1)", sheet);

        result.Should().Be(new NumberValue(99));
    }
}
