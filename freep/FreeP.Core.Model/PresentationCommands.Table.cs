using Free.Shared.Commands;

namespace FreeP.Core.Model;

// ════════════════════════════════════════════════════════════════════════════════
// TABLE EDIT COMMANDS  (Wave 9A)
//
// All commands work on a table shape identified by (slideIndex, shapeId).
// The helper FindTable() retrieves the TableShape; if the shape is not found
// or its Table payload is null the command is a no-op (safe to call).
//
// Undo/redo contract: every command captures the minimum prior state needed
// to fully revert.  Captured data is deep-cloned so later mutations cannot
// corrupt the snapshot.
//
// Merge semantics follow OOXML a:tbl:
//   • The top-left cell of the merged region is the "anchor": GridSpan/RowSpan > 1.
//   • Every other cell in the region has HMerge=true (same row) or VMerge=true
//     (rows below the first), with GridSpan=1, RowSpan=1.
//   • The compositor skips HMerge/VMerge cells and sizes the anchor cell by
//     summing its GridSpan columns and RowSpan rows.
// ════════════════════════════════════════════════════════════════════════════════

// ── shared file-local helpers ────────────────────────────────────────────────

file static class TableCommandHelper
{
    internal static TableShape? FindTable(Presentation p, int slideIndex, uint shapeId)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return null;
        var shape = p.Slides[slideIndex].Shapes.FirstOrDefault(s => s.Id == shapeId);
        return shape?.Table;
    }

    // Deep-clone a TableShape so undo can restore exact prior state.
    internal static TableShape CloneTable(TableShape src)
    {
        var copy = new TableShape
        {
            Flags        = CloneFlags(src.Flags),
            TableStyleId = src.TableStyleId,
            StyleData    = src.StyleData, // read-only from XML – share
        };
        foreach (var w in src.ColumnWidthsEmu)
            copy.ColumnWidthsEmu.Add(w);
        foreach (var row in src.Rows)
            copy.Rows.Add(CloneRow(row));
        return copy;
    }

    internal static TableStyleFlags CloneFlags(TableStyleFlags f) => new()
    {
        FirstRow = f.FirstRow, LastRow = f.LastRow,
        FirstCol = f.FirstCol, LastCol = f.LastCol,
        BandRow  = f.BandRow,  BandCol = f.BandCol,
    };

    internal static TableRow CloneRow(TableRow row)
    {
        var r = new TableRow { HeightEmu = row.HeightEmu };
        foreach (var cell in row.Cells)
            r.Cells.Add(CloneCell(cell));
        return r;
    }

    internal static TableCell CloneCell(TableCell src) => new()
    {
        TextBody      = src.TextBody is null ? null : CloneTextBody(src.TextBody),
        Fill          = src.Fill,
        Borders       = src.Borders,
        GridSpan      = src.GridSpan,
        RowSpan       = src.RowSpan,
        HMerge        = src.HMerge,
        VMerge        = src.VMerge,
        InsetLeftPt   = src.InsetLeftPt,
        InsetRightPt  = src.InsetRightPt,
        InsetTopPt    = src.InsetTopPt,
        InsetBottomPt = src.InsetBottomPt,
        Anchor        = src.Anchor,
    };

    internal static TextBody CloneTextBody(TextBody tb)
    {
        var copy = new TextBody
        {
            Anchor           = tb.Anchor,
            DefaultParaAlign = tb.DefaultParaAlign,
            InsetLeftPt      = tb.InsetLeftPt,
            InsetRightPt     = tb.InsetRightPt,
            InsetTopPt       = tb.InsetTopPt,
            InsetBottomPt    = tb.InsetBottomPt,
            Wrap             = tb.Wrap,
            AutoFit          = tb.AutoFit,
        };
        foreach (var para in tb.Paragraphs)
        {
            var cp = new Paragraph
            {
                Align         = para.Align,
                Level         = para.Level,
                BulletKind    = para.BulletKind,
                BulletChar    = para.BulletChar,
                SpaceBeforePt = para.SpaceBeforePt,
                SpaceAfterPt  = para.SpaceAfterPt,
            };
            foreach (var run in para.Runs)
                cp.Runs.Add(new Run
                {
                    Text          = run.Text,
                    FontFamily    = run.FontFamily,
                    FontSizePt    = run.FontSizePt,
                    Bold          = run.Bold,
                    Italic        = run.Italic,
                    Underline     = run.Underline,
                    Strikethrough = run.Strikethrough,
                    Color         = run.Color,
                });
            copy.Paragraphs.Add(cp);
        }
        return copy;
    }

    /// <summary>Replace the whole table's rows/widths from a clone snapshot (for revert).</summary>
    internal static void RestoreTableState(TableShape table, TableShape snapshot)
    {
        table.ColumnWidthsEmu.Clear();
        foreach (var w in snapshot.ColumnWidthsEmu)
            table.ColumnWidthsEmu.Add(w);

        table.Rows.Clear();
        foreach (var row in snapshot.Rows)
            table.Rows.Add(CloneRow(row)); // clone again so the snapshot remains pristine

        table.Flags     = CloneFlags(snapshot.Flags);
        table.TableStyleId = snapshot.TableStyleId;
        // StyleData stays — it is read-only XML data
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 1. SetTableCellTextCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Replaces the <see cref="TextBody"/> of the cell at (<paramref name="row"/>, <paramref name="col"/>)
/// with <paramref name="newBody"/>. Captures the previous body for undo.
/// </summary>
public sealed class SetTableCellTextCommand : IPresentationCommand
{
    private readonly int       _slideIndex;
    private readonly uint      _shapeId;
    private readonly int       _row;
    private readonly int       _col;
    private readonly TextBody? _newBody;
    private TextBody?          _oldBody;

    public SetTableCellTextCommand(int slideIndex, uint shapeId, int row, int col, TextBody? newBody)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _row        = row;
        _col        = col;
        _newBody    = newBody is null ? null : TableCommandHelper.CloneTextBody(newBody);
    }

    public string Label => "Edit Cell Text";

    public void Apply(Presentation p)
    {
        var cell = GetCell(p);
        if (cell is null) return;
        _oldBody     = cell.TextBody is null ? null : TableCommandHelper.CloneTextBody(cell.TextBody);
        cell.TextBody = _newBody is null ? null : TableCommandHelper.CloneTextBody(_newBody);
    }

    public void Revert(Presentation p)
    {
        var cell = GetCell(p);
        if (cell is null) return;
        cell.TextBody = _oldBody is null ? null : TableCommandHelper.CloneTextBody(_oldBody);
    }

    private TableCell? GetCell(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return null;
        if (_row < 0 || _row >= table.Rows.Count) return null;
        var row = table.Rows[_row];
        if (_col < 0 || _col >= row.Cells.Count) return null;
        return row.Cells[_col];
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 2. InsertTableRowCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Inserts a new blank row at <paramref name="atRow"/> (rows at and after shift down).
/// The new row gets the same height as the adjacent row (or a default if the table is empty).
/// Grid integrity: one cell per column, all GridSpan=1 RowSpan=1.
/// Captures a full table snapshot for undo.
/// </summary>
public sealed class InsertTableRowCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _atRow;
    private TableShape?   _snapshot;

    public InsertTableRowCommand(int slideIndex, uint shapeId, int atRow)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _atRow      = atRow;
    }

    public string Label => "Insert Row";

    public void Apply(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return;

        // Snapshot before mutation.
        _snapshot = TableCommandHelper.CloneTable(table);

        int cols = table.ColumnWidthsEmu.Count;
        // Default height: match previous row if available, else next row, else 457200 EMU (~0.5 inch).
        int idx = Math.Clamp(_atRow, 0, table.Rows.Count);
        long height = idx > 0
            ? table.Rows[idx - 1].HeightEmu
            : (table.Rows.Count > 0 ? table.Rows[0].HeightEmu : 457200L);

        var newRow = new TableRow { HeightEmu = height };
        for (int c = 0; c < cols; c++)
            newRow.Cells.Add(new TableCell());

        table.Rows.Insert(idx, newRow);
    }

    public void Revert(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _snapshot is null) return;
        TableCommandHelper.RestoreTableState(table, _snapshot);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 3. DeleteTableRowCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Deletes the row at <paramref name="atRow"/>. No-op if that would leave the table with zero rows.
/// Captures a full table snapshot for undo.
/// </summary>
public sealed class DeleteTableRowCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _atRow;
    private TableShape?   _snapshot;

    public DeleteTableRowCommand(int slideIndex, uint shapeId, int atRow)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _atRow      = atRow;
    }

    public string Label => "Delete Row";

    public void Apply(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return;
        if (table.Rows.Count <= 1) return; // keep at least one row
        if (_atRow < 0 || _atRow >= table.Rows.Count) return;

        _snapshot = TableCommandHelper.CloneTable(table);
        table.Rows.RemoveAt(_atRow);
    }

    public void Revert(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _snapshot is null) return;
        TableCommandHelper.RestoreTableState(table, _snapshot);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 4. InsertTableColumnCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Inserts a new blank column at <paramref name="atCol"/>.
