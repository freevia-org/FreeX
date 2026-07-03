using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public class FormulaRewriterTests
{
    // ── InsertRowsOp ──────────────────────────────────────────────────────────

    [Fact]
    public void InsertRows_RelativeRef_AtInsertPoint_ShiftsDown()
    {
        // Insert 1 row before row 3. =A3 is on "Sheet1" (same sheet) → =A4
        var result = FormulaRewriter.Rewrite("A3", new InsertRowsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().Be("A4");
    }

    [Fact]
    public void InsertRows_RelativeRef_AboveInsertPoint_Unchanged()
    {
        var result = FormulaRewriter.Rewrite("A2", new InsertRowsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().BeNull(); // no change
    }

    [Fact]
    public void InsertRows_AbsoluteRowRef_Shifts()
    {
        // Excel adjusts absolute references for structural row inserts.
        var result = FormulaRewriter.Rewrite("$A$3", new InsertRowsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().Be("$A$4");
    }

    [Fact]
    public void InsertRows_ColAbsoluteRowRelative_ShiftsRow()
    {
        // $A3 — col absolute, row relative → row shifts
        var result = FormulaRewriter.Rewrite("$A3", new InsertRowsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().Be("$A4");
    }

    [Fact]
    public void InsertRows_MultipleRows_ShiftsByCount()
    {
        var result = FormulaRewriter.Rewrite("A5", new InsertRowsOp("Sheet1", 3, 3), "Sheet1");
        result.Should().Be("A8");
    }

    [Fact]
    public void InsertRows_DifferentSheet_NoChange()
    {
        // Cell lives on Sheet2, op is on Sheet1 — no change
        var result = FormulaRewriter.Rewrite("A3", new InsertRowsOp("Sheet1", 3, 1), "Sheet2");
        result.Should().BeNull();
    }

    [Fact]
    public void InsertRows_CrossSheetRef_OnTargetSheet_Shifts()
    {
        // Formula =Sheet1!A3, cell lives on Sheet2, insert on Sheet1 → =Sheet1!A4
        var result = FormulaRewriter.Rewrite("Sheet1!A3", new InsertRowsOp("Sheet1", 3, 1), "Sheet2");
        result.Should().Be("Sheet1!A4");
    }

    [Fact]
    public void InsertRows_QuotedCrossSheetRef_OnTargetSheet_ShiftsAndPreservesQuotes()
    {
        var result = FormulaRewriter.Rewrite("'My Sheet'!A3", new InsertRowsOp("My Sheet", 3, 1), "Sheet2");
        result.Should().Be("'My Sheet'!A4");
    }

    [Fact]
    public void InsertRows_QuotedCrossSheetRef_WithApostrophe_ShiftsAndEscapesSheetName()
    {
        var result = FormulaRewriter.Rewrite("'Bob''s Sheet'!A3", new InsertRowsOp("Bob's Sheet", 3, 1), "Sheet2");
        result.Should().Be("'Bob''s Sheet'!A4");
    }

    [Fact]
    public void InsertRows_RangeRef_BothEndsShift()
    {
        var result = FormulaRewriter.Rewrite("SUM(A3:A10)", new InsertRowsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().Be("SUM(A4:A11)");
    }

    [Fact]
    public void InsertRows_FullRowRange_ShiftsAffectedEndpoint()
    {
        var result = FormulaRewriter.Rewrite("SUM(1:3)", new InsertRowsOp("Sheet1", 2, 1), "Sheet1");
        result.Should().Be("SUM(1:4)");
    }

    // ── DeleteRowsOp ──────────────────────────────────────────────────────────

    [Fact]
    public void DeleteRows_RefInDeletedRange_BecomesRef()
    {
        // Delete row 3. =A3 → =#REF!
        var result = FormulaRewriter.Rewrite("A3", new DeleteRowsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().Be("#REF!");
    }

    [Fact]
    public void DeleteRows_RefBelowDeletedRange_ShiftsUp()
    {
        // Delete row 3. =A5 → =A4
        var result = FormulaRewriter.Rewrite("A5", new DeleteRowsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().Be("A4");
    }

    [Fact]
    public void DeleteRows_RefAboveDeletedRange_Unchanged()
    {
        var result = FormulaRewriter.Rewrite("A2", new DeleteRowsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().BeNull();
    }

    [Fact]
    public void DeleteRows_AbsoluteRowRef_BelowDeleted_Shifts()
    {
        // Excel adjusts absolute references for structural row deletes.
        var result = FormulaRewriter.Rewrite("$A$5", new DeleteRowsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().Be("$A$4");
    }

    [Fact]
    public void DeleteRows_RangeRef_StartInDeletedRange_Shrinks()
    {
        // Delete row 3 (only the range's START endpoint). Excel SHRINKS the range to the surviving
        // rows rather than collapsing it to #REF!: A3:A5 → A3:A4 (old rows 4-5 shift up to 3-4).
        var result = FormulaRewriter.Rewrite("SUM(A3:A5)", new DeleteRowsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().Be("SUM(A3:A4)");
    }

    [Fact]
    public void DeleteRows_RangeRef_EndInDeletedRange_Shrinks()
    {
        // Delete row 5 (only the range's END endpoint). A3:A5 → A3:A4 (last surviving row is 4).
        var result = FormulaRewriter.Rewrite("SUM(A3:A5)", new DeleteRowsOp("Sheet1", 5, 1), "Sheet1");
        result.Should().Be("SUM(A3:A4)");
    }

    [Fact]
    public void DeleteRows_RangeRef_EntireRangeDeleted_BecomesRef()
    {
        // The whole range is inside the deleted band → #REF! (Excel's only #REF! case here).
        var result = FormulaRewriter.Rewrite("SUM(A3:A5)", new DeleteRowsOp("Sheet1", 3, 3), "Sheet1");
        result.Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void DeleteRows_RangeRef_BandInsideRange_Shrinks()
    {
        // Delete rows 4-5, strictly inside A3:A8 → A3:A6 (range shrinks by the deleted count).
        var result = FormulaRewriter.Rewrite("SUM(A3:A8)", new DeleteRowsOp("Sheet1", 4, 2), "Sheet1");
        result.Should().Be("SUM(A3:A6)");
    }

    [Fact]
    public void DeleteCols_RangeRef_StartInDeletedRange_Shrinks()
    {
        // Delete column B (the range's START). B1:D1 → B1:C1 (old cols C-D shift left to B-C).
        var result = FormulaRewriter.Rewrite("SUM(B1:D1)", new DeleteColsOp("Sheet1", 2, 1), "Sheet1");
        result.Should().Be("SUM(B1:C1)");
    }

    [Fact]
    public void DeleteCols_RangeRef_EntireRangeDeleted_BecomesRef()
    {
        var result = FormulaRewriter.Rewrite("SUM(B1:D1)", new DeleteColsOp("Sheet1", 2, 3), "Sheet1");
        result.Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void DeleteRows_FullRowRange_DeletedEndpointBecomesRef()
    {
        var result = FormulaRewriter.Rewrite("SUM(1:3)", new DeleteRowsOp("Sheet1", 1, 1), "Sheet1");
        result.Should().Be("SUM(#REF!)");
    }

    // ── DeleteCellsShiftUpOp ──────────────────────────────────────────────────

    [Fact]
    public void DeleteCellsShiftUp_RangeRef_StartInDeletedBand_Shrinks()
    {
        // Delete A10:A12 shift-up (column band A..A). SUM(A11:A20)'s start (row 11) falls
        // inside the deleted band [10..12] but the end (row 20) survives. Excel SHRINKS the
        // range to the surviving rows rather than collapsing it to #REF!: A11:A20 → A10:A17
        // (surviving rows 13-20 slide up to 10-17).
        var op = new DeleteCellsShiftUpOp("Sheet1", DeletedStartRow: 10, DeletedEndRow: 12, BandEndRow: 1048576,
            RangeStartCol: 1, RangeEndCol: 1, Count: 3);
        var result = FormulaRewriter.Rewrite("SUM(A11:A20)", op, "Sheet1");
        result.Should().Be("SUM(A10:A17)");
    }

    [Fact]
    public void DeleteCellsShiftUp_RangeRef_EndInDeletedBand_Shrinks()
    {
        // Delete A15:A17 shift-up. SUM(A10:A16)'s end (row 16) falls inside the deleted band
        // [15..17] but the start (row 10) survives above it. Shrinks to A10:A14 (last
        // surviving row before the band is 14).
        var op = new DeleteCellsShiftUpOp("Sheet1", DeletedStartRow: 15, DeletedEndRow: 17, BandEndRow: 1048576,
            RangeStartCol: 1, RangeEndCol: 1, Count: 3);
        var result = FormulaRewriter.Rewrite("SUM(A10:A16)", op, "Sheet1");
        result.Should().Be("SUM(A10:A14)");
    }

    [Fact]
    public void DeleteCellsShiftUp_RangeRef_EntireRangeInDeletedBand_BecomesRef()
    {
        var op = new DeleteCellsShiftUpOp("Sheet1", DeletedStartRow: 10, DeletedEndRow: 20, BandEndRow: 1048576,
            RangeStartCol: 1, RangeEndCol: 1, Count: 11);
        var result = FormulaRewriter.Rewrite("SUM(A11:A15)", op, "Sheet1");
        result.Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void DeleteCellsShiftUp_RangeRef_OutsideColumnBand_Unchanged()
    {
        // Delete band is column A only (RangeStartCol=RangeEndCol=1). A range in column B is
        // untouched even though its rows overlap the deleted row band.
        var op = new DeleteCellsShiftUpOp("Sheet1", DeletedStartRow: 10, DeletedEndRow: 12, BandEndRow: 1048576,
            RangeStartCol: 1, RangeEndCol: 1, Count: 3);
        var result = FormulaRewriter.Rewrite("SUM(B11:B20)", op, "Sheet1");
        result.Should().BeNull();
    }

    // ── DeleteCellsShiftLeftOp ────────────────────────────────────────────────

    [Fact]
    public void DeleteCellsShiftLeft_RangeRef_StartInDeletedBand_Shrinks()
    {
        // Delete J1:L1 shift-left (row band 1..1). SUM(K1:T1)'s start (col K=11) falls inside
        // the deleted band [10..12] but the end (col T=20) survives. Shrinks to J1:Q1
        // (surviving cols M-T slide left to J-Q).
        var op = new DeleteCellsShiftLeftOp("Sheet1", BandStartRow: 1, BandEndRow: 1,
            DeletedStartCol: 10, DeletedEndCol: 12, BandEndCol: 16384, Count: 3);
        var result = FormulaRewriter.Rewrite("SUM(K1:T1)", op, "Sheet1");
        result.Should().Be("SUM(J1:Q1)");
    }

    [Fact]
    public void DeleteCellsShiftLeft_RangeRef_EndInDeletedBand_Shrinks()
    {
        // Delete O1:Q1 shift-left. SUM(J1:P1)'s end (col P=16) falls inside the deleted band
        // [15..17] but the start (col J=10) survives to the left of it. Shrinks to J1:N1.
        var op = new DeleteCellsShiftLeftOp("Sheet1", BandStartRow: 1, BandEndRow: 1,
            DeletedStartCol: 15, DeletedEndCol: 17, BandEndCol: 16384, Count: 3);
        var result = FormulaRewriter.Rewrite("SUM(J1:P1)", op, "Sheet1");
        result.Should().Be("SUM(J1:N1)");
    }

    [Fact]
    public void DeleteCellsShiftLeft_RangeRef_EntireRangeInDeletedBand_BecomesRef()
    {
        var op = new DeleteCellsShiftLeftOp("Sheet1", BandStartRow: 1, BandEndRow: 1,
            DeletedStartCol: 10, DeletedEndCol: 20, BandEndCol: 16384, Count: 11);
        var result = FormulaRewriter.Rewrite("SUM(K1:O1)", op, "Sheet1");
        result.Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void DeleteCellsShiftLeft_RangeRef_OutsideRowBand_Unchanged()
    {
        // Delete band is row 1 only (BandStartRow=BandEndRow=1). A range on row 2 is
        // untouched even though its columns overlap the deleted column band.
        var op = new DeleteCellsShiftLeftOp("Sheet1", BandStartRow: 1, BandEndRow: 1,
            DeletedStartCol: 10, DeletedEndCol: 12, BandEndCol: 16384, Count: 3);
        var result = FormulaRewriter.Rewrite("SUM(K2:T2)", op, "Sheet1");
        result.Should().BeNull();
    }

    // ── InsertColsOp ─────────────────────────────────────────────────────────

    [Fact]
    public void InsertCols_RelativeRef_AtInsertPoint_ShiftsRight()
    {
        // Insert 1 col before col 2 (B). =B1 → =C1
        var result = FormulaRewriter.Rewrite("B1", new InsertColsOp("Sheet1", 2, 1), "Sheet1");
        result.Should().Be("C1");
    }

    [Fact]
    public void InsertCols_AbsoluteColRef_Shifts()
    {
        var result = FormulaRewriter.Rewrite("$B1", new InsertColsOp("Sheet1", 2, 1), "Sheet1");
        result.Should().Be("$C1");
    }

    [Fact]
    public void InsertCols_FullColumnRange_ShiftsAffectedEndpoint()
    {
        var result = FormulaRewriter.Rewrite("SUM(A:C)", new InsertColsOp("Sheet1", 2, 1), "Sheet1");
        result.Should().Be("SUM(A:D)");
    }

    // ── DeleteColsOp ─────────────────────────────────────────────────────────

    [Fact]
    public void DeleteCols_RefInDeletedCol_BecomesRef()
    {
        var result = FormulaRewriter.Rewrite("B1", new DeleteColsOp("Sheet1", 2, 1), "Sheet1");
        result.Should().Be("#REF!");
    }

    [Fact]
    public void DeleteCols_RefRightOfDeletedCol_ShiftsLeft()
    {
        var result = FormulaRewriter.Rewrite("D1", new DeleteColsOp("Sheet1", 2, 1), "Sheet1");
        result.Should().Be("C1");
    }

    [Fact]
    public void DeleteCols_FullColumnRange_DeletedEndpointBecomesRef()
    {
        var result = FormulaRewriter.Rewrite("SUM(A:C)", new DeleteColsOp("Sheet1", 1, 1), "Sheet1");
        result.Should().Be("SUM(#REF!)");
    }

    // ── PasteOffsetOp ─────────────────────────────────────────────────────────

    [Fact]
    public void PasteOffset_RelativeRef_ShiftsByOffset()
    {
        // Copy from C1, paste to E3 → rowDelta=2, colDelta=2. =A1 → =C3
        var result = FormulaRewriter.Rewrite("A1", new PasteOffsetOp(2, 2), "Sheet1");
        result.Should().Be("C3");
    }

    [Fact]
    public void PasteOffset_AbsoluteRef_Unchanged()
    {
        var result = FormulaRewriter.Rewrite("$A$1", new PasteOffsetOp(2, 2), "Sheet1");
        result.Should().BeNull();
    }

    [Fact]
    public void PasteOffset_ColAbsoluteRowRelative_OnlyRowShifts()
    {
        var result = FormulaRewriter.Rewrite("$A1", new PasteOffsetOp(2, 2), "Sheet1");
        result.Should().Be("$A3");
    }

    [Fact]
    public void PasteOffset_OutOfBounds_BecomesRef()
    {
        // Row 1, offset -2 → row -1 → #REF!
        var result = FormulaRewriter.Rewrite("A1", new PasteOffsetOp(-2, 0), "Sheet1");
        result.Should().Be("#REF!");
    }

    [Fact]
    public void PasteOffset_RangeRef_BothEndsShift()
    {
        var result = FormulaRewriter.Rewrite("SUM(A1:A3)", new PasteOffsetOp(1, 1), "Sheet1");
        result.Should().Be("SUM(B2:B4)");
    }

    [Fact]
    public void PasteOffset_FullColumnRange_ShiftsRelativeColumns()
    {
        var result = FormulaRewriter.Rewrite("SUM(A:B)", new PasteOffsetOp(5, 2), "Sheet1");
        result.Should().Be("SUM(C:D)");
    }

    [Fact]
    public void PasteOffset_FullRowRange_ShiftsRelativeRows()
    {
        var result = FormulaRewriter.Rewrite("SUM(1:2)", new PasteOffsetOp(3, 5), "Sheet1");
        result.Should().Be("SUM(4:5)");
    }

    [Fact]
    public void PasteOffset_DynamicArrayFormulaWithOmittedArgument_PreservesOmittedSlot()
    {
        var result = FormulaRewriter.Rewrite("EXPAND(A1:B1,,3)", new PasteOffsetOp(1, 1), "Sheet1");
        result.Should().Be("EXPAND(B2:C2,,3)");
    }

    [Fact]
    public void PasteOffset_ModernErrorLiteral_PreservesErrorToken()
    {
        var result = FormulaRewriter.Rewrite("IFERROR(A1,#CALC!)", new PasteOffsetOp(1, 1), "Sheet1");
        result.Should().Be("IFERROR(B2,#CALC!)");
    }

    [Fact]
    public void MoveRange_CellRefsInsideSource_RetargetEvenWhenAbsolute()
    {
        var op = new MoveRangeOp("Sheet1", 1, 1, 1, 2, 2, 2);

        var result = FormulaRewriter.Rewrite("A1+$A$1+B1", op, "Sheet1");

        result.Should().Be("C3+$C$3+D3");
    }

    [Fact]
    public void MoveRange_CellRefsOutsideSource_AreUnchanged()
    {
        var op = new MoveRangeOp("Sheet1", 1, 2, 1, 2, 2, 2);

        var result = FormulaRewriter.Rewrite("A1+$A$1+SUM(A1:A2)", op, "Sheet1");

        result.Should().BeNull();
    }

    [Fact]
    public void MoveRange_RangeRef_RewritesOnlyWhenBothEndpointsMoved()
    {
        var op = new MoveRangeOp("Sheet1", 1, 1, 1, 2, 2, 2);

        var result = FormulaRewriter.Rewrite("SUM(A1:B1)+SUM(A1:C1)", op, "Sheet1");

        result.Should().Be("SUM(C3:D3)+SUM(A1:C1)");
    }

    [Fact]
    public void MoveRange_SingleCellEndpointMove_ExpandsOneAxisRangeOnlyWhenMovingOutward()
    {
        var op = new MoveRangeOp("Sheet1", 1, 2, 1, 2, 0, 3);

        var result = FormulaRewriter.Rewrite("SUM(A1:B1)+SUM(B1:C1)", op, "Sheet1");

        result.Should().Be("SUM(A1:E1)+SUM(B1:C1)");
    }

    [Fact]
    public void Rewrite_ParseFailure_ReturnsNull()
    {
        // Malformed formula should not throw — returns null
        var result = FormulaRewriter.Rewrite("BROKEN(((", new InsertRowsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().BeNull();
    }

    [Fact]
    public void Rewrite_NoRefsInRange_ReturnsNull()
    {
        // Formula has no refs that need changing
        var result = FormulaRewriter.Rewrite("1+2", new InsertRowsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().BeNull();
    }

    [Fact]
    public void RenameSheet_QuotedCrossSheetRange_RewritesSheetName()
    {
        var result = FormulaRewriter.Rewrite(
            "SUM('Old Sheet'!A1:B2)",
            new RenameSheetOp("Old Sheet", "New Sheet"),
            "Host");

        result.Should().Be("SUM('New Sheet'!A1:B2)");
    }

    [Fact]
    public void RenameSheet_FullColumnAndFullRowRanges_RewriteSheetName()
    {
        FormulaRewriter.Rewrite(
                "SUM('Old Sheet'!A:B)",
                new RenameSheetOp("Old Sheet", "New Sheet"),
                "Host")
            .Should().Be("SUM('New Sheet'!A:B)");

        FormulaRewriter.Rewrite(
                "SUM('Old Sheet'!1:2)",
                new RenameSheetOp("Old Sheet", "New Sheet"),
                "Host")
            .Should().Be("SUM('New Sheet'!1:2)");
    }

    [Fact]
    public void DeleteSheet_FullColumnAndFullRowRanges_BecomeRef()
    {
        FormulaRewriter.Rewrite(
                "SUM(Sheet1!A:B)",
                new DeleteSheetOp("Sheet1"),
                "Host")
            .Should().Be("SUM(#REF!)");

        FormulaRewriter.Rewrite(
                "SUM(Sheet1!1:2)",
                new DeleteSheetOp("Sheet1"),
                "Host")
            .Should().Be("SUM(#REF!)");
    }
}
