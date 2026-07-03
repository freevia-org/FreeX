using FreeX.Core.Model;

namespace FreeX.Core.Formula;

// ── Operation types ───────────────────────────────────────────────────────────

public abstract record RewriteOperation;
public sealed record InsertRowsOp(string SheetName, uint BeforeRow, uint Count) : RewriteOperation;
public sealed record DeleteRowsOp(string SheetName, uint StartRow,  uint Count) : RewriteOperation;
public sealed record InsertColsOp(string SheetName, uint BeforeCol, uint Count) : RewriteOperation;
public sealed record DeleteColsOp(string SheetName, uint StartCol,  uint Count) : RewriteOperation;
public sealed record PasteOffsetOp(int RowDelta, int ColDelta)                  : RewriteOperation;
public sealed record MoveRangeOp(
    string SheetName,
    uint SourceStartRow,
    uint SourceStartCol,
    uint SourceEndRow,
    uint SourceEndCol,
    int RowDelta,
    int ColDelta)                                                               : RewriteOperation;
public sealed record RenameSheetOp(string OldSheetName, string NewSheetName)    : RewriteOperation;
public sealed record DeleteSheetOp(string SheetName)                            : RewriteOperation;

// ── Partial-range (Insert/Delete Cells) operations ────────────────────────────
// These shift only cells whose address falls inside the constrained band
// (StartRow..EndRow × StartCol..MaxCol for ShiftRight, StartRow..MaxRow × StartCol..EndCol for ShiftDown).
// References completely outside the band are left untouched.
// References to cells that are removed by a delete-cells op become #REF!.

/// <summary>
/// Insert Cells / Shift Down: cells in rows [<see cref="BandStartRow"/>..<see cref="BandEndRow"/>]
/// inside column [<see cref="RangeStartCol"/>..<see cref="RangeEndCol"/>] were pushed down by <see cref="Count"/> rows.
/// </summary>
public sealed record InsertCellsShiftDownOp(
    string SheetName,
    uint BandStartRow,  // _range.Start.Row
    uint BandEndRow,    // CellAddress.MaxRow (the full shift region goes to the bottom)
    uint RangeStartCol, // _range.Start.Col
    uint RangeEndCol,   // _range.End.Col
    uint InsertBeforeRow, // _range.Start.Row (first blank row after insert)
    uint Count)         // _range.RowCount
    : RewriteOperation;

/// <summary>
/// Insert Cells / Shift Right: cells in rows [<see cref="BandStartRow"/>..<see cref="BandEndRow"/>]
/// in column [<see cref="RangeStartCol"/>..<see cref="BandEndCol"/>] were pushed right by <see cref="Count"/> columns.
/// </summary>
public sealed record InsertCellsShiftRightOp(
    string SheetName,
    uint BandStartRow,    // _range.Start.Row
    uint BandEndRow,      // _range.End.Row
    uint RangeStartCol,   // _range.Start.Col
    uint BandEndCol,      // CellAddress.MaxCol (the full shift region goes to the right)
    uint InsertBeforeCol, // _range.Start.Col
    uint Count)           // _range.ColCount
    : RewriteOperation;

/// <summary>
/// Delete Cells / Shift Up: cells in rows [<see cref="DeletedStartRow"/>..<see cref="DeletedEndRow"/>]
/// inside column [<see cref="RangeStartCol"/>..<see cref="RangeEndCol"/>] were removed, and cells below shifted up.
/// </summary>
public sealed record DeleteCellsShiftUpOp(
    string SheetName,
    uint DeletedStartRow, // _range.Start.Row
    uint DeletedEndRow,   // _range.End.Row
    uint BandEndRow,      // CellAddress.MaxRow
    uint RangeStartCol,   // _range.Start.Col
    uint RangeEndCol,     // _range.End.Col
    uint Count)           // _range.RowCount
    : RewriteOperation;

/// <summary>
/// Delete Cells / Shift Left: cells in rows [<see cref="BandStartRow"/>..<see cref="BandEndRow"/>]
/// in columns [<see cref="DeletedStartCol"/>..<see cref="DeletedEndCol"/>] were removed, and cells to the right shifted left.
/// </summary>
public sealed record DeleteCellsShiftLeftOp(
    string SheetName,
    uint BandStartRow,    // _range.Start.Row
    uint BandEndRow,      // _range.End.Row
    uint DeletedStartCol, // _range.Start.Col
    uint DeletedEndCol,   // _range.End.Col
    uint BandEndCol,      // CellAddress.MaxCol
    uint Count)           // _range.ColCount
    : RewriteOperation;

// ── Rewriter ─────────────────────────────────────────────────────────────────