/// The new column gets the same width as the adjacent column (or a default).
/// Captures a full table snapshot for undo.
/// </summary>
public sealed class InsertTableColumnCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _atCol;
    private TableShape?   _snapshot;

    public InsertTableColumnCommand(int slideIndex, uint shapeId, int atCol)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _atCol      = atCol;
    }

    public string Label => "Insert Column";

    public void Apply(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return;

        _snapshot = TableCommandHelper.CloneTable(table);

        int idx = Math.Clamp(_atCol, 0, table.ColumnWidthsEmu.Count);
        // Default width: match adjacent column or 914400 EMU (1 inch).
        long width = idx > 0
            ? table.ColumnWidthsEmu[idx - 1]
            : (table.ColumnWidthsEmu.Count > 0 ? table.ColumnWidthsEmu[0] : 914400L);

        table.ColumnWidthsEmu.Insert(idx, width);

        // Insert a blank cell in each row at the same column index.
        foreach (var row in table.Rows)
        {
            int cellIdx = Math.Clamp(idx, 0, row.Cells.Count);
            row.Cells.Insert(cellIdx, new TableCell());
        }
    }

    public void Revert(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _snapshot is null) return;
        TableCommandHelper.RestoreTableState(table, _snapshot);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 5. DeleteTableColumnCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Deletes the column at <paramref name="atCol"/>. No-op if that would leave the table with zero columns.
