using Freexcel.Core.Formula;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

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
    public void DeleteRows_RangeRef_StartInDeletedRange_BecomesRef()
    {
        var result = FormulaRewriter.Rewrite("SUM(A3:A5)", new DeleteRowsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().Be("SUM(#REF!)");
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
}