/// <summary>
/// Rewrites cell references in a formula string according to a structural operation
/// (insert/delete rows or columns, or paste offset). Returns null when no references
/// were changed so callers can skip the write-back.
/// </summary>
public static class FormulaRewriter
{
    /// <summary>
    /// Rewrites all CellRefNodes in <paramref name="formulaText"/> according to
    /// <paramref name="op"/>. <paramref name="hostSheetName"/> is the sheet the cell
    /// lives on — used to decide whether sheet-unqualified refs should be adjusted.
    /// Returns null when no refs were modified.
    /// </summary>
    public static string? Rewrite(string formulaText, RewriteOperation op, string hostSheetName)
    {
        try
        {
            var tokens = new Lexer(formulaText).Tokenize();
            var ast    = new Parser(tokens).Parse();
            bool changed = false;
            var rewritten = RewriteNode(ast, op, hostSheetName, ref changed);
            return changed ? FormulaSerializer.Serialize(rewritten) : null;
        }
        catch
        {
            return null;   // malformed formula — leave untouched
        }
    }

    private static FormulaNode RewriteNode(
        FormulaNode node, RewriteOperation op, string hostSheetName, ref bool changed)
    {
        return node switch
        {
            CellRefNode cr  => RewriteCellRef(cr, op, hostSheetName, ref changed),
            RangeRefNode rr => RewriteRange(rr, op, hostSheetName, ref changed),
            FullColumnRangeRefNode fcr => RewriteFullColumnRange(fcr, op, hostSheetName, ref changed),
            FullRowRangeRefNode frr => RewriteFullRowRange(frr, op, hostSheetName, ref changed),
            BinaryOpNode b  => b with
            {
                Left  = RewriteNode(b.Left,  op, hostSheetName, ref changed),
                Right = RewriteNode(b.Right, op, hostSheetName, ref changed)
            },
            UnaryOpNode u => u with
            {
                Operand = RewriteNode(u.Operand, op, hostSheetName, ref changed)
            },
            FunctionCallNode f => RewriteFunctionArgs(f, op, hostSheetName, ref changed),
            _ => node   // NumberNode, StringNode, BooleanNode, NamedRangeNode, ErrorNode
        };
    }

    private static FunctionCallNode RewriteFunctionArgs(
        FunctionCallNode f, RewriteOperation op, string hostSheetName, ref bool changed)
    {
        var newArgs = new List<FormulaNode>(f.Arguments.Count);
        foreach (var arg in f.Arguments)
            newArgs.Add(RewriteNode(arg, op, hostSheetName, ref changed));
        return f with { Arguments = newArgs };
    }

    private static FormulaNode RewriteCellRef(
        CellRefNode cr, RewriteOperation op, string hostSheetName, ref bool changed)
    {
        if (!Matches(cr, op, hostSheetName))
            return cr;

        return op switch
        {
            InsertRowsOp ins => RewriteCellRefInsertRows(cr, ins, ref changed),
            DeleteRowsOp del => RewriteCellRefDeleteRows(cr, del, ref changed),
            InsertColsOp ins => RewriteCellRefInsertCols(cr, ins, ref changed),
            DeleteColsOp del => RewriteCellRefDeleteCols(cr, del, ref changed),
            PasteOffsetOp paste => RewriteCellRefPaste(cr, paste, ref changed),
            MoveRangeOp move => RewriteCellRefMove(cr, move, ref changed),
            RenameSheetOp rename => RewriteCellRefRenameSheet(cr, rename, ref changed),
            DeleteSheetOp => RewriteSheetQualifiedRefDeleteSheet(ref changed),
            InsertCellsShiftDownOp ins => RewriteCellRefInsertCellsShiftDown(cr, ins, ref changed),
            InsertCellsShiftRightOp ins => RewriteCellRefInsertCellsShiftRight(cr, ins, ref changed),
            DeleteCellsShiftUpOp del => RewriteCellRefDeleteCellsShiftUp(cr, del, ref changed),
            DeleteCellsShiftLeftOp del => RewriteCellRefDeleteCellsShiftLeft(cr, del, ref changed),
            _ => cr
        };
    }

