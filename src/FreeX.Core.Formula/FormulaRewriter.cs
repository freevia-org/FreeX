using FreeX.Core.Model;

namespace FreeX.Core.Formula;

// ── Operation types ───────────────────────────────────────────────────────────

public abstract record RewriteOperation;
public sealed record InsertRowsOp(string SheetName, uint BeforeRow, uint Count) : RewriteOperation;
public sealed record DeleteRowsOp(string SheetName, uint StartRow,  uint Count) : RewriteOperation;
public sealed record InsertColsOp(string SheetName, uint BeforeCol, uint Count) : RewriteOperation;
public sealed record DeleteColsOp(string SheetName, uint StartCol,  uint Count) : RewriteOperation;
public sealed record PasteOffsetOp(int RowDelta, int ColDelta)                  : RewriteOperation;
public sealed record RenameSheetOp(string OldSheetName, string NewSheetName)    : RewriteOperation;
public sealed record DeleteSheetOp(string SheetName)                            : RewriteOperation;

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
            RenameSheetOp rename => RewriteCellRefRenameSheet(cr, rename, ref changed),
            DeleteSheetOp => RewriteSheetQualifiedRefDeleteSheet(ref changed),
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
            _ => null
        };

        if (opSheet is null) return false;

        var refSheet = refSheetName ?? hostSheetName;
        return string.Equals(refSheet, opSheet, StringComparison.OrdinalIgnoreCase);
    }
}