/// Captures a full table snapshot for undo.
/// </summary>
public sealed class DeleteTableColumnCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _atCol;
    private TableShape?   _snapshot;

    public DeleteTableColumnCommand(int slideIndex, uint shapeId, int atCol)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _atCol      = atCol;
    }

    public string Label => "Delete Column";

    public void Apply(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return;
        if (table.ColumnWidthsEmu.Count <= 1) return; // keep at least one column
        if (_atCol < 0 || _atCol >= table.ColumnWidthsEmu.Count) return;

        _snapshot = TableCommandHelper.CloneTable(table);
        table.ColumnWidthsEmu.RemoveAt(_atCol);

        foreach (var row in table.Rows)
        {
            if (_atCol < row.Cells.Count)
                row.Cells.RemoveAt(_atCol);
        }
    }

    public void Revert(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _snapshot is null) return;
        TableCommandHelper.RestoreTableState(table, _snapshot);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 6. MergeTableCellsCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Merges the rectangular region [r1,c1]..[r2,c2] (inclusive, order-independent).
/// The top-left cell becomes the anchor (GridSpan = colCount, RowSpan = rowCount).
/// All other cells in the region are marked HMerge/VMerge.
/// Text from all merged cells is concatenated into the anchor (newlines between non-empty cells).
/// Captures a full table snapshot for undo.
/// </summary>
public sealed class MergeTableCellsCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _r1, _c1, _r2, _c2;
    private TableShape?   _snapshot;

    /// <param name="r1">Row of first corner.</param>
    /// <param name="c1">Column of first corner.</param>
    /// <param name="r2">Row of second corner.</param>
    /// <param name="c2">Column of second corner.</param>
    public MergeTableCellsCommand(int slideIndex, uint shapeId, int r1, int c1, int r2, int c2)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        // Normalise so r1 <= r2, c1 <= c2.
        _r1 = Math.Min(r1, r2);
        _c1 = Math.Min(c1, c2);
        _r2 = Math.Max(r1, r2);
        _c2 = Math.Max(c1, c2);
    }

    public string Label => "Merge Cells";

    public void Apply(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return;
        if (_r2 >= table.Rows.Count || _c2 >= table.ColumnWidthsEmu.Count) return;
        if (_r1 == _r2 && _c1 == _c2) return; // nothing to merge

        _snapshot = TableCommandHelper.CloneTable(table);

        int gridSpan = _c2 - _c1 + 1;
        int rowSpan  = _r2 - _r1 + 1;

        // Collect text from all cells in the region to put in the anchor.
        var texts = new List<string>();
        for (int r = _r1; r <= _r2; r++)
        {
            for (int c = _c1; c <= _c2; c++)
            {
                var cell = table.Rows[r].Cells[c];
                var cellText = GetPlainText(cell.TextBody);
                if (!string.IsNullOrWhiteSpace(cellText))
                    texts.Add(cellText);
            }
        }

        // Set all cells to HMerge/VMerge first.
        for (int r = _r1; r <= _r2; r++)
        {
            for (int c = _c1; c <= _c2; c++)
            {
                var cell = table.Rows[r].Cells[c];
                if (r == _r1 && c == _c1)
                {
                    // Anchor cell.
                    cell.GridSpan = gridSpan;
                    cell.RowSpan  = rowSpan;
                    cell.HMerge   = false;
                    cell.VMerge   = false;
                    // Put merged text into anchor.
                    if (texts.Count > 0)
                        cell.TextBody = MakeTextBody(string.Join("\n", texts));
                }
                else
                {
                    cell.GridSpan = 1;
                    cell.RowSpan  = 1;
                    cell.HMerge   = (r == _r1); // same row as anchor → HMerge
                    cell.VMerge   = (r > _r1);  // rows below anchor → VMerge
                    cell.TextBody = null;
                }
            }
        }
    }

    public void Revert(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _snapshot is null) return;
        TableCommandHelper.RestoreTableState(table, _snapshot);
    }

    private static string GetPlainText(TextBody? body)
    {
        if (body is null) return string.Empty;
        return string.Join("\n", body.Paragraphs.SelectMany(pa => pa.Runs).Select(r => r.Text));
    }

    private static TextBody MakeTextBody(string text)
    {
        var body = new TextBody { Wrap = true };
        foreach (var line in text.Split('\n'))
        {
            var para = new Paragraph();
            para.Runs.Add(new Run { Text = line });
            body.Paragraphs.Add(para);
        }
        return body;
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 7. SplitTableCellCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Splits the merged cell at (<paramref name="row"/>, <paramref name="col"/>) back into individual cells.
/// If the target cell is not an anchor (GridSpan=1, RowSpan=1) the command is a no-op.
/// The anchor's TextBody is kept in the anchor; all newly-split cells are blank.
/// Captures a full table snapshot for undo.
/// </summary>
public sealed class SplitTableCellCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _row;
    private readonly int  _col;
    private TableShape?   _snapshot;

    public SplitTableCellCommand(int slideIndex, uint shapeId, int row, int col)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _row        = row;
        _col        = col;
    }

    public string Label => "Split Cell";

    /// <summary>No effect unless the target cell exists and is actually merged.</summary>
    public bool HasEffect(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _row < 0 || _row >= table.Rows.Count) return false;
        var anchor = table.Rows[_row].Cells.ElementAtOrDefault(_col);
        return anchor is not null && (anchor.GridSpan > 1 || anchor.RowSpan > 1);
    }

    public void Apply(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return;
        if (_row < 0 || _row >= table.Rows.Count) return;
        var anchor = table.Rows[_row].Cells.ElementAtOrDefault(_col);
        if (anchor is null) return;

        bool isMerged = anchor.GridSpan > 1 || anchor.RowSpan > 1;
        if (!isMerged) return;

        _snapshot = TableCommandHelper.CloneTable(table);

        int gridSpan = anchor.GridSpan;
        int rowSpan  = anchor.RowSpan;

        // Clear the merge on the anchor.
        anchor.GridSpan = 1;
        anchor.RowSpan  = 1;

        // Restore all covered cells to blank/unmerged.
        for (int r = _row; r < _row + rowSpan && r < table.Rows.Count; r++)
        {
            for (int c = _col; c < _col + gridSpan && c < table.ColumnWidthsEmu.Count; c++)
            {
                if (r == _row && c == _col) continue; // anchor already fixed
                var cell = table.Rows[r].Cells.ElementAtOrDefault(c);
                if (cell is null) continue;
                cell.GridSpan = 1;
                cell.RowSpan  = 1;
                cell.HMerge   = false;
                cell.VMerge   = false;
                // Leave TextBody null (blank cell after split).
            }
        }
    }

    public void Revert(Presentation p)
    {
        var table = TableCommandHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _snapshot is null) return;
        TableCommandHelper.RestoreTableState(table, _snapshot);
    }
}