    private static FormulaNode RewriteRange(
        RangeRefNode rr, RewriteOperation op, string hostSheetName, ref bool changed)
    {
        // For sheet-qualified ranges, the sheet is on rr.SheetName and Start has SheetName set.
        // End may have SheetName = null; use the range's SheetName as its effective sheet.
        var endRef = rr.End.SheetName is null && rr.SheetName is not null
            ? rr.End with { SheetName = rr.SheetName }
            : rr.End;

        if (op is MoveRangeOp move)
            return RewriteRangeMove(rr, endRef, move, hostSheetName, ref changed);

        // Row/column deletes that cover only part of a range must SHRINK the range to the surviving
        // rows/columns, not collapse the whole reference to #REF!. Excel emits #REF! only when the
        // entire range is deleted. Rewriting the endpoints independently (below) cannot express this,
        // so delete ops get dedicated handling.
        if (op is DeleteRowsOp delRows)
            return RewriteRangeDeleteRows(rr, endRef, delRows, hostSheetName, ref changed);
        if (op is DeleteColsOp delCols)
            return RewriteRangeDeleteCols(rr, endRef, delCols, hostSheetName, ref changed);
        if (op is DeleteCellsShiftUpOp delCellsUp)
            return RewriteRangeDeleteCellsShiftUp(rr, endRef, delCellsUp, hostSheetName, ref changed);
        if (op is DeleteCellsShiftLeftOp delCellsLeft)
            return RewriteRangeDeleteCellsShiftLeft(rr, endRef, delCellsLeft, hostSheetName, ref changed);

        var start = RewriteCellRef(rr.Start, op, hostSheetName, ref changed);
        var end   = RewriteCellRef(endRef,   op, hostSheetName, ref changed);

        if (start is ErrorNode || end is ErrorNode)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        var sheetName = rr.SheetName;
        if (op is RenameSheetOp rename &&
            sheetName is not null &&
            string.Equals(sheetName, rename.OldSheetName, StringComparison.OrdinalIgnoreCase))
        {
            sheetName = rename.NewSheetName;
        }

        return rr with { Start = (CellRefNode)start, End = (CellRefNode)end, SheetName = sheetName };
    }

    private static FormulaNode RewriteRangeDeleteRows(
        RangeRefNode rr, CellRefNode endRef, DeleteRowsOp op, string hostSheetName, ref bool changed)
    {
        if (!Matches(rr.SheetName, op, hostSheetName))
            return rr;

        uint s = rr.Start.Row, e = endRef.Row;
        uint bandStart = op.StartRow, bandEnd = op.StartRow + op.Count - 1;

        // Whole range inside the deleted band → the reference is gone.
        if (bandStart <= s && e <= bandEnd)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        var newStart = ShiftOrClampForDelete(s, bandStart, bandEnd, op.Count, isRangeStart: true);
        var newEnd = ShiftOrClampForDelete(e, bandStart, bandEnd, op.Count, isRangeStart: false);
        if (newStart == s && newEnd == e)
            return rr; // band entirely below the range: no change

        changed = true;
        return rr with
        {
            Start = rr.Start with { Row = newStart },
            End = endRef with { Row = newEnd },
        };
    }

    private static FormulaNode RewriteRangeDeleteCols(
        RangeRefNode rr, CellRefNode endRef, DeleteColsOp op, string hostSheetName, ref bool changed)
    {
        if (!Matches(rr.SheetName, op, hostSheetName))
            return rr;

        uint s = rr.Start.ColumnNumber, e = endRef.ColumnNumber;
        uint bandStart = op.StartCol, bandEnd = op.StartCol + op.Count - 1;

        if (bandStart <= s && e <= bandEnd)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        var newStart = ShiftOrClampForDelete(s, bandStart, bandEnd, op.Count, isRangeStart: true);
        var newEnd = ShiftOrClampForDelete(e, bandStart, bandEnd, op.Count, isRangeStart: false);
        if (newStart == s && newEnd == e)
            return rr;

        changed = true;
        return rr with
        {
            Start = rr.Start with { ColumnName = CellAddress.NumberToColumnName(newStart) },
            End = endRef with { ColumnName = CellAddress.NumberToColumnName(newEnd) },
        };
    }

    /// <summary>
    /// Delete Cells / Shift Up: a range ref that lies within the column band
    /// [<see cref="DeleteCellsShiftUpOp.RangeStartCol"/>..<see cref="DeleteCellsShiftUpOp.RangeEndCol"/>]
    /// and straddles the deleted row band must SHRINK to the surviving rows, not collapse to #REF!
    /// just because one endpoint fell inside the deleted band (mirrors <see cref="RewriteRangeDeleteRows"/>).
    /// Ranges outside the column band, or spanning columns only partially inside it, fall back to the
    /// generic per-endpoint rewrite (same as any other cell-ref op).
    /// </summary>
    private static FormulaNode RewriteRangeDeleteCellsShiftUp(
        RangeRefNode rr, CellRefNode endRef, DeleteCellsShiftUpOp op, string hostSheetName, ref bool changed)
    {
        if (!Matches(rr.SheetName, op, hostSheetName))
            return rr;

        uint colStart = rr.Start.ColumnNumber, colEnd = endRef.ColumnNumber;
        if (colStart < op.RangeStartCol || colStart > op.RangeEndCol ||
            colEnd < op.RangeStartCol || colEnd > op.RangeEndCol)
        {
            // Range's columns aren't fully inside the deleted band's column scope: fall back to
            // rewriting each endpoint independently, same as any other op.
            return RewriteRangeGenericEndpoints(rr, endRef, op, hostSheetName, ref changed);
        }

        uint s = rr.Start.Row, e = endRef.Row;
        uint bandStart = op.DeletedStartRow, bandEnd = op.DeletedEndRow;

        // Whole range inside the deleted band → the reference is gone.
        if (bandStart <= s && e <= bandEnd)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        var newStart = ShiftOrClampForDelete(s, bandStart, bandEnd, op.Count, isRangeStart: true);
        var newEnd = ShiftOrClampForDelete(e, bandStart, bandEnd, op.Count, isRangeStart: false);
        if (newStart == s && newEnd == e)
            return rr; // band entirely below the range: no change

        changed = true;
        return rr with
        {
            Start = rr.Start with { Row = newStart },
            End = endRef with { Row = newEnd },
        };
    }

