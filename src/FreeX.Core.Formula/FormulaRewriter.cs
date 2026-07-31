using FreeX.Core.Model;

namespace FreeX.Core.Formula;

// ── Operation types ───────────────────────────────────────────────────────────

public abstract record RewriteOperation;
public sealed record InsertRowsOp(string SheetName, uint BeforeRow, uint Count) : RewriteOperation;
// DeletedTableNames: structured tables hosted on SheetName whose entire Range fell inside the
// deleted band [StartRow, StartRow+Count-1] -- a row delete that fully consumes a table's range is
// exactly as destructive to the table's identity as deleting its host sheet (see
// RowColumnShiftHelpers.ShiftStructuredTables), so any remaining Table[...] structured reference to
// it must collapse to #REF!, mirroring DeleteSheetOp.DeletedTableNames below. Optional/defaulted so
// existing callers that only rewrite cell/range refs keep compiling unchanged.
public sealed record DeleteRowsOp(string SheetName, uint StartRow,  uint Count, IReadOnlyList<string>? DeletedTableNames = null) : RewriteOperation;
public sealed record InsertColsOp(string SheetName, uint BeforeCol, uint Count) : RewriteOperation;
// DeletedTableNames: same as DeleteRowsOp.DeletedTableNames above, for a column delete that fully
// consumes a table's range.
public sealed record DeleteColsOp(string SheetName, uint StartCol,  uint Count, IReadOnlyList<string>? DeletedTableNames = null) : RewriteOperation;
public sealed record PasteOffsetOp(int RowDelta, int ColDelta)                  : RewriteOperation;
// Transpose paste: a relative reference's (row,col) offset from the COPIED block's own anchor
// (SourceAnchorRow/Col) is axis-swapped and re-anchored at the DESTINATION block's anchor
// (DestAnchorRow/Col) -- e.g. a reference 2 columns left of the source anchor becomes 2 rows
// above the destination anchor. This is distinct from PasteOffsetOp, which applies one uniform
// (RowDelta,ColDelta) translation to every reference -- correct for a plain paste (every cell in
// the block moves by the same amount) but wrong once Transpose swaps the block's shape, where
// each reference's own position within the block determines a different translation.
public sealed record PasteTransposeOp(
    uint SourceAnchorRow,
    uint SourceAnchorCol,
    uint DestAnchorRow,
    uint DestAnchorCol)                                                        : RewriteOperation;
public sealed record MoveRangeOp(
    string SheetName,
    uint SourceStartRow,
    uint SourceStartCol,
    uint SourceEndRow,
    uint SourceEndCol,
    int RowDelta,
    int ColDelta)                                                               : RewriteOperation;
