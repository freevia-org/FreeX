using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Applies a partial style override to every cell in a range.
/// Only non-null StyleDiff fields are changed; others are preserved.
/// <para>
/// Performance note: for unbounded selections (whole-column or whole-row, where the range extends
/// to <see cref="CellAddress.MaxRow"/> or <see cref="CellAddress.MaxCol"/>) the dense loop over
/// millions of empty cells is clamped to the sheet's used-range bounding box.  Content cells
/// anywhere in the selection and pre-existing style-only entries anywhere in the selection are
/// still fully honoured; only the creation of <em>new</em> style-only entries for empty,
/// never-touched cells is clamped.  Bounded selections (e.g. a format-painter target block) are
/// never clamped — every empty cell in the explicit selection gets a style-only entry.
/// </para>
/// </summary>
public sealed class ApplyStyleCommand : IWorkbookCommand, IEstimatesMemory
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly StyleDiff _diff;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly, StyleOnlySource? OldStyleOnlySource)>? _snapshot;
    private List<(CellAddress Address, IReadOnlyList<CellTextRun> OldRuns)>? _richTextSnapshot;

    private const int BytesPerCell = 200;

    public string Label => "Apply Style";

    /// <inheritdoc/>
    public int EstimatedBytes => (int)Math.Min(_range.CellCount * BytesPerCell, int.MaxValue);

    public ApplyStyleCommand(SheetId sheetId, GridRange range, StyleDiff diff)
    {
        _sheetId = sheetId;
        _range   = range;
        _diff    = diff;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
            return protectedOutcome;
        // Excel keeps the Protection tab's Locked/Hidden checkboxes disabled whenever the sheet is
        // protected, regardless of which protection permissions (including Format Cells) are
        // granted -- the sheet must be unprotected first to change either flag. Enforce that as an
        // always-on check independent of the FormatCells permission check above, so a "Format
        // Cells" grant cannot be used to progressively unlock/hide cells while still protected.
        if (sheet.IsProtected && (_diff.Locked is not null || _diff.Hidden is not null))
            return CommandGuards.RejectSheetProtected();
        if (StyleDiffValidator.Validate(_diff) is { } validationOutcome)
            return validationOutcome;

        _snapshot = [];
        var styleCache = new Dictionary<StyleId, StyleId>();

        // Compute the zone in which we will CREATE new style-only entries for empty cells.
        // This is clamped to the sheet's used range to avoid materialising millions of style-only
        // entries when a whole column or row is selected.  Content cells and pre-existing
        // style-only entries outside the clamp zone are still processed below.
        var styleOnlyCreateZone = StyleOnlyCreateZone(sheet, _range);

        // R92-render-cellstyle-inheritance-5-3: classify THIS command as a whole-row format op
        // (unbounded columns, bounded rows -- e.g. a row-header selection), a whole-column format
        // op (unbounded rows, bounded columns -- e.g. a column-header selection), or neither (a
        // bounded cell-range selection, or a fully-unbounded select-all) so the style-only passes
        // below can enforce Excel's fixed row-beats-column precedence at a row/column intersection
        // instead of the previous "whichever command ran last wins" behavior.
        var commandSource = DetermineStyleOnlySource(_range);

        // --- Pass 1: content cells anywhere in the selection ---
        // Iterate the occupied-cell dictionary (O(cellCount), not O(rangeSize)).
        // A cell that already has real content always carries its own cell-level xf (the highest
        // rung of Excel's cell > row > column precedence chain), so row/column provenance never
        // applies here -- the diff always merges directly onto the cell's existing StyleId.
        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (row < _range.Start.Row || row > _range.End.Row) continue;
            if (col < _range.Start.Col || col > _range.End.Col) continue;

            var addr = new CellAddress(_sheetId, row, col);
            _snapshot.Add((addr, cell.Clone(), null, null));
            cell.StyleId = StyleDiffStyleCache.GetOrRegister(ctx.Workbook, _diff, cell.StyleId, styleCache);

            // Whole-cell font-formatting commands (Bold/Italic/Underline/Strikethrough/Font Name/
            // Font Size/Font Color) must win over stale per-run rich-text overrides for the same
            // property. Matches Excel: applying direct formatting to a whole cell (no partial-text/
            // edit-mode selection) clears per-character overrides for that property so the newly
            // applied uniform value actually renders instead of being masked by old run formatting.
            if (sheet.RichTextRuns.TryGetValue(addr, out var runs) && AffectsRichRunFont(_diff))
            {
                _richTextSnapshot ??= [];
                _richTextSnapshot.Add((addr, runs));
                sheet.RichTextRuns[addr] = ClearOverriddenRunProperties(runs, _diff);
            }
        }

        // --- Pass 2: empty cells within the style-only create zone ---
        // This is the dense loop, but clamped to at most usedRange rows × usedRange cols.
        if (styleOnlyCreateZone.HasValue)
        {
            var zone = styleOnlyCreateZone.Value;
            for (var r = zone.Start.Row; r <= zone.End.Row; r++)
            {
                for (var c = zone.Start.Col; c <= zone.End.Col; c++)
                {
                    // Skip occupied cells — already handled in Pass 1.
                    if (sheet.GetCell(r, c) is not null)
                        continue;

                    var oldStyleOnly = sheet.GetStyleOnly(r, c);
                    var oldSource = sheet.GetStyleOnlySource(r, c);

                    // R92-render-cellstyle-inheritance-5-3: a column-format op must never touch a
                    // cell whose style-only entry is already row-sourced -- the row's format always
                    // wins at that intersection, regardless of which op ran more recently.
                    if (commandSource == StyleOnlySource.Column && oldSource == StyleOnlySource.Row)
                        continue;

                    var addr = new CellAddress(_sheetId, r, c);
                    _snapshot.Add((addr, null, oldStyleOnly, oldSource));

                    // A row-format op overtaking a column-sourced entry REPLACES it outright
                    // (matching Excel: the row's own format is what renders, not a merge of the
                    // two) rather than layering its diff on top of the column-derived style.
                    var baseStyleId = commandSource == StyleOnlySource.Row && oldSource == StyleOnlySource.Column
                        ? StyleId.Default
                        : oldStyleOnly ?? StyleId.Default;

                    var newStyleId = StyleDiffStyleCache.GetOrRegister(
                        ctx.Workbook,
                        _diff,
                        baseStyleId,
                        styleCache);
                    sheet.SetStyleOnly(r, c, newStyleId);
                    if (commandSource.HasValue)
                        sheet.SetStyleOnlySource(r, c, commandSource.Value);
                    else
                        sheet.ClearStyleOnlySource(r, c);
                }
            }
        }

        // --- Pass 3: pre-existing style-only entries OUTSIDE the create zone ---
        // These exist when a cell was previously styled by a prior command.  We must update them
        // so that re-applying Bold on the same column after a prior Bold pass is consistent.
        // Materialise the snapshot before the loop to avoid iterating while mutating _styleOnly.
        var preExistingStyleOnly = sheet.GetStyleOnlyEntries().ToList();
        foreach (var ((row, col), existingStyleId) in preExistingStyleOnly)
        {
            if (row < _range.Start.Row || row > _range.End.Row) continue;
            if (col < _range.Start.Col || col > _range.End.Col) continue;

            // Skip anything already covered by Pass 2 to avoid duplicate snapshot entries.
            if (styleOnlyCreateZone.HasValue)
            {
                var z = styleOnlyCreateZone.Value;
                if (row >= z.Start.Row && row <= z.End.Row &&
                    col >= z.Start.Col && col <= z.End.Col)
                {
                    continue;
                }
            }

            // Skip if the cell is now occupied (Pass 1 handles it).
            if (sheet.GetCell(row, col) is not null)
                continue;

            var existingSource = sheet.GetStyleOnlySource(row, col);

            // R92-render-cellstyle-inheritance-5-3: same row-beats-column precedence as Pass 2.
            if (commandSource == StyleOnlySource.Column && existingSource == StyleOnlySource.Row)
                continue;

            var addr = new CellAddress(_sheetId, row, col);
            _snapshot.Add((addr, null, existingStyleId, existingSource));

            var baseStyleId = commandSource == StyleOnlySource.Row && existingSource == StyleOnlySource.Column
                ? StyleId.Default
                : existingStyleId;

            var newStyleId = StyleDiffStyleCache.GetOrRegister(
                ctx.Workbook,
                _diff,
                baseStyleId,
                styleCache);
            sheet.SetStyleOnly(row, col, newStyleId);
            if (commandSource.HasValue)
                sheet.SetStyleOnlySource(row, col, commandSource.Value);
            else
                sheet.ClearStyleOnlySource(row, col);
        }

        // Report the affected cells (mirroring PropagateCalculatedColumnCommand's use of its own
        // snapshot) so WorkbookSession's undo/redo selection-restore path (ApplySuccessfulHistoryResult
        // / CommandOutcome.AffectedCells contract) knows which sheet and range this style command
        // touched -- without this, undoing a style command applied on a different sheet than the
        // one currently active had nothing to switch back to or restore a selection for.
        return new CommandOutcome(true, AffectedCells: _snapshot.ConvertAll(s => s.Address));
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, oldCell, oldStyleOnly, oldStyleOnlySource) in _snapshot)
        {
            if (oldCell is null)
            {
                if (oldStyleOnly.HasValue)
                {
                    sheet.SetStyleOnly(addr.Row, addr.Col, oldStyleOnly.Value);
                    // R92-render-cellstyle-inheritance-5-3: restore the pre-existing entry's
                    // provenance tag too, so undoing this command doesn't leave a stale Row/Column
                    // tag (or lose one) at this address.
                    if (oldStyleOnlySource.HasValue)
                        sheet.SetStyleOnlySource(addr.Row, addr.Col, oldStyleOnlySource.Value);
                    else
                        sheet.ClearStyleOnlySource(addr.Row, addr.Col);
                }
                else
                {
                    sheet.ClearStyleOnly(addr.Row, addr.Col);
                }
            }
            else
            {
                sheet.SetCell(addr, oldCell.Clone());
            }
        }

        if (_richTextSnapshot is not null)
        {
            foreach (var (addr, oldRuns) in _richTextSnapshot)
                sheet.RichTextRuns[addr] = oldRuns;
        }
    }

    /// <summary>
    /// True when <paramref name="diff"/> sets at least one property that has a corresponding
    /// per-run override on <see cref="CellTextRun"/> (Bold, Italic, Underline, Strikethrough,
    /// FontName, FontSize, or either font-color form).
    /// </summary>
    private static bool AffectsRichRunFont(StyleDiff diff) =>
        diff.Bold is not null
        || diff.Italic is not null
        || diff.Underline is not null
        || diff.Strikethrough is not null
        || diff.FontName is not null
        || diff.FontSize is not null
        || diff.FontColor is not null
        || diff.FontThemeColor is not null;

    /// <summary>
    /// Returns a copy of <paramref name="runs"/> with the per-run override cleared (set to null,
    /// i.e. "inherit from the cell style") for every property that <paramref name="diff"/>
    /// explicitly sets, so the newly applied whole-cell value is not masked by a stale run-level
    /// override.
    /// </summary>
    private static List<CellTextRun> ClearOverriddenRunProperties(IReadOnlyList<CellTextRun> runs, StyleDiff diff)
    {
        var result = new List<CellTextRun>(runs.Count);
        foreach (var run in runs)
        {
            result.Add(run with
            {
                Bold          = diff.Bold          is not null ? null : run.Bold,
                Italic        = diff.Italic        is not null ? null : run.Italic,
                Underline     = diff.Underline     is not null ? null : run.Underline,
                Strikethrough = diff.Strikethrough is not null ? null : run.Strikethrough,
                FontName      = diff.FontName      is not null ? null : run.FontName,
                FontSize      = diff.FontSize      is not null ? null : run.FontSize,
                FontColor     = (diff.FontColor is not null || diff.FontThemeColor is not null) ? null : run.FontColor,
            });
        }
        return result;
    }

    /// <summary>
    /// Classifies <paramref name="range"/> as a whole-row format op, a whole-column format op, or
    /// neither, for the row-beats-column style-only precedence enforced in <see cref="Apply"/>
    /// (R92-render-cellstyle-inheritance-5-3). A whole-row selection (e.g. a row-header click)
    /// spans every column but a bounded set of rows; a whole-column selection (e.g. a
    /// column-header click) spans every row but a bounded set of columns. A bounded cell-range
    /// selection, or a fully-unbounded select-all (both dimensions unbounded), is neither -- both
    /// fall back to the pre-existing plain merge-on-top behavior.
    /// </summary>
    internal static StyleOnlySource? DetermineStyleOnlySource(GridRange range)
    {
        // Must agree with SelectionRangeService.IsWholeColumnSelection/IsWholeRowSelection: a
        // genuine column-header selection spans every row AND starts at row 1, and a genuine
        // row-header selection spans every column AND starts at column 1. A range that merely
        // reaches CellAddress.MaxRow/MaxCol at its End corner while starting mid-sheet (e.g. a
        // Ctrl+Shift+Down selection from B5 to the bottom of the sheet) is NOT a whole-column
        // selection and must not be tagged with row/column provenance -- only StyleOnlyCreateZone
        // treats it specially, for its own perf-clamp reasons, not for row-beats-column precedence.
        var isWholeColumn = SelectionRangeService.IsWholeColumnSelection(range);
        var isWholeRow = SelectionRangeService.IsWholeRowSelection(range);

        if (isWholeColumn && !isWholeRow)
            return StyleOnlySource.Column; // every row, bounded columns -- a column-header selection
        if (isWholeRow && !isWholeColumn)
            return StyleOnlySource.Row; // every column, bounded rows -- a row-header selection
        return null;
    }

    /// <summary>
    /// Returns the zone within <paramref name="range"/> where new style-only entries may be
    /// created for empty cells.  For bounded selections (user selected a specific cell block) the
    /// full range is returned unchanged — every empty cell in the explicit selection gets a
    /// style-only entry, which is the expected behaviour.
    /// <para>
    /// The clamp only activates for <em>unbounded</em> selections, i.e. whole-column
    /// (<see cref="CellAddress.MaxRow"/>) or whole-row (<see cref="CellAddress.MaxCol"/>)
    /// selections, where iterating the full range would materialise millions of style-only entries.
    /// In that case the zone is intersected with the sheet's used-range bounding box.
    /// </para>
    /// Returns null when the intersection is empty (unbounded selection on an empty sheet, or
    /// unbounded selection that does not overlap the used range).
    /// </summary>
    public static GridRange? StyleOnlyCreateZone(Sheet sheet, GridRange range)
    {
        var isUnboundedRows = range.End.Row >= CellAddress.MaxRow;
        var isUnboundedCols = range.End.Col >= CellAddress.MaxCol;

        // Bounded selection: the caller explicitly chose every cell in the range.
        // Return the full range so all empty cells get a style-only entry.
        if (!isUnboundedRows && !isUnboundedCols)
            return range;

        var usedRange = sheet.GetUsedRange();
        if (usedRange is null)
        {
            // No content on sheet.  Allow style-only entries only within a bounded zone so that
            // clicking a column header on an empty sheet does not materialise 1M entries.
            // We allow up to the selection bounding box but cap at a sensible default viewport.
            const uint EmptySheetMaxRow = 1_000;
            const uint EmptySheetMaxCol = CellAddress.MaxCol;
            var cappedEnd = new CellAddress(
                range.Start.Sheet,
                Math.Min(range.End.Row, EmptySheetMaxRow),
                Math.Min(range.End.Col, EmptySheetMaxCol));
            if (cappedEnd.Row < range.Start.Row || cappedEnd.Col < range.Start.Col)
                return null;
            return new GridRange(range.Start, cappedEnd);
        }

        // Unbounded selection: clamp only the UNBOUNDED dimension(s).
        // A whole-column selection (isUnboundedRows) keeps its selected columns and clamps rows to
        // the used range.  A whole-row selection (isUnboundedCols) keeps its selected rows and
        // clamps columns to the used range.  Clamping the BOUNDED dimension would silently
        // produce an empty intersection when the selected column/row has no data of its own but the
        // rest of the sheet does — e.g. formatting column A when data lives only in B:D.
        //
        // The clamped start must never move ABOVE/LEFT-OF the selection's own Start: a range like
        // B5:B1048576 (Start.Row=5, reaches MaxRow -- e.g. a Ctrl+Shift+Down selection, not a true
        // column-header click) is still "unbounded" for perf-clamp purposes, but the zone must stay
        // within row 5.. -- taking usedRange.Start.Row verbatim would pull the zone up into rows the
        // user never selected (e.g. a header row at row 1) and silently style them. Math.Max keeps
        // the true whole-column case (range.Start.Row == 1) unaffected, since usedRange.Start.Row is
        // always >= 1.
        var startRow = isUnboundedRows
            ? Math.Max(usedRange.Value.Start.Row, range.Start.Row)
            : range.Start.Row;
        var endRow = isUnboundedRows
            ? usedRange.Value.End.Row
            : range.End.Row;
        var startCol = isUnboundedCols
            ? Math.Max(usedRange.Value.Start.Col, range.Start.Col)
            : range.Start.Col;
        var endCol = isUnboundedCols
            ? usedRange.Value.End.Col
            : range.End.Col;

        if (startRow > endRow || startCol > endCol)
            return null; // used-range row/col dimension is empty (shouldn't happen with a valid used range)

        return new GridRange(
            new CellAddress(range.Start.Sheet, startRow, startCol),
            new CellAddress(range.Start.Sheet, endRow, endCol));
    }
}