    /// <summary>
    /// Delete Cells / Shift Left: a range ref that lies within the row band
    /// [<see cref="DeleteCellsShiftLeftOp.BandStartRow"/>..<see cref="DeleteCellsShiftLeftOp.BandEndRow"/>]
    /// and straddles the deleted column band must SHRINK to the surviving columns, not collapse to
    /// #REF! just because one endpoint fell inside the deleted band (mirrors <see cref="RewriteRangeDeleteCols"/>).
    /// Ranges outside the row band, or spanning rows only partially inside it, fall back to the
    /// generic per-endpoint rewrite (same as any other cell-ref op).
    /// </summary>
    private static FormulaNode RewriteRangeDeleteCellsShiftLeft(
        RangeRefNode rr, CellRefNode endRef, DeleteCellsShiftLeftOp op, string hostSheetName, ref bool changed)
    {
        if (!Matches(rr.SheetName, op, hostSheetName))
            return rr;

        uint rowStart = rr.Start.Row, rowEnd = endRef.Row;
        if (rowStart < op.BandStartRow || rowStart > op.BandEndRow ||
            rowEnd < op.BandStartRow || rowEnd > op.BandEndRow)
        {
            // Range's rows aren't fully inside the deleted band's row scope: fall back to
            // rewriting each endpoint independently, same as any other op.
            return RewriteRangeGenericEndpoints(rr, endRef, op, hostSheetName, ref changed);
        }

        uint s = rr.Start.ColumnNumber, e = endRef.ColumnNumber;
        uint bandStart = op.DeletedStartCol, bandEnd = op.DeletedEndCol;

        if (bandStart <= s && e <= bandEnd)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        var newStart = ShiftOrClampForDelete(s, bandStart, bandEnd, op.Count, isRangeStart: true);
        var newEnd = ShiftOrClampForDelete(e, bandStart, bandEnd, op.Count, isRangeStart: false);
        if (newStart == s && newEnd == e)
            return rr;

        changed = true;
        return rr with
        {
            Start = rr.Start with { ColumnName = CellAddress.NumberToColumnName(newStart) },
            End = endRef with { ColumnName = CellAddress.NumberToColumnName(newEnd) },
        };
    }

    /// <summary>
    /// Generic fallback used when a delete-cells range op doesn't apply its shrink logic (the range's
    /// perpendicular axis isn't fully inside the op's band scope): rewrite Start/End independently,
    /// same as the default path in <see cref="RewriteRange"/>.
    /// </summary>
    private static FormulaNode RewriteRangeGenericEndpoints(
        RangeRefNode rr, CellRefNode endRef, RewriteOperation op, string hostSheetName, ref bool changed)
    {
        var start = RewriteCellRef(rr.Start, op, hostSheetName, ref changed);
        var end = RewriteCellRef(endRef, op, hostSheetName, ref changed);

        if (start is ErrorNode || end is ErrorNode)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        return rr with { Start = (CellRefNode)start, End = (CellRefNode)end };
    }

    /// <summary>
    /// Map a single range endpoint (row or column number) through a delete: unchanged when before the
    /// deleted band, shifted up/left by <paramref name="count"/> when after it, and clamped to the
    /// surviving edge when inside it (start → first row/col after the band, end → last before it).
    /// </summary>
    private static uint ShiftOrClampForDelete(uint value, uint bandStart, uint bandEnd, uint count, bool isRangeStart)
    {
        if (value < bandStart)
            return value;
        if (value > bandEnd)
            return value - count;
        return isRangeStart ? bandStart : bandStart - 1;
    }

    private static FormulaNode RewriteFullColumnRange(
        FullColumnRangeRefNode range, RewriteOperation op, string hostSheetName, ref bool changed)
    {
        if (!Matches(range.SheetName, op, hostSheetName))
            return range;

        return op switch
        {
            InsertColsOp ins => RewriteFullColumnRangeInsertCols(range, ins, ref changed),
            DeleteColsOp del => RewriteFullColumnRangeDeleteCols(range, del, ref changed),
            PasteOffsetOp paste => RewriteFullColumnRangePaste(range, paste, ref changed),
            RenameSheetOp rename => RewriteFullColumnRangeRenameSheet(range, rename, ref changed),
            DeleteSheetOp => RewriteSheetQualifiedRefDeleteSheet(ref changed),
            _ => range
        };
    }