public sealed record RenameSheetOp(string OldSheetName, string NewSheetName)    : RewriteOperation;
// DeletedTableNames: structured tables that were hosted on the deleted sheet (and so no longer
// exist anywhere in the workbook). Optional/defaulted so existing callers that only rewrite
// cell/range refs keep compiling unchanged; callers that also own the deleted sheet's
// StructuredTables can populate it to get the matching Table[...] #REF! behavior below.
public sealed record DeleteSheetOp(string SheetName, IReadOnlyList<string>? DeletedTableNames = null) : RewriteOperation;
public sealed record RenameTableOp(string OldTableName, string NewTableName)    : RewriteOperation;

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
            StructuredReferenceNode sr => RewriteStructuredReference(sr, op, ref changed),
            StructuredCurrentRowReferenceNode scr => RewriteStructuredCurrentRowReference(scr, op, ref changed),
            IntersectionNode ix => ix with
            {
                Left  = RewriteNode(ix.Left,  op, hostSheetName, ref changed),
                Right = RewriteNode(ix.Right, op, hostSheetName, ref changed)
            },
            NamedRangeEndpointNode nre => nre with
            {
                // A NamedRangeNode endpoint stays a name (unrewritable), same as a bare
                // NamedRangeNode falls through the catch-all below unchanged; only a
                // CellRefNode endpoint carries row/col coordinates to shift.
                Start = RewriteNode(nre.Start, op, hostSheetName, ref changed),
                End   = RewriteNode(nre.End,   op, hostSheetName, ref changed)
            },
            UnionNode union => RewriteUnion(union, op, hostSheetName, ref changed),
            _ => node   // NumberNode, StringNode, BooleanNode, NamedRangeNode, ErrorNode
        };
    }

    // A ref parameter can't be captured by a lambda (e.g. a List.Select projection), so each area
    // is rewritten in an explicit loop rather than LINQ.
    private static FormulaNode RewriteUnion(
        UnionNode union, RewriteOperation op, string hostSheetName, ref bool changed)
    {
        var areas = new List<FormulaNode>(union.Areas.Count);
        foreach (var area in union.Areas)
            areas.Add(RewriteNode(area, op, hostSheetName, ref changed));
        return union with { Areas = areas };
    }

    // ── Table rename ──────────────────────────────────────────────────────────
    // Structured references carry the table name as a bare literal (no table-ID indirection),
    // so renaming a table must rewrite every TableName[...] / TableName[@Column] occurrence
    // across every formula, mirroring how RenameSheetOp rewrites sheet-qualified refs above.
    // An unqualified reference (TableName empty, e.g. bare [@Column] inside the table's own
    // formulas) resolves against whichever table the host cell belongs to, so it never carries
    // the old name and needs no rewrite.

    private static FormulaNode RewriteStructuredReference(
        StructuredReferenceNode sr, RewriteOperation op, ref bool changed)
    {
        // The table's own host sheet was deleted, or a row/column delete fully consumed the
        // table's range -- either way the table no longer exists anywhere in the workbook, so
        // every remaining Table[...] reference to it is exactly as dead as a deleted defined
        // name, and Excel shows #REF! (never #NAME?) for that case.
        if (!string.IsNullOrEmpty(sr.TableName) && MatchesDeletedTable(sr.TableName, op))
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        if (op is DeleteSheetOp or DeleteRowsOp or DeleteColsOp)
            return sr;

        if (op is not RenameTableOp rename ||
            string.IsNullOrEmpty(sr.TableName) ||
            !string.Equals(sr.TableName, rename.OldTableName, StringComparison.OrdinalIgnoreCase))
        {
            return sr;
        }

        changed = true;
        return sr with { TableName = rename.NewTableName };
    }

    private static FormulaNode RewriteStructuredCurrentRowReference(
        StructuredCurrentRowReferenceNode scr, RewriteOperation op, ref bool changed)
    {
        if (!string.IsNullOrEmpty(scr.TableName) && MatchesDeletedTable(scr.TableName, op))
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        if (op is DeleteSheetOp or DeleteRowsOp or DeleteColsOp)
            return scr;

        if (op is not RenameTableOp rename ||
            string.IsNullOrEmpty(scr.TableName) ||
            !string.Equals(scr.TableName, rename.OldTableName, StringComparison.OrdinalIgnoreCase))
        {
            return scr;
        }

        changed = true;
        return scr with { TableName = rename.NewTableName };
    }

    // Shared by both the DeleteSheetOp case (table's whole host sheet removed) and the
    // DeleteRowsOp/DeleteColsOp case (a row/column delete fully consumed the table's range,
    // freeing its name workbook-wide the same way -- see RowColumnShiftHelpers.ShiftStructuredTables).
    // Every other op carries no DeletedTableNames and so never matches.
    private static bool MatchesDeletedTable(string tableName, RewriteOperation op)
    {
        var deletedTableNames = op switch
        {
            DeleteSheetOp delSheet => delSheet.DeletedTableNames,
            DeleteRowsOp delRows   => delRows.DeletedTableNames,
            DeleteColsOp delCols   => delCols.DeletedTableNames,
            _ => null
        };

        if (deletedTableNames is null)
            return false;

        foreach (var deletedTable in deletedTableNames)
        {
            if (string.Equals(deletedTable, tableName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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
            PasteTransposeOp transpose => RewriteCellRefTranspose(cr, transpose, ref changed),
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
        // TODO(H28 3-D sheet-span refs): a span (EndSheetName set, e.g. Sheet1:Sheet3!A1) is passed
        // through untouched for row/col structural ops — insert/delete rows or columns, cell moves.
        // None of the row/col shift or delete-shrink math below understands "this reference spans
        // multiple sheets", so blindly reusing that logic here would silently mis-rewrite (or wrongly
        // #REF!) references to the *other* spanned sheets. Leaving the span untouched for those ops is
        // conservative: the formula text is unchanged, so it still means exactly what it said before
        // the structural edit (correct for edits on sheets outside the span; potentially stale — same
        // as Excel would need to fully re-resolve — for edits on a spanned sheet whose row/col shift
        // should have shown up in this reference). Full span-aware rewriting (per-sheet shift math) is
        // intentionally out of scope for this change. RenameSheetOp, however, is purely textual — the
        // span's endpoint sheet *names* live directly on rr.SheetName/rr.EndSheetName, so a rename can
        // (and must) be applied without touching the per-cell shift math at all. DeleteSheetOp on a
        // span endpoint is likewise handled below (freezing the whole span to #REF!, mirroring
        // RewriteSheetQualifiedRefDeleteSheet) — see R94-Core.Formula-3dspan-deletesheet: leaving the
        // stale sheet name in place let TryExpandSheetSpanAggregateRange silently re-resolve it against
        // any future sheet that happened to reuse the deleted name.
        if (rr.EndSheetName is not null)
        {
            if (op is RenameSheetOp renameSpan)
            {
                var newStartSheet = rr.SheetName;
                var newEndSheet = rr.EndSheetName;
                bool spanChanged = false;

                if (newStartSheet is not null &&
                    string.Equals(newStartSheet, renameSpan.OldSheetName, StringComparison.OrdinalIgnoreCase))
                {
                    newStartSheet = renameSpan.NewSheetName;
                    spanChanged = true;
                }

                if (string.Equals(newEndSheet, renameSpan.OldSheetName, StringComparison.OrdinalIgnoreCase))
                {
                    newEndSheet = renameSpan.NewSheetName;
                    spanChanged = true;
                }

                if (spanChanged)
                {
                    changed = true;
                    return rr with { SheetName = newStartSheet, EndSheetName = newEndSheet };
                }
            }

            // Deleting either endpoint sheet of the span permanently collapses the whole span to
            // #REF!, mirroring RewriteSheetQualifiedRefDeleteSheet's treatment of an ordinary
            // sheet-qualified reference. This is deliberate (not "TODO" like the row/col-shift
            // ops above): unlike a shift, a delete has no span-aware math to defer -- the sheet
            // named by rr.SheetName/rr.EndSheetName is simply gone. Freezing the text to #REF!
            // (instead of leaving the original sheet name in place) matches Excel and prevents the
            // span from silently re-resolving against an unrelated future sheet that happens to
            // reuse the same name (TryExpandSheetSpanAggregateRange re-resolves both endpoints by
            // NAME on every recalculation, so a stale name is a live landmine, not an inert one).
            if (op is DeleteSheetOp deleteSpanSheet &&
                ((rr.SheetName is not null &&
                  string.Equals(rr.SheetName, deleteSpanSheet.SheetName, StringComparison.OrdinalIgnoreCase)) ||
                 string.Equals(rr.EndSheetName, deleteSpanSheet.SheetName, StringComparison.OrdinalIgnoreCase)))
            {
                changed = true;
                return new ErrorNode(ErrorValue.Ref);
            }

            return rr;
        }

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
        if (op is InsertCellsShiftRightOp insCellsRight)
            return RewriteRangeInsertCellsShiftRight(rr, endRef, insCellsRight, hostSheetName, ref changed);
        if (op is InsertCellsShiftDownOp insCellsDown)
            return RewriteRangeInsertCellsShiftDown(rr, endRef, insCellsDown, hostSheetName, ref changed);

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

        // Excel treats A5:A1 identically to A1:A5 — normalize endpoint order before any band math
        // so a reversed range shrinks/collapses exactly like its normalized equivalent.
        uint s = Math.Min(rr.Start.Row, endRef.Row), e = Math.Max(rr.Start.Row, endRef.Row);
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
        var (newStartRef, newEndRef) = rr.Start.Row <= endRef.Row
            ? (rr.Start with { Row = newStart }, endRef with { Row = newEnd })
            : (rr.Start with { Row = newEnd }, endRef with { Row = newStart });
        return rr with { Start = newStartRef, End = newEndRef };
    }

    private static FormulaNode RewriteRangeDeleteCols(
        RangeRefNode rr, CellRefNode endRef, DeleteColsOp op, string hostSheetName, ref bool changed)
    {
        if (!Matches(rr.SheetName, op, hostSheetName))
            return rr;

        // Excel treats B3:A1 (columns reversed) identically to A1:B3 — normalize endpoint order
        // before any band math so a reversed range shrinks/collapses exactly like its normalized
        // equivalent.
        uint s = Math.Min(rr.Start.ColumnNumber, endRef.ColumnNumber);
        uint e = Math.Max(rr.Start.ColumnNumber, endRef.ColumnNumber);
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
        var (startCol, endCol) = rr.Start.ColumnNumber <= endRef.ColumnNumber
            ? (newStart, newEnd)
            : (newEnd, newStart);
        return rr with
        {
            Start = rr.Start with { ColumnName = CellAddress.NumberToColumnName(startCol) },
            End = endRef with { ColumnName = CellAddress.NumberToColumnName(endCol) },
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

        // Excel treats B3:A1 (columns reversed) the same as A1:B3 — normalize the perpendicular
        // (column) axis before checking whether it's fully inside the band's column scope.
        uint colStart = Math.Min(rr.Start.ColumnNumber, endRef.ColumnNumber);
        uint colEnd = Math.Max(rr.Start.ColumnNumber, endRef.ColumnNumber);
        if (colStart < op.RangeStartCol || colStart > op.RangeEndCol ||
            colEnd < op.RangeStartCol || colEnd > op.RangeEndCol)
        {
            // Range's columns aren't fully inside the deleted band's column scope: fall back to
            // rewriting each endpoint independently, same as any other op.
            return RewriteRangeGenericEndpoints(rr, endRef, op, hostSheetName, ref changed);
        }

        // Likewise normalize the row endpoints (e.g. A5:A1) before the band/shrink math.
        uint s = Math.Min(rr.Start.Row, endRef.Row), e = Math.Max(rr.Start.Row, endRef.Row);
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
        var (newStartRef, newEndRef) = rr.Start.Row <= endRef.Row
            ? (rr.Start with { Row = newStart }, endRef with { Row = newEnd })
            : (rr.Start with { Row = newEnd }, endRef with { Row = newStart });
        return rr with { Start = newStartRef, End = newEndRef };
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

        // Excel treats A3:A1 (rows reversed) the same as A1:A3 — normalize the perpendicular
        // (row) axis before checking whether it's fully inside the band's row scope.
        uint rowStart = Math.Min(rr.Start.Row, endRef.Row), rowEnd = Math.Max(rr.Start.Row, endRef.Row);
        if (rowStart < op.BandStartRow || rowStart > op.BandEndRow ||
            rowEnd < op.BandStartRow || rowEnd > op.BandEndRow)
        {
            // Range's rows aren't fully inside the deleted band's row scope: fall back to
            // rewriting each endpoint independently, same as any other op.
            return RewriteRangeGenericEndpoints(rr, endRef, op, hostSheetName, ref changed);
        }

        // Likewise normalize the column endpoints (e.g. B3:A1) before the band/shrink math.
        uint s = Math.Min(rr.Start.ColumnNumber, endRef.ColumnNumber);
        uint e = Math.Max(rr.Start.ColumnNumber, endRef.ColumnNumber);
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
        var (startCol, endCol) = rr.Start.ColumnNumber <= endRef.ColumnNumber
            ? (newStart, newEnd)
            : (newEnd, newStart);
        return rr with
        {
            Start = rr.Start with { ColumnName = CellAddress.NumberToColumnName(startCol) },
            End = endRef with { ColumnName = CellAddress.NumberToColumnName(endCol) },
        };
    }

    /// <summary>
    /// Insert Cells / Shift Right: a range ref only shifts when its ENTIRE row span sits inside the
    /// op's row band [<see cref="InsertCellsShiftRightOp.BandStartRow"/>..<see cref="InsertCellsShiftRightOp.BandEndRow"/>].
    /// A range that straddles the band (one row inside, one outside) can't be represented after a
    /// partial shift by a single rectangle -- shifting only the in-band corner's column while leaving
    /// the other corner's column untouched produces a bounding box that silently pulls in unrelated
    /// cells (e.g. <c>SUM(D1:D5)</c> with the band on row 1 only becomes <c>E1:D5</c>, which normalizes
    /// to the D1:E5 bounding box). Leave the range entirely untouched in that case (and likewise when
    /// its rows fall wholly outside the band), instead of falling back to the generic per-endpoint
    /// rewrite the delete-cells ops use.
    /// </summary>
    private static FormulaNode RewriteRangeInsertCellsShiftRight(
        RangeRefNode rr, CellRefNode endRef, InsertCellsShiftRightOp op, string hostSheetName, ref bool changed)
    {
        if (!Matches(rr.SheetName, op, hostSheetName))
            return rr;

        // Excel treats A5:A1 (rows reversed) the same as A1:A5 — normalize before the band check.
        uint rowStart = Math.Min(rr.Start.Row, endRef.Row);
        uint rowEnd = Math.Max(rr.Start.Row, endRef.Row);
        if (rowStart < op.BandStartRow || rowEnd > op.BandEndRow)
            return rr;

        return RewriteRangeGenericEndpoints(rr, endRef, op, hostSheetName, ref changed);
    }

    /// <summary>
    /// Insert Cells / Shift Down: mirrors <see cref="RewriteRangeInsertCellsShiftRight"/>, banded on
    /// columns instead of rows -- a range ref only shifts when its ENTIRE column span sits inside
    /// [<see cref="InsertCellsShiftDownOp.RangeStartCol"/>..<see cref="InsertCellsShiftDownOp.RangeEndCol"/>].
    /// </summary>
    private static FormulaNode RewriteRangeInsertCellsShiftDown(
        RangeRefNode rr, CellRefNode endRef, InsertCellsShiftDownOp op, string hostSheetName, ref bool changed)
    {
        if (!Matches(rr.SheetName, op, hostSheetName))
            return rr;

        // Excel treats B3:A1 (columns reversed) the same as A1:B3 — normalize before the band check.
        uint colStart = Math.Min(rr.Start.ColumnNumber, endRef.ColumnNumber);
        uint colEnd = Math.Max(rr.Start.ColumnNumber, endRef.ColumnNumber);
        if (colStart < op.RangeStartCol || colEnd > op.RangeEndCol)
            return rr;

        return RewriteRangeGenericEndpoints(rr, endRef, op, hostSheetName, ref changed);
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
            PasteTransposeOp transpose => RewriteFullColumnRangeTranspose(range, transpose, ref changed),
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
            PasteTransposeOp transpose => RewriteFullRowRangeTranspose(range, transpose, ref changed),
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

    // ── Paste transpose ───────────────────────────────────────────────────────

    private static FormulaNode RewriteCellRefTranspose(
        CellRefNode cr, PasteTransposeOp op, ref bool changed)
    {
        var newRow = cr.Row;
        var newColNum = cr.ColumnNumber;
        bool rowChanged = false, colChanged = false;

        // A relative COLUMN token takes on the position implied by this reference's ROW offset
        // from the source anchor -- transposing swaps which axis drives which. An absolute column
        // ($ on the column) keeps its literal value untouched, exactly like a plain paste offset.
        if (!cr.IsColAbsolute)
        {
            long rowOffsetFromSourceAnchor = (long)cr.Row - op.SourceAnchorRow;
            long c = (long)op.DestAnchorCol + rowOffsetFromSourceAnchor;
            if (c < 1 || c > CellAddress.MaxCol)
            {
                changed = true;
                return new ErrorNode(ErrorValue.Ref);
            }
            newColNum = (uint)c;
            colChanged = newColNum != cr.ColumnNumber;
        }

        // Likewise, a relative ROW token takes on the position implied by this reference's COLUMN
        // offset from the source anchor.
        if (!cr.IsRowAbsolute)
        {
            long colOffsetFromSourceAnchor = (long)cr.ColumnNumber - op.SourceAnchorCol;
            long r = (long)op.DestAnchorRow + colOffsetFromSourceAnchor;
            if (r < 1 || r > CellAddress.MaxRow)
            {
                changed = true;
                return new ErrorNode(ErrorValue.Ref);
            }
            newRow = (uint)r;
            rowChanged = newRow != cr.Row;
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
        // A delete that only partially overlaps the range must SHRINK to the surviving columns
        // (mirrors RewriteRangeDeleteCols' ShiftOrClampForDelete) — only #REF! when the ENTIRE
        // span is inside the deleted band. Rewriting each endpoint independently (as before)
        // wrongly collapsed the whole reference to #REF! whenever either endpoint alone fell
        // inside the deleted band, even when the other endpoint survived outside it.
        uint s = Math.Min(range.StartColumnNumber, range.EndColumnNumber);
        uint e = Math.Max(range.StartColumnNumber, range.EndColumnNumber);
        uint bandStart = op.StartCol, bandEnd = op.StartCol + op.Count - 1;

        if (bandStart <= s && e <= bandEnd)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        var newStart = ShiftOrClampForDelete(s, bandStart, bandEnd, op.Count, isRangeStart: true);
        var newEnd = ShiftOrClampForDelete(e, bandStart, bandEnd, op.Count, isRangeStart: false);
        if (newStart == s && newEnd == e)
            return range; // band entirely to the right of the range: no change

        changed = true;
        var (startCol, endCol) = range.StartColumnNumber <= range.EndColumnNumber
            ? (newStart, newEnd)
            : (newEnd, newStart);
        return range with
        {
            StartColumnName = CellAddress.NumberToColumnName(startCol),
            EndColumnName = CellAddress.NumberToColumnName(endCol)
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

    /// <summary>
    /// Paste Special &gt; Transpose on a full-column reference (e.g. B:B): a whole-column ref
    /// carries no row of its own, so transposing swaps axes the same way
    /// <see cref="RewriteCellRefTranspose"/>'s ROW branch does -- the column's own position
    /// becomes a ROW position, derived from its offset from <see cref="PasteTransposeOp.SourceAnchorCol"/>
    /// re-anchored at <see cref="PasteTransposeOp.DestAnchorRow"/>. An absolute column endpoint
    /// ($ on that side) keeps its literal numeric value unchanged, mirroring how
    /// RewriteCellRefTranspose leaves an absolute axis' literal value untouched instead of
    /// recomputing it from the offset.
    /// </summary>
    private static FormulaNode RewriteFullColumnRangeTranspose(
        FullColumnRangeRefNode range, PasteTransposeOp op, ref bool changed)
    {
        var startRow = TransposeColumnNumberToRow(range.StartColumnNumber, range.IsStartAbsolute, op);
        var endRow = TransposeColumnNumberToRow(range.EndColumnNumber, range.IsEndAbsolute, op);
        if (startRow is null || endRow is null)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        changed = true;
        return new FullRowRangeRefNode(
            startRow.Value,
            endRow.Value,
            IsStartAbsolute: range.IsStartAbsolute,
            IsEndAbsolute: range.IsEndAbsolute,
            SheetName: range.SheetName);
    }

    private static uint? TransposeColumnNumberToRow(uint columnNumber, bool isAbsolute, PasteTransposeOp op)
    {
        if (isAbsolute)
            return columnNumber <= CellAddress.MaxRow ? columnNumber : null;

        long colOffsetFromSourceAnchor = (long)columnNumber - op.SourceAnchorCol;
        long r = (long)op.DestAnchorRow + colOffsetFromSourceAnchor;
        return r >= 1 && r <= CellAddress.MaxRow ? (uint?)r : null;
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
        // A delete that only partially overlaps the range must SHRINK to the surviving rows
        // (mirrors RewriteRangeDeleteRows' ShiftOrClampForDelete) — only #REF! when the ENTIRE
        // span is inside the deleted band. Rewriting each endpoint independently (as before)
        // wrongly collapsed the whole reference to #REF! whenever either endpoint alone fell
        // inside the deleted band, even when the other endpoint survived outside it.
        uint s = Math.Min(range.StartRow, range.EndRow);
        uint e = Math.Max(range.StartRow, range.EndRow);
        uint bandStart = op.StartRow, bandEnd = op.StartRow + op.Count - 1;

        if (bandStart <= s && e <= bandEnd)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        var newStart = ShiftOrClampForDelete(s, bandStart, bandEnd, op.Count, isRangeStart: true);
        var newEnd = ShiftOrClampForDelete(e, bandStart, bandEnd, op.Count, isRangeStart: false);
        if (newStart == s && newEnd == e)
            return range; // band entirely below the range: no change

        changed = true;
        var (startRow, endRow) = range.StartRow <= range.EndRow
            ? (newStart, newEnd)
            : (newEnd, newStart);
        return range with { StartRow = startRow, EndRow = endRow };
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

    /// <summary>
    /// Paste Special &gt; Transpose on a full-row reference (e.g. 1:1): the mirror image of
    /// <see cref="RewriteFullColumnRangeTranspose"/> -- the row's own position becomes a COLUMN
    /// position, derived from its offset from <see cref="PasteTransposeOp.SourceAnchorRow"/>
    /// re-anchored at <see cref="PasteTransposeOp.DestAnchorCol"/>. Unlike the column→row
    /// direction, row numbers routinely exceed <see cref="CellAddress.MaxCol"/>, so the overflow
    /// check below is the common case, not just a defensive guard.
    /// </summary>
    private static FormulaNode RewriteFullRowRangeTranspose(
        FullRowRangeRefNode range, PasteTransposeOp op, ref bool changed)
    {
        var startCol = TransposeRowNumberToColumn(range.StartRow, range.IsStartAbsolute, op);
        var endCol = TransposeRowNumberToColumn(range.EndRow, range.IsEndAbsolute, op);
        if (startCol is null || endCol is null)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        changed = true;
        return new FullColumnRangeRefNode(
            CellAddress.NumberToColumnName(startCol.Value),
            CellAddress.NumberToColumnName(endCol.Value),
            IsStartAbsolute: range.IsStartAbsolute,
            IsEndAbsolute: range.IsEndAbsolute,
            SheetName: range.SheetName);
    }

    private static uint? TransposeRowNumberToColumn(uint rowNumber, bool isAbsolute, PasteTransposeOp op)
    {
        if (isAbsolute)
            return rowNumber <= CellAddress.MaxCol ? rowNumber : null;

        long rowOffsetFromSourceAnchor = (long)rowNumber - op.SourceAnchorRow;
        long c = (long)op.DestAnchorCol + rowOffsetFromSourceAnchor;
        return c >= 1 && c <= CellAddress.MaxCol ? (uint?)c : null;
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
        if (op is PasteOffsetOp or PasteTransposeOp) return true;   // paste always adjusts
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