internal static class StyleDiffValidator
{
    public static CommandOutcome? Validate(StyleDiff diff)
    {
        if (diff.HAlign is { } hAlign && !Enum.IsDefined(hAlign))
            return new CommandOutcome(false, "Horizontal alignment is not supported.");
        if (diff.VAlign is { } vAlign && !Enum.IsDefined(vAlign))
            return new CommandOutcome(false, "Vertical alignment is not supported.");
        if (diff.FontSize is { } fontSize && !IsSupportedFontSize(fontSize))
            return new CommandOutcome(false, "Font size is not supported.");
        if (diff.TextRotation is { } rotation && !IsSupportedTextRotation(rotation))
            return new CommandOutcome(false, "Text rotation is not supported.");
        if (diff.FillPatternStyle is { } fillPatternStyle && !Enum.IsDefined(fillPatternStyle))
            return new CommandOutcome(false, "Fill pattern style is not supported.");
        if (HasInvalidBorderStyle(diff.BorderTop) ||
            HasInvalidBorderStyle(diff.BorderRight) ||
            HasInvalidBorderStyle(diff.BorderBottom) ||
            HasInvalidBorderStyle(diff.BorderLeft))
            return new CommandOutcome(false, "Border style is not supported.");

        return null;
    }

    private static bool IsSupportedTextRotation(int rotation) =>
        rotation == 255 || rotation is >= -90 and <= 90;

    private static bool IsSupportedFontSize(double fontSize) =>
        double.IsFinite(fontSize) && fontSize is >= 1 and <= 409;

    private static bool HasInvalidBorderStyle(CellBorder? border) =>
        border is { } value && !Enum.IsDefined(value.Style);
}

internal static class StyleDiffStyleCache
{
    public static StyleId GetOrRegister(
        Workbook workbook,
        StyleDiff diff,
        StyleId baseStyleId,
        Dictionary<StyleId, StyleId> cache)
    {
        if (cache.TryGetValue(baseStyleId, out var cachedStyleId))
            return cachedStyleId;

        var newStyle = diff.ApplyTo(workbook.GetStyle(baseStyleId));
        var newStyleId = workbook.RegisterStyle(newStyle);
        cache[baseStyleId] = newStyleId;
        return newStyleId;
    }
}