    private static FormulaNode RewriteFullRowRange(
        FullRowRangeRefNode range, RewriteOperation op, string hostSheetName, ref bool changed)
    {
        if (!Matches(range.SheetName, op, hostSheetName))
            return range;

        return op switch
        {
            InsertRowsOp ins => RewriteFullRowRangeInsertRows(range, ins, ref changed),
            DeleteRowsOp del => RewriteFullRowRangeDeleteRows(range, del, ref changed),
            PasteOffsetOp paste => RewriteFullRowRangePaste(range, paste, ref changed),
            RenameSheetOp rename => RewriteFullRowRangeRenameSheet(range, rename, ref changed),
            DeleteSheetOp => RewriteSheetQualifiedRefDeleteSheet(ref changed),
            _ => range
        };
    }

    // ── Row insert ────────────────────────────────────────────────────────────

    private static FormulaNode RewriteCellRefInsertRows(
        CellRefNode cr, InsertRowsOp op, ref bool changed)
    {
        // $ does NOT protect against structural row shifts — Excel adjusts $A$5 → $A$7
        // after inserting 2 rows above row 5. Only paste offsets respect IsRowAbsolute.
        if (cr.Row < op.BeforeRow)
            return cr;

        long newRow = (long)cr.Row + op.Count;
        if (newRow > CellAddress.MaxRow)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        changed = true;
        return cr with { Row = (uint)newRow };
    }

    // ── Row delete ────────────────────────────────────────────────────────────

    private static FormulaNode RewriteCellRefDeleteRows(
        CellRefNode cr, DeleteRowsOp op, ref bool changed)
    {
        uint endRow = op.StartRow + op.Count - 1;

        if (cr.Row >= op.StartRow && cr.Row <= endRow)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        if (cr.Row > endRow)
        {
            changed = true;
            return cr with { Row = cr.Row - op.Count };
        }

        return cr;
    }

    // ── Column insert ─────────────────────────────────────────────────────────

    private static FormulaNode RewriteCellRefInsertCols(
        CellRefNode cr, InsertColsOp op, ref bool changed)
    {
        // $ does NOT protect against structural column shifts (same rule as rows above).
        if (cr.ColumnNumber < op.BeforeCol)
            return cr;

        long newColNum = (long)cr.ColumnNumber + op.Count;
        if (newColNum > CellAddress.MaxCol)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        changed = true;
        var newCol = CellAddress.NumberToColumnName((uint)newColNum);
        return cr with { ColumnName = newCol };
    }

    // ── Column delete ─────────────────────────────────────────────────────────

    private static FormulaNode RewriteCellRefDeleteCols(
        CellRefNode cr, DeleteColsOp op, ref bool changed)
    {
        uint endCol = op.StartCol + op.Count - 1;

        if (cr.ColumnNumber >= op.StartCol && cr.ColumnNumber <= endCol)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        if (cr.ColumnNumber > endCol)
        {
            changed = true;
            var newCol = CellAddress.NumberToColumnName(cr.ColumnNumber - op.Count);
            return cr with { ColumnName = newCol };
        }

        return cr;
    }

    // ── Insert Cells Shift Down ───────────────────────────────────────────────

    private static FormulaNode RewriteCellRefInsertCellsShiftDown(
        CellRefNode cr, InsertCellsShiftDownOp op, ref bool changed)
    {
        // Only cells in the band column range that are at or below the insert row are shifted.
        if (cr.ColumnNumber < op.RangeStartCol || cr.ColumnNumber > op.RangeEndCol)
            return cr;  // outside column band: untouched
        if (cr.Row < op.InsertBeforeRow)
            return cr;  // above insert point: untouched

        long newRow = (long)cr.Row + op.Count;
        if (newRow > CellAddress.MaxRow)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        changed = true;
        return cr with { Row = (uint)newRow };
    }

    // ── Insert Cells Shift Right ──────────────────────────────────────────────

    private static FormulaNode RewriteCellRefInsertCellsShiftRight(
        CellRefNode cr, InsertCellsShiftRightOp op, ref bool changed)
    {
        // Only cells in the band row range that are at or to the right of the insert column are shifted.
        if (cr.Row < op.BandStartRow || cr.Row > op.BandEndRow)
            return cr;  // outside row band: untouched
        if (cr.ColumnNumber < op.InsertBeforeCol)
            return cr;  // left of insert point: untouched

        long newColNum = (long)cr.ColumnNumber + op.Count;
        if (newColNum > CellAddress.MaxCol)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        changed = true;
        return cr with { ColumnName = CellAddress.NumberToColumnName((uint)newColNum) };
    }

    // ── Delete Cells Shift Up ─────────────────────────────────────────────────

