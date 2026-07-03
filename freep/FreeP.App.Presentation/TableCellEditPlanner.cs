using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum TableCellEditStartStatus
{
    Ready,
    MissingSlide,
    ShapeNotFound,
    NotTable,
    CellOutOfRange,
    MissingCellBounds,
}

public enum TableCellTextFormatKind
{
    Bold,
    Italic,
    Underline,
}

public enum TableCellTextValueFormatKind
{
    FontFamily,
    FontSize,
    Color,
}

public enum TableCellTextFormatStatus
{
    Ready,
    MissingSlide,
    ShapeNotFound,
    NotTable,
    MissingActiveCell,
    CellOutOfRange,
    MissingTextBody,
    NoTextRuns,
}

public sealed record TableCellEditState(
    uint? ShapeId,
    int? Row,
    int? Col,
    bool HasSelectedTable,
    bool HasActiveCell,
    bool CanEditText,
    bool CanFormatText,
    bool CanInsertRow,
    bool CanInsertColumn,
    bool CanDeleteRow,
    bool CanDeleteColumn,
    bool CanMergeWithRight,
    bool CanMergeWithBelow,
    bool CanSplitCell)
{
    public static readonly TableCellEditState None = new(
        null,
        null,
        null,
        HasSelectedTable: false,
        HasActiveCell: false,
        CanEditText: false,
        CanFormatText: false,
        CanInsertRow: false,
        CanInsertColumn: false,
        CanDeleteRow: false,
        CanDeleteColumn: false,
        CanMergeWithRight: false,
        CanMergeWithBelow: false,
        CanSplitCell: false);
}

public sealed record TableCellEditStartPlan(
    TableCellEditStartStatus Status,
    uint ShapeId,
    int Row,
    int Col,
    TableCell? Cell,
    CellRectDip? CellRect,
    InCanvasEditorPlacement? Placement,
    TextBody? OriginalBody,
    InCanvasTableCellTextEditPlanner? EditPlanner)
{
    public bool IsReady => Status == TableCellEditStartStatus.Ready;
}

public sealed record TableCellTextFormatPlan(
    TableCellTextFormatStatus Status,
    uint? ShapeId,
    int? Row,
    int? Col,
    TableCellTextFormatKind Kind,
    bool? TargetValue,
    IPresentationCommand? Command)
{
    public bool IsReady => Status == TableCellTextFormatStatus.Ready && Command is not null;
}

public sealed record TableCellTextValueFormatPlan(
    TableCellTextFormatStatus Status,
    uint? ShapeId,
    int? Row,
    int? Col,
    TableCellTextValueFormatKind Kind,
    object? Value,
    IPresentationCommand? Command)
{
    public bool IsReady => Status == TableCellTextFormatStatus.Ready && Command is not null;
}

public static class TableCellEditPlanner
{
    public static TableCellEditState PlanSelectedCell(
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell)
    {
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        if (slide is null || selectedShapeIds.Count == 0)
            return TableCellEditState.None;

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == selectedShapeIds[0]);
        if (shape?.Kind != SlideShapeKind.Table || shape.Table is null)
            return TableCellEditState.None;

        if (activeCell is not { } requested)
        {
            return TableCellEditState.None with
            {
                ShapeId = shape.Id,
                HasSelectedTable = true,
                CanInsertRow = shape.Table.Rows.Count > 0,
                CanInsertColumn = shape.Table.ColumnWidthsEmu.Count > 0,
                CanDeleteRow = shape.Table.Rows.Count > 1,
                CanDeleteColumn = shape.Table.ColumnWidthsEmu.Count > 1,
            };
        }

        var normalized = NormalizeCell(shape.Table, requested.Row, requested.Col);
        if (normalized is null)
        {
            return TableCellEditState.None with
            {
                ShapeId = shape.Id,
                HasSelectedTable = true,
            };
        }

        var cell = normalized.Value.Cell;
        int row = normalized.Value.Row;
        int col = normalized.Value.Col;
        int colSpan = Math.Max(1, cell.GridSpan);
        int rowSpan = Math.Max(1, cell.RowSpan);

