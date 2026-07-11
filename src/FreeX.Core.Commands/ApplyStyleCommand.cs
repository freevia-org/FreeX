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
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;
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
        if (StyleDiffValidator.Validate(_diff) is { } validationOutcome)
            return validationOutcome;

        _snapshot = [];
        var styleCache = new Dictionary<StyleId, StyleId>();

        // Compute the zone in which we will CREATE new style-only entries for empty cells.
        // This is clamped to the sheet's used range to avoid materialising millions of style-only
        // entries when a whole column or row is selected.  Content cells and pre-existing
        // style-only entries outside the clamp zone are still processed below.
        var styleOnlyCreateZone = StyleOnlyCreateZone(sheet, _range);

        // --- Pass 1: content cells anywhere in the selection ---
        // Iterate the occupied-cell dictionary (O(cellCount), not O(rangeSize)).
        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (row < _range.Start.Row || row > _range.End.Row) continue;
            if (col < _range.Start.Col || col > _range.End.Col) continue;

            var addr = new CellAddress(_sheetId, row, col);
            _snapshot.Add((addr, cell.Clone(), null));
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

                    var addr = new CellAddress(_sheetId, r, c);
                    var oldStyleOnly = sheet.GetStyleOnly(r, c);
                    _snapshot.Add((addr, null, oldStyleOnly));

                    var newStyleId = StyleDiffStyleCache.GetOrRegister(
                        ctx.Workbook,
                        _diff,
                        oldStyleOnly ?? StyleId.Default,
                        styleCache);
                    sheet.SetStyleOnly(r, c, newStyleId);
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

            var addr = new CellAddress(_sheetId, row, col);
            _snapshot.Add((addr, null, existingStyleId));

            var newStyleId = StyleDiffStyleCache.GetOrRegister(
                ctx.Workbook,
                _diff,
                existingStyleId,
                styleCache);
            sheet.SetStyleOnly(row, col, newStyleId);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, oldCell, oldStyleOnly) in _snapshot)
        {
            if (oldCell is null)
            {
                if (oldStyleOnly.HasValue)
                    sheet.SetStyleOnly(addr.Row, addr.Col, oldStyleOnly.Value);
                else
                    sheet.ClearStyleOnly(addr.Row, addr.Col);
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
        var startRow = isUnboundedRows
            ? usedRange.Value.Start.Row
            : range.Start.Row;
        var endRow = isUnboundedRows
            ? usedRange.Value.End.Row
            : range.End.Row;
        var startCol = isUnboundedCols
            ? usedRange.Value.Start.Col
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