    private static FormulaNode RewriteCellRefDeleteCellsShiftUp(
        CellRefNode cr, DeleteCellsShiftUpOp op, ref bool changed)
    {
        // Only cells in the band column range are affected.
        if (cr.ColumnNumber < op.RangeStartCol || cr.ColumnNumber > op.RangeEndCol)
            return cr;  // outside column band: untouched

        if (cr.Row >= op.DeletedStartRow && cr.Row <= op.DeletedEndRow)
        {
            // Cell was deleted → #REF!
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        if (cr.Row > op.DeletedEndRow)
        {
            // Below the deleted band: shift up
            changed = true;
            return cr with { Row = cr.Row - op.Count };
        }

        return cr;  // above deleted band: untouched
    }

    // ── Delete Cells Shift Left ───────────────────────────────────────────────

    private static FormulaNode RewriteCellRefDeleteCellsShiftLeft(
        CellRefNode cr, DeleteCellsShiftLeftOp op, ref bool changed)
    {
        // Only cells in the band row range are affected.
        if (cr.Row < op.BandStartRow || cr.Row > op.BandEndRow)
            return cr;  // outside row band: untouched

        if (cr.ColumnNumber >= op.DeletedStartCol && cr.ColumnNumber <= op.DeletedEndCol)
        {
            // Cell was deleted → #REF!
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        if (cr.ColumnNumber > op.DeletedEndCol)
        {
            // Right of the deleted band: shift left
            changed = true;
            return cr with { ColumnName = CellAddress.NumberToColumnName(cr.ColumnNumber - op.Count) };
        }

        return cr;  // left of deleted band: untouched
    }

    // ── Paste offset ──────────────────────────────────────────────────────────

    private static FormulaNode RewriteCellRefPaste(
        CellRefNode cr, PasteOffsetOp op, ref bool changed)
    {
        var newRow = cr.Row;
        var newColNum = cr.ColumnNumber;
        bool rowChanged = false, colChanged = false;

        if (!cr.IsRowAbsolute && op.RowDelta != 0)
        {
            long r = (long)cr.Row + op.RowDelta;
            if (r < 1 || r > CellAddress.MaxRow)
            {
                changed = true;
                return new ErrorNode(ErrorValue.Ref);
            }
            newRow = (uint)r;
            rowChanged = true;
        }

        if (!cr.IsColAbsolute && op.ColDelta != 0)
        {
            long c = (long)cr.ColumnNumber + op.ColDelta;
            if (c < 1 || c > CellAddress.MaxCol)
            {
                changed = true;
                return new ErrorNode(ErrorValue.Ref);
            }
            newColNum = (uint)c;
            colChanged = true;
        }

        if (!rowChanged && !colChanged)
            return cr;

        changed = true;
        var newColName = colChanged
            ? CellAddress.NumberToColumnName(newColNum)
            : cr.ColumnName;
        return cr with { Row = newRow, ColumnName = newColName };
    }

    private static FormulaNode RewriteCellRefMove(
        CellRefNode cr, MoveRangeOp op, ref bool changed)
    {
        if (!IsInMoveSource(cr, op))
            return cr;

        long row = (long)cr.Row + op.RowDelta;
        long col = (long)cr.ColumnNumber + op.ColDelta;
        if (row < 1 || row > CellAddress.MaxRow || col < 1 || col > CellAddress.MaxCol)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        changed = true;
        return cr with
        {
            Row = (uint)row,
            ColumnName = CellAddress.NumberToColumnName((uint)col)
        };
    }

    private static FormulaNode RewriteRangeMove(
        RangeRefNode rr,
        CellRefNode endRef,
        MoveRangeOp op,
        string hostSheetName,
        ref bool changed)
    {
        if (!Matches(rr.SheetName, op, hostSheetName))
            return rr;

        var startInSource = IsInMoveSource(rr.Start, op);
        var endInSource = IsInMoveSource(endRef, op);
        if (!startInSource || !endInSource)
        {
            if (!IsSingleCellMove(op) || startInSource == endInSource)
                return rr;

            var moving = startInSource ? rr.Start : endRef;
            var other = startInSource ? endRef : rr.Start;
            if (!CanExpandSingleCellMoveRange(moving, other, op))
                return rr;

            var rewritten = RewriteCellRefMove(moving, op, ref changed);
            if (rewritten is ErrorNode)
            {
                changed = true;
                return new ErrorNode(ErrorValue.Ref);
            }

            return startInSource
                ? rr with { Start = (CellRefNode)rewritten }
                : rr with { End = (CellRefNode)rewritten };
        }

        var start = RewriteCellRefMove(rr.Start, op, ref changed);
        var end = RewriteCellRefMove(endRef, op, ref changed);
        if (start is ErrorNode || end is ErrorNode)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        return rr with { Start = (CellRefNode)start, End = (CellRefNode)end };
    }

    private static FormulaNode RewriteCellRefRenameSheet(
        CellRefNode cr, RenameSheetOp op, ref bool changed)
    {
        if (cr.SheetName is null ||
            !string.Equals(cr.SheetName, op.OldSheetName, StringComparison.OrdinalIgnoreCase))
            return cr;

        changed = true;
        return cr with { SheetName = op.NewSheetName };
    }

    private static FormulaNode RewriteSheetQualifiedRefDeleteSheet(ref bool changed)
    {
        changed = true;
        return new ErrorNode(ErrorValue.Ref);
    }

    private static FormulaNode RewriteFullColumnRangeInsertCols(
        FullColumnRangeRefNode range, InsertColsOp op, ref bool changed)
    {
        var start = RewriteColumnInsert(range.StartColumnNumber, op, ref changed);
        var end = RewriteColumnInsert(range.EndColumnNumber, op, ref changed);
        if (start is null || end is null)
            return new ErrorNode(ErrorValue.Ref);

        return range with
        {
            StartColumnName = CellAddress.NumberToColumnName(start.Value),
            EndColumnName = CellAddress.NumberToColumnName(end.Value)
        };
    }

    private static FormulaNode RewriteFullColumnRangeDeleteCols(
        FullColumnRangeRefNode range, DeleteColsOp op, ref bool changed)
    {
        var start = RewriteColumnDelete(range.StartColumnNumber, op, ref changed);
        var end = RewriteColumnDelete(range.EndColumnNumber, op, ref changed);
        if (start is null || end is null)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        return range with
        {
            StartColumnName = CellAddress.NumberToColumnName(start.Value),
            EndColumnName = CellAddress.NumberToColumnName(end.Value)
        };
    }

    private static FormulaNode RewriteFullColumnRangePaste(
        FullColumnRangeRefNode range, PasteOffsetOp op, ref bool changed)
    {
        if (op.ColDelta == 0 || (range.IsStartAbsolute && range.IsEndAbsolute))
            return range;

        var start = RewriteColumnPaste(range.StartColumnNumber, range.IsStartAbsolute, op, ref changed);
        var end = RewriteColumnPaste(range.EndColumnNumber, range.IsEndAbsolute, op, ref changed);
        if (start is null || end is null)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        return range with
        {
            StartColumnName = CellAddress.NumberToColumnName(start.Value),
            EndColumnName = CellAddress.NumberToColumnName(end.Value)
        };
    }

    private static FormulaNode RewriteFullColumnRangeRenameSheet(
        FullColumnRangeRefNode range, RenameSheetOp op, ref bool changed)
    {
        if (range.SheetName is null ||
            !string.Equals(range.SheetName, op.OldSheetName, StringComparison.OrdinalIgnoreCase))
            return range;

        changed = true;
        return range with { SheetName = op.NewSheetName };
    }

    private static FormulaNode RewriteFullRowRangeInsertRows(
        FullRowRangeRefNode range, InsertRowsOp op, ref bool changed)
    {
        var start = RewriteRowInsert(range.StartRow, op, ref changed);
        var end = RewriteRowInsert(range.EndRow, op, ref changed);
        if (start is null || end is null)
            return new ErrorNode(ErrorValue.Ref);

        return range with { StartRow = start.Value, EndRow = end.Value };
    }

    private static FormulaNode RewriteFullRowRangeDeleteRows(
        FullRowRangeRefNode range, DeleteRowsOp op, ref bool changed)
    {
        var start = RewriteRowDelete(range.StartRow, op, ref changed);
        var end = RewriteRowDelete(range.EndRow, op, ref changed);
        if (start is null || end is null)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        return range with { StartRow = start.Value, EndRow = end.Value };
    }

    private static FormulaNode RewriteFullRowRangePaste(
        FullRowRangeRefNode range, PasteOffsetOp op, ref bool changed)
    {
        if (op.RowDelta == 0 || (range.IsStartAbsolute && range.IsEndAbsolute))
            return range;

        var start = RewriteRowPaste(range.StartRow, range.IsStartAbsolute, op, ref changed);
        var end = RewriteRowPaste(range.EndRow, range.IsEndAbsolute, op, ref changed);
        if (start is null || end is null)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        return range with { StartRow = start.Value, EndRow = end.Value };
    }

    private static FormulaNode RewriteFullRowRangeRenameSheet(
        FullRowRangeRefNode range, RenameSheetOp op, ref bool changed)
    {
        if (range.SheetName is null ||
            !string.Equals(range.SheetName, op.OldSheetName, StringComparison.OrdinalIgnoreCase))
            return range;

        changed = true;
        return range with { SheetName = op.NewSheetName };
    }

    private static uint? RewriteColumnInsert(uint column, InsertColsOp op, ref bool changed)
    {
        if (column < op.BeforeCol)
            return column;

        long newColumn = (long)column + op.Count;
        if (newColumn > CellAddress.MaxCol)
        {
            changed = true;
            return null;
        }

        changed = true;
        return (uint)newColumn;
    }

    private static uint? RewriteColumnDelete(uint column, DeleteColsOp op, ref bool changed)
    {
        uint endCol = op.StartCol + op.Count - 1;
        if (column >= op.StartCol && column <= endCol)
            return null;

        if (column > endCol)
        {
            changed = true;
            return column - op.Count;
        }

        return column;
    }

    private static uint? RewriteColumnPaste(uint column, bool isAbsolute, PasteOffsetOp op, ref bool changed)
    {
        if (isAbsolute || op.ColDelta == 0)
            return column;

        long newColumn = (long)column + op.ColDelta;
        if (newColumn < 1 || newColumn > CellAddress.MaxCol)
            return null;

        changed = true;
        return (uint)newColumn;
    }

    private static uint? RewriteRowInsert(uint row, InsertRowsOp op, ref bool changed)
    {
        if (row < op.BeforeRow)
            return row;

        long newRow = (long)row + op.Count;
        if (newRow > CellAddress.MaxRow)
        {
            changed = true;
            return null;
        }

        changed = true;
        return (uint)newRow;
    }

    private static uint? RewriteRowDelete(uint row, DeleteRowsOp op, ref bool changed)
    {
        uint endRow = op.StartRow + op.Count - 1;
        if (row >= op.StartRow && row <= endRow)
            return null;

        if (row > endRow)
        {
            changed = true;
            return row - op.Count;
        }

        return row;
    }

    private static uint? RewriteRowPaste(uint row, bool isAbsolute, PasteOffsetOp op, ref bool changed)
    {
        if (isAbsolute || op.RowDelta == 0)
            return row;

        long newRow = (long)row + op.RowDelta;
        if (newRow < 1 || newRow > CellAddress.MaxRow)
            return null;

        changed = true;
        return (uint)newRow;
    }

    // ── Sheet matching ────────────────────────────────────────────────────────

    private static bool Matches(CellRefNode cr, RewriteOperation op, string hostSheetName)
    {
        return Matches(cr.SheetName, op, hostSheetName);
    }

    private static bool Matches(string? refSheetName, RewriteOperation op, string hostSheetName)
    {
        if (op is PasteOffsetOp) return true;   // paste always adjusts
        if (op is RenameSheetOp rename)
            return refSheetName is not null &&
                   string.Equals(refSheetName, rename.OldSheetName, StringComparison.OrdinalIgnoreCase);
        if (op is DeleteSheetOp deleteSheet)
            return refSheetName is not null &&
                   string.Equals(refSheetName, deleteSheet.SheetName, StringComparison.OrdinalIgnoreCase);

        var opSheet = op switch
        {
            InsertRowsOp ins => ins.SheetName,
            DeleteRowsOp del => del.SheetName,
            InsertColsOp ins => ins.SheetName,
            DeleteColsOp del => del.SheetName,
            MoveRangeOp move => move.SheetName,
            InsertCellsShiftDownOp ins => ins.SheetName,
            InsertCellsShiftRightOp ins => ins.SheetName,
            DeleteCellsShiftUpOp del => del.SheetName,
            DeleteCellsShiftLeftOp del => del.SheetName,
            _ => null
        };

        if (opSheet is null) return false;

        var refSheet = refSheetName ?? hostSheetName;
        return string.Equals(refSheet, opSheet, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInMoveSource(CellRefNode cr, MoveRangeOp op) =>
        cr.Row >= op.SourceStartRow &&
        cr.Row <= op.SourceEndRow &&
        cr.ColumnNumber >= op.SourceStartCol &&
        cr.ColumnNumber <= op.SourceEndCol;

    private static bool IsSingleCellMove(MoveRangeOp op) =>
        op.SourceStartRow == op.SourceEndRow &&
        op.SourceStartCol == op.SourceEndCol;

    private static bool CanExpandSingleCellMoveRange(
        CellRefNode movingEndpoint,
        CellRefNode otherEndpoint,
        MoveRangeOp op)
    {
        if (movingEndpoint.Row == otherEndpoint.Row &&
            op.RowDelta == 0 &&
            IsFartherOutward(movingEndpoint.ColumnNumber, otherEndpoint.ColumnNumber, op.ColDelta))
        {
            return true;
        }

        return movingEndpoint.ColumnNumber == otherEndpoint.ColumnNumber &&
               op.ColDelta == 0 &&
               IsFartherOutward(movingEndpoint.Row, otherEndpoint.Row, op.RowDelta);
    }

    private static bool IsFartherOutward(uint movingCoordinate, uint otherCoordinate, int delta)
    {
        if (delta == 0 || movingCoordinate == otherCoordinate)
            return false;

        var originalDistance = Math.Abs((long)movingCoordinate - otherCoordinate);
        var movedCoordinate = (long)movingCoordinate + delta;
        var movedDistance = Math.Abs(movedCoordinate - otherCoordinate);
        return Math.Sign((long)movingCoordinate - otherCoordinate) == Math.Sign(movedCoordinate - otherCoordinate) &&
               movedDistance > originalDistance;
    }
}