        return new TableCellEditState(
            shape.Id,
            row,
            col,
            HasSelectedTable: true,
            HasActiveCell: true,
            CanEditText: true,
            CanFormatText: true,
            CanInsertRow: true,
            CanInsertColumn: shape.Table.ColumnWidthsEmu.Count > 0,
            CanDeleteRow: shape.Table.Rows.Count > 1,
            CanDeleteColumn: shape.Table.ColumnWidthsEmu.Count > 1,
            CanMergeWithRight: col + colSpan < shape.Table.ColumnWidthsEmu.Count,
            CanMergeWithBelow: row + rowSpan < shape.Table.Rows.Count,
            CanSplitCell: colSpan > 1 || rowSpan > 1);
    }

    public static TableCellEditStartPlan BeginEdit(
        int slideIndex,
        Slide? slide,
        uint shapeId,
        int row,
        int col,
        SlideTransformCore transform,
        double minimumWidth,
        double minimumHeight)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumHeight);

        if (slide is null)
            return NotReady(TableCellEditStartStatus.MissingSlide, shapeId, row, col);

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == shapeId);
        if (shape is null)
            return NotReady(TableCellEditStartStatus.ShapeNotFound, shapeId, row, col);
        if (shape.Kind != SlideShapeKind.Table || shape.Table is null)
            return NotReady(TableCellEditStartStatus.NotTable, shapeId, row, col);

        var normalized = NormalizeCell(shape.Table, row, col);
        if (normalized is null)
            return NotReady(TableCellEditStartStatus.CellOutOfRange, shapeId, row, col);

        var cellRect = TableCellHitTester.GetCellRect(shape, normalized.Value.Row, normalized.Value.Col);
        if (cellRect is null)
            return NotReady(TableCellEditStartStatus.MissingCellBounds, shapeId, normalized.Value.Row, normalized.Value.Col);

        var screenRect = SlideCanvasGeometryPlanner.DipBoundsToScreen(cellRect.Value, transform);
        var placement = SlideCanvasGeometryPlanner.PlanEditorPlacement(
            screenRect,
            minimumWidth,
            minimumHeight);
        var originalBody = TextBodyModelCloner.CloneTextBody(normalized.Value.Cell.TextBody);

        return new TableCellEditStartPlan(
            TableCellEditStartStatus.Ready,
            shapeId,
            normalized.Value.Row,
            normalized.Value.Col,
            normalized.Value.Cell,
            cellRect.Value,
            placement,
            originalBody,
            InCanvasTableCellTextEditPlanner.BeginRichText(
                slideIndex,
                shapeId,
                normalized.Value.Row,
                normalized.Value.Col,
                normalized.Value.Cell.TextBody));
    }

    public static InCanvasTextEditDecision CommitRichText(
        InCanvasTableCellTextEditPlanner? editPlanner,
        TextBody editedBody)
    {
        ArgumentNullException.ThrowIfNull(editedBody);

        return editPlanner?.CommitRichText(editedBody)
            ?? new InCanvasTextEditDecision(InCanvasTextEditOutcome.Unchanged, null);
    }

    public static InCanvasTextEditDecision Cancel(InCanvasTableCellTextEditPlanner? editPlanner) =>
        editPlanner?.Cancel()
        ?? new InCanvasTextEditDecision(InCanvasTextEditOutcome.Canceled, null);

    public static TableCellTextFormatPlan PlanTextFormat(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        TableCellTextFormatKind kind,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        if (slide is null)
            return DisabledFormat(TableCellTextFormatStatus.MissingSlide, kind);
        if (selectedShapeIds.Count == 0)
            return DisabledFormat(TableCellTextFormatStatus.ShapeNotFound, kind);

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == selectedShapeIds[0]);
        if (shape is null)
            return DisabledFormat(TableCellTextFormatStatus.ShapeNotFound, kind);
        if (shape.Kind != SlideShapeKind.Table || shape.Table is null)
            return DisabledFormat(TableCellTextFormatStatus.NotTable, kind, shape.Id);
        if (activeCell is not { } requested)
            return DisabledFormat(TableCellTextFormatStatus.MissingActiveCell, kind, shape.Id);

        var normalized = NormalizeCell(shape.Table, requested.Row, requested.Col);
        if (normalized is null)
            return DisabledFormat(TableCellTextFormatStatus.CellOutOfRange, kind, shape.Id);

        var (row, col, cell) = normalized.Value;
        if (cell.TextBody is null)
            return DisabledFormat(TableCellTextFormatStatus.MissingTextBody, kind, shape.Id, row, col);

        var runs = cell.TextBody.Paragraphs.SelectMany(p => p.Runs).ToList();
        if (runs.Count == 0)
            return DisabledFormat(TableCellTextFormatStatus.NoTextRuns, kind, shape.Id, row, col);

        // Normalize the selection to a non-negative, ordered, non-collapsed range within the
        // cell's plain-text length. A null/collapsed/out-of-range selection falls back to the
        // whole-cell behavior (matches the app convention for a collapsed caret).
        int textLength = runs.Sum(r => r.Text.Length) + Math.Max(0, cell.TextBody.Paragraphs.Count - 1);
        (int Start, int End)? range = NormalizeSelection(selection, textLength);

        var editedBody = TextBodyModelCloner.CloneTextBody(cell.TextBody)!;
        bool targetValue;
        if (range is { } r)
        {
            var selectedRuns = SplitRunsAtSelection(editedBody, r.Start, r.End);
            targetValue = selectedRuns.Count == 0 || !selectedRuns.All(run => GetRunFormat(run, kind));
            foreach (var run in selectedRuns)
                SetRunFormat(run, kind, targetValue);
            MergeAdjacentRunsWithSameFormat(editedBody);
        }
        else
        {
            targetValue = !runs.All(run => GetRunFormat(run, kind));
            foreach (var run in editedBody.Paragraphs.SelectMany(p => p.Runs))
                SetRunFormat(run, kind, targetValue);
        }

        return new TableCellTextFormatPlan(
            TableCellTextFormatStatus.Ready,
            shape.Id,
            row,
            col,
            kind,
            targetValue,
            new SetTableCellTextCommand(slideIndex, shape.Id, row, col, editedBody));
    }

    public static TableCellTextValueFormatPlan PlanFontFamily(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        string? fontFamily,
        (int Start, int End)? selection = null) =>
        PlanTextValueFormat(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            TableCellTextValueFormatKind.FontFamily,
            fontFamily,
            selection);

    public static TableCellTextValueFormatPlan PlanFontSize(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        double? sizePt,
        (int Start, int End)? selection = null) =>
        PlanTextValueFormat(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            TableCellTextValueFormatKind.FontSize,
            sizePt,
            selection);

    public static TableCellTextValueFormatPlan PlanColor(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        ThemeAwareColor? color,
        (int Start, int End)? selection = null) =>
        PlanTextValueFormat(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            TableCellTextValueFormatKind.Color,
            color,
            selection);

    private static TableCellTextValueFormatPlan PlanTextValueFormat(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        TableCellTextValueFormatKind kind,
        object? value,
        (int Start, int End)? selection)
    {
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        if (slide is null)
            return DisabledValueFormat(TableCellTextFormatStatus.MissingSlide, kind, value);
        if (selectedShapeIds.Count == 0)
            return DisabledValueFormat(TableCellTextFormatStatus.ShapeNotFound, kind, value);

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == selectedShapeIds[0]);
        if (shape is null)
            return DisabledValueFormat(TableCellTextFormatStatus.ShapeNotFound, kind, value);
        if (shape.Kind != SlideShapeKind.Table || shape.Table is null)
            return DisabledValueFormat(TableCellTextFormatStatus.NotTable, kind, value, shape.Id);
        if (activeCell is not { } requested)
            return DisabledValueFormat(TableCellTextFormatStatus.MissingActiveCell, kind, value, shape.Id);

        var normalized = NormalizeCell(shape.Table, requested.Row, requested.Col);
        if (normalized is null)
            return DisabledValueFormat(TableCellTextFormatStatus.CellOutOfRange, kind, value, shape.Id);

        var (row, col, cell) = normalized.Value;
        if (cell.TextBody is null)
            return DisabledValueFormat(TableCellTextFormatStatus.MissingTextBody, kind, value, shape.Id, row, col);

        var runs = cell.TextBody.Paragraphs.SelectMany(p => p.Runs).ToList();
        if (runs.Count == 0)
            return DisabledValueFormat(TableCellTextFormatStatus.NoTextRuns, kind, value, shape.Id, row, col);

        int textLength = runs.Sum(r => r.Text.Length) + Math.Max(0, cell.TextBody.Paragraphs.Count - 1);
        (int Start, int End)? range = NormalizeSelection(selection, textLength);

        var editedBody = TextBodyModelCloner.CloneTextBody(cell.TextBody)!;
        var targetRuns = range is { } r
            ? SplitRunsAtSelection(editedBody, r.Start, r.End)
            : editedBody.Paragraphs.SelectMany(p => p.Runs).ToList();

        foreach (var run in targetRuns)
            SetRunValueFormat(run, kind, value);

        MergeAdjacentRunsWithSameFormat(editedBody);

        return new TableCellTextValueFormatPlan(
            TableCellTextFormatStatus.Ready,
            shape.Id,
            row,
            col,
            kind,
            value,
            new SetTableCellTextCommand(slideIndex, shape.Id, row, col, editedBody));
    }

    private static (int Start, int End)? NormalizeSelection((int Start, int End)? selection, int textLength)
    {
        if (selection is not { } s)
            return null;

        int start = Math.Min(s.Start, s.End);
        int end = Math.Max(s.Start, s.End);
        start = Math.Clamp(start, 0, textLength);
        end = Math.Clamp(end, 0, textLength);
        return end > start ? (start, end) : null;
    }

    /// <summary>
    /// Splits the runs of <paramref name="body"/> (in place) at the [start, end) character
    /// boundaries of its concatenated plain text (paragraphs joined with '\n', matching
    /// <see cref="InCanvasTextEditPlanner.ExtractPlainText"/>), and returns the list of runs
    /// that fall entirely within the selection so callers can apply formatting to just them.
    /// Runs entirely outside the range are left untouched; runs straddling a boundary are
    /// split into an in-range and an out-of-range run (cloned formatting, sliced text).
    /// </summary>
    private static List<Run> SplitRunsAtSelection(TextBody body, int start, int end)
    {
        var selected = new List<Run>();
        int cursor = 0;

        for (int pi = 0; pi < body.Paragraphs.Count; pi++)
        {
            if (pi > 0)
                cursor += 1; // '\n' joining separator, matches ExtractPlainText

            var paragraph = body.Paragraphs[pi];
            var newRuns = new List<Run>();

            foreach (var run in paragraph.Runs)
            {
                int runStart = cursor;
                int runLen = run.Text.Length;
                int runEnd = runStart + runLen;
                cursor = runEnd;

                int overlapStart = Math.Max(runStart, start);
                int overlapEnd = Math.Min(runEnd, end);

                if (overlapEnd <= overlapStart)
                {
                    // No overlap with the selection at all.
                    newRuns.Add(run);
                    continue;
                }

                // Slice into up to three pieces: before (unselected), middle (selected), after (unselected).
                int beforeLen = overlapStart - runStart;
                int selectedLen = overlapEnd - overlapStart;
                int afterLen = runEnd - overlapEnd;

                if (beforeLen > 0)
                    newRuns.Add(CloneRunWithText(run, run.Text.Substring(0, beforeLen)));

                var middle = CloneRunWithText(run, run.Text.Substring(beforeLen, selectedLen));
                newRuns.Add(middle);
                selected.Add(middle);

                if (afterLen > 0)
                    newRuns.Add(CloneRunWithText(run, run.Text.Substring(beforeLen + selectedLen, afterLen)));
            }

            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(newRuns);
        }

        return selected;
    }

    private static Run CloneRunWithText(Run source, string text) => new()
    {
        Text = text,
        FontFamily = source.FontFamily,
        FontSizePt = source.FontSizePt,
        Bold = source.Bold,
        Italic = source.Italic,
        BoldSet = source.BoldSet,
        ItalicSet = source.ItalicSet,
        Underline = source.Underline,
        Strikethrough = source.Strikethrough,
        Color = source.Color,
        Hyperlink = source.Hyperlink,
        Field = source.Field,
        TextFill = source.TextFill,
        TextOutline = source.TextOutline,
        TextShadow = source.TextShadow,
        TextReflection = source.TextReflection,
        Math = source.Math,
    };

    private static bool RunFormatEquals(Run a, Run b) =>
        a.FontFamily == b.FontFamily
        && a.FontSizePt == b.FontSizePt
        && a.Bold == b.Bold
        && a.Italic == b.Italic
        && a.BoldSet == b.BoldSet
        && a.ItalicSet == b.ItalicSet
        && a.Underline == b.Underline
        && a.Strikethrough == b.Strikethrough
        && TextBodyModelCloner.ColorsEqual(a.Color, b.Color)
        && a.Hyperlink == b.Hyperlink
        && a.Field == b.Field
        && a.TextFill == b.TextFill
        && a.TextOutline == b.TextOutline
        && a.TextShadow == b.TextShadow
        && a.TextReflection == b.TextReflection
        && a.Math == b.Math;

    /// <summary>Merges adjacent runs within each paragraph that share identical formatting, to avoid run proliferation after a selection split.</summary>
    private static void MergeAdjacentRunsWithSameFormat(TextBody body)
    {
        foreach (var paragraph in body.Paragraphs)
        {
            var merged = new List<Run>();
            foreach (var run in paragraph.Runs)
            {
                if (merged.Count > 0 && RunFormatEquals(merged[^1], run))
                    merged[^1].Text += run.Text;
                else
                    merged.Add(run);
            }

            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(merged);
        }
    }

    private static TableCellEditStartPlan NotReady(
        TableCellEditStartStatus status,
        uint shapeId,
        int row,
        int col) =>
        new(status, shapeId, row, col, null, null, null, null, null);

    private static TableCellTextFormatPlan DisabledFormat(
        TableCellTextFormatStatus status,
        TableCellTextFormatKind kind,
        uint? shapeId = null,
        int? row = null,
        int? col = null) =>
        new(status, shapeId, row, col, kind, null, null);

    private static TableCellTextValueFormatPlan DisabledValueFormat(
        TableCellTextFormatStatus status,
        TableCellTextValueFormatKind kind,
        object? value,
        uint? shapeId = null,
        int? row = null,
        int? col = null) =>
        new(status, shapeId, row, col, kind, value, null);

    private static bool GetRunFormat(Run run, TableCellTextFormatKind kind) => kind switch
    {
        TableCellTextFormatKind.Bold => run.Bold,
        TableCellTextFormatKind.Italic => run.Italic,
        TableCellTextFormatKind.Underline => run.Underline,
        _ => false,
    };

    private static void SetRunFormat(Run run, TableCellTextFormatKind kind, bool value)
    {
        switch (kind)
        {
            case TableCellTextFormatKind.Bold:
                run.Bold = value;
                run.BoldSet = true;
                break;
            case TableCellTextFormatKind.Italic:
                run.Italic = value;
                run.ItalicSet = true;
                break;
            case TableCellTextFormatKind.Underline:
                run.Underline = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static void SetRunValueFormat(Run run, TableCellTextValueFormatKind kind, object? value)
    {
        switch (kind)
        {
            case TableCellTextValueFormatKind.FontFamily:
                run.FontFamily = (string?)value;
                break;
            case TableCellTextValueFormatKind.FontSize:
                run.FontSizePt = (double?)value;
                break;
            case TableCellTextValueFormatKind.Color:
                run.Color = (ThemeAwareColor?)value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static (int Row, int Col, TableCell Cell)? NormalizeCell(
        TableShape table,
        int row,
        int col)
    {
        if (row < 0 || row >= table.Rows.Count)
            return null;
        if (col < 0 || col >= table.ColumnWidthsEmu.Count)
            return null;
        if (col >= table.Rows[row].Cells.Count)
            return null;

        var requestedCell = table.Rows[row].Cells[col];
        if (!requestedCell.HMerge && !requestedCell.VMerge)
            return (row, col, requestedCell);

        for (int r = 0; r < table.Rows.Count; r++)
        {
            var tableRow = table.Rows[r];
            for (int c = 0; c < tableRow.Cells.Count; c++)
            {
                var candidate = tableRow.Cells[c];
                if (candidate.HMerge || candidate.VMerge)
                    continue;

                int colSpan = Math.Max(1, candidate.GridSpan);
                int rowSpan = Math.Max(1, candidate.RowSpan);
                if (r <= row && row < r + rowSpan && c <= col && col < c + colSpan)
                    return (r, c, candidate);
            }
        }

        return null;
    }
}
