using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Materializes each loaded structured table's built-in style (e.g. <c>TableStyleMedium2</c> with
/// row stripes) onto the cells already present for it, WITHOUT changing values.  Excel applies table
/// styles dynamically (header fill + alternating row banding + header font) and does not bake them
/// into per-cell styles, so a table read from xlsx arrives with correct values but no formatting;
/// this paints that formatting on the loaded cells so a table looks like it does in Excel.  It is the
/// table analog of <see cref="PivotTableRefreshService.ApplyLoadedPivotStyles"/> and is rebased out of
/// the patch-save snapshot by the open service so saving does not write the materialized fills back
/// (Excel keeps tables dynamic).  Best-effort per table — a malformed table is skipped rather than
/// failing the whole open.
/// </summary>
public static class StructuredTableStyleService
{
    /// <summary>
    /// Paints every loaded table's banding onto its cells.  Returns true if any table was styled.
    /// </summary>
    public static bool ApplyLoadedTableStyles(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var styledAny = false;
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var table in sheet.StructuredTables)
            {
                try
                {
                    if (ApplyTableStyle(workbook, sheet, table))
                        styledAny = true;
                }
                catch
                {
                    // A single malformed table must not break opening the workbook.
                }
            }
        }

        return styledAny;
    }

    /// <summary>
    /// Re-flows a table's alternating banding purely from each row/column's CURRENT position
    /// within <paramref name="table"/>.Range, overwriting any previously-baked stripe fill.
    /// Call this after a mutation that can change which physical row/column a table's data
    /// occupies — inserting/deleting rows inside the table, sorting it, or filtering it — since
    /// real Excel's table banding is dynamic (purely positional) and continuously re-flows after
    /// any such change, whereas <see cref="ApplyLoadedTableStyles"/>'s one-time load-time bake
    /// never runs again on its own. Unlike the load-time materializer, this always rewrites the
    /// body fill (forceReband) because after such a mutation any fill already on a body cell is
    /// necessarily this service's own stale banding from before the mutation, never a user
    /// customization (a genuine user fill would already have been preserved by whichever earlier
    /// call painted it, and this call cannot tell the two apart once it starts).
    /// </summary>
    public static bool RebandTable(Workbook workbook, Sheet sheet, StructuredTableModel table) =>
        ApplyTableStyle(workbook, sheet, table, forceReband: true);

    private static bool ApplyTableStyle(Workbook workbook, Sheet sheet, StructuredTableModel table, bool forceReband = false)
    {
        var range = table.Range;
        if (range.Start.Sheet != sheet.Id ||
            range.End.Row < range.Start.Row ||
            range.End.Col < range.Start.Col)
        {
            return false;
        }

        // R46-io-table-style-bands-2-1: when the table's style name resolves to a registered custom
        // table style (one defined in the workbook's own styles.xml <tableStyles>), the load pipeline
        // already painted its exact header/totals/stripe/border formatting onto these cells via
        // XlsxStructuredTableModelMapper.MaterializeStyle (called earlier, per-sheet, during the load
        // loop). StructuredTableStyleBandingResolver only recognizes Excel's built-in
        // TableStyleLight/Medium/Dark name families, so a custom name would otherwise fall through to
        // DefaultLightBanding() and this generic materializer would stomp the already-correct custom
        // header/totals formatting with Excel's generic gray/black default. Skip the generic banding
        // entirely for a table using a recognized custom style — nothing more to paint here.
        if (HasCustomTableStyle(workbook, table.StyleName))
            return false;

        var banding = StructuredTableStyleBandingResolver.Resolve(table.StyleName, workbook.Theme);

        var hasHeaderRow = table.HeaderRowCount is null or > 0;
        var hasTotalsRow = table.TotalsRowShown && range.End.Row > range.Start.Row;
        var dataStartRow = range.Start.Row + (hasHeaderRow ? 1u : 0u);
        var dataEndRow = range.End.Row - (hasTotalsRow ? 1u : 0u);

        var bodyBorder = CreateTableBodyBorder(banding);
        var headerFill = workbook.RegisterStyle(BuildHeaderStyle(banding, bodyBorder));
        var oddFill = workbook.RegisterStyle(BuildBodyStyle(banding.OddRowFill, bodyBorder));
        var evenFill = workbook.RegisterStyle(BuildBodyStyle(banding.EvenRowFill, bodyBorder));
        var bodyFill = workbook.RegisterStyle(BuildBodyStyle(banding.EffectiveBodyFill, bodyBorder));
        var totalsRowFill = workbook.RegisterStyle(BuildTotalsRowStyle(banding, bodyBorder));

        var styledAny = false;

        // ── Header row ───────────────────────────────────────────────────────
        if (hasHeaderRow)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                styledAny |= MergeStyleOntoCell(workbook, sheet, range.Start.Row, col, headerFill, isHeaderOrTotals: true, banding: banding);
        }

        // ── Data body rows (row banding, or body fill when row stripes off) ─
        if (dataStartRow <= dataEndRow)
        {
            // R46-io-table-style-bands-2-2: capture, BEFORE either banding pass below writes
            // anything, which body cells already carry an explicit fill (a user-set fill loaded
            // straight from the source file, or one materialized by a custom table style). The
            // column-stripe pass still needs to win over the row-banding pass's own fill (written
            // immediately below, in this same call) via forceFill, but it must not also clobber a
            // fill that was already on the cell before this table was styled at all — that fill
            // always wins over dynamic banding, in both Excel and this method's own body-cell rule.
            var originalBodyFill = table.ShowColumnStripes
                ? CaptureBodyFillState(workbook, sheet, dataStartRow, dataEndRow, range.Start.Col, range.End.Col)
                : null;

            // R91-meta-3: Excel's dynamic row banding alternates across VISIBLE rows only — a row
            // hidden by AutoFilter (or a manual/group hide) does not consume a stripe slot, so the
            // rows that remain visible after a filter re-flow their parity around the gap instead of
            // keeping whatever parity their unchanged PHYSICAL row offset would give them. Track a
            // separate running counter that only advances for rows that are currently visible; a
            // hidden row is painted with its still-pending parity (irrelevant, since it isn't
            // rendered) without consuming a slot, so the next visible row lands on the correct stripe.
            // When nothing is hidden this counter is identical to the old physical-offset formula, so
            // Insert/Sort's already-correct "purely positional" behavior (R90) is unaffected.
            var visibleRowIndex = 0u;
            for (var row = dataStartRow; row <= dataEndRow; row++)
            {
                // Match Excel's (and the table-creation command's) banding parity: the first
                // (visible) data row is the "even" (typically unfilled) stripe, the next is the
                // "odd" (tinted) stripe.
                var rowStyle = table.ShowRowStripes
                    ? (visibleRowIndex % 2 == 0 ? evenFill : oddFill)
                    : bodyFill;
                for (var col = range.Start.Col; col <= range.End.Col; col++)
                    styledAny |= MergeStyleOntoCell(workbook, sheet, row, col, rowStyle, isHeaderOrTotals: false, banding: banding, forceFill: forceReband);
                if (!sheet.IsRowEffectivelyHidden(row))
                    visibleRowIndex++;
            }

            // ── Column stripes (overrides row fill per column, mirrors StructuredTableCommand) ──
            // When ShowColumnStripes is true Excel draws vertical bands instead of (or layered over)
            // row bands.  Mirror the apply-command: iterate columns and paint even/odd column fills
            // onto the data body range.  forceFill bypasses the keepExistingFill guard only for a
            // cell that had NO explicit fill before this call started (i.e. the only fill it could be
            // preserving is the one the row-banding pass above just wrote); a cell that already had an
            // explicit fill before this table was styled keeps it, matching Excel and body-cell parity.
            if (table.ShowColumnStripes)
            {
                for (var col = range.Start.Col; col <= range.End.Col; col++)
                {
                    var colOffset = col - range.Start.Col;
                    var colFill = colOffset % 2 == 0 ? evenFill : oddFill;
                    for (var row = dataStartRow; row <= dataEndRow; row++)
                    {
                        var hadExplicitFillBeforeStyling = originalBodyFill!.Contains((row, col));
                        styledAny |= MergeStyleOntoCell(workbook, sheet, row, col, colFill, isHeaderOrTotals: false, banding: banding, forceFill: forceReband || !hadExplicitFillBeforeStyling);
                    }
                }
            }
        }

        // ── Totals row ───────────────────────────────────────────────────────
        // Excel's totals row has its own look (body-level fill, not the header band) with a distinct
        // top border separating it from the data body.
        if (hasTotalsRow)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                styledAny |= MergeStyleOntoCell(workbook, sheet, range.End.Row, col, totalsRowFill, isHeaderOrTotals: true, banding: banding);
        }

        // ── First column emphasis (bold, mirrors StructuredTableCommand) ─────
        if (table.ShowFirstColumn)
        {
            var boldStyle = workbook.RegisterStyle(BuildBoldStyle());
            for (var row = range.Start.Row; row <= range.End.Row; row++)
                styledAny |= MergeBoldOntoCell(workbook, sheet, row, range.Start.Col, boldStyle);
        }

        // ── Last column emphasis (bold, mirrors StructuredTableCommand) ──────
        if (table.ShowLastColumn && range.End.Col != range.Start.Col)
        {
            var boldStyle = workbook.RegisterStyle(BuildBoldStyle());
            for (var row = range.Start.Row; row <= range.End.Row; row++)
                styledAny |= MergeBoldOntoCell(workbook, sheet, row, range.End.Col, boldStyle);
        }

        return styledAny;
    }

    /// <summary>
    /// True when <paramref name="styleName"/> matches a workbook-registered custom table style
    /// (an entry in <see cref="Workbook.StructuredTableStyles"/> with <c>AppliesToTables</c> set) —
    /// i.e. one XlsxStructuredTableModelMapper.MaterializeStyle already painted onto this table's
    /// cells at load time, using the exact dxf-defined formatting from the workbook's own styles.xml.
    /// Mirrors that mapper's own lookup (case-insensitive name match).
    /// </summary>
    private static bool HasCustomTableStyle(Workbook workbook, string? styleName)
    {
        if (string.IsNullOrWhiteSpace(styleName))
            return false;

        foreach (var candidate in workbook.StructuredTableStyles)
        {
            if (candidate.AppliesToTables && string.Equals(candidate.Name, styleName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Snapshots which cells in [<paramref name="startRow"/>, <paramref name="endRow"/>] ×
    /// [<paramref name="startCol"/>, <paramref name="endCol"/>] already carry an explicit fill,
    /// captured before either banding pass in <see cref="ApplyTableStyle"/> writes anything — used so
    /// the column-stripe pass can tell "fill the row-banding pass just wrote" (safe to override) apart
    /// from "fill that was already there" (must be preserved, per R46-io-table-style-bands-2-2).
    /// </summary>
    private static HashSet<(uint Row, uint Col)> CaptureBodyFillState(
        Workbook workbook, Sheet sheet, uint startRow, uint endRow, uint startCol, uint endCol)
    {
        var present = new HashSet<(uint Row, uint Col)>();
        for (var row = startRow; row <= endRow; row++)
        {
            for (var col = startCol; col <= endCol; col++)
            {
                if (sheet.GetCell(row, col) is { } cell && workbook.GetStyle(cell.StyleId).FillColor is not null)
                    present.Add((row, col));
            }
        }

        return present;
    }

    /// <summary>
    /// Creates the thin border used on interior table cells when the style family provides a border
    /// color.  Returns <see cref="CellBorder.None"/> (no border) when the style has no border color
    /// (e.g. Light family styles where Excel draws no interior borders).
    /// </summary>
    private static CellBorder CreateTableBodyBorder(StructuredTableStyleBanding banding) =>
        banding.Border is { } color
            ? new CellBorder(BorderStyle.Thin, color)
            : default;

    /// <summary>
    /// Header row: bold + header fill + header font color + bottom border (the separator between
    /// header and data body that Excel draws for styled tables).
    /// </summary>
    private static CellStyle BuildHeaderStyle(StructuredTableStyleBanding banding, CellBorder bodyBorder) => new()
    {
        Bold = true,
        FontColor = banding.HeaderFontColor,
        FillColor = banding.HeaderFill,
        // Header gets a bottom border (separator line) matching the body border color.
        BorderBottom = bodyBorder
    };

    private static CellStyle BuildBodyStyle(CellColor fill, CellBorder bodyBorder) => new()
    {
        FillColor = fill,
        BorderTop    = bodyBorder,
        BorderRight  = bodyBorder,
        BorderBottom = bodyBorder,
        BorderLeft   = bodyBorder
    };

    /// <summary>
    /// Totals-row style: bold + the effective body fill (not the header fill) + a top border that
    /// separates the totals row from the data body, matching Excel's distinct separator line.
    /// </summary>
    private static CellStyle BuildTotalsRowStyle(StructuredTableStyleBanding banding, CellBorder bodyBorder) => new()
    {
        Bold = true,
        FillColor = banding.EffectiveBodyFill,
        // Totals row gets a top border (separator line above totals, distinct from the interior grid).
        BorderTop = bodyBorder
    };

    /// <summary>Marker style used to apply first/last column bold emphasis.</summary>
    private static CellStyle BuildBoldStyle() => new() { Bold = true };

    /// <summary>
    /// Merges the visual fill/font/border from <paramref name="visualStyleId"/> onto the cell at
    /// (<paramref name="row"/>, <paramref name="col"/>) while preserving the cell's own number format,
    /// alignment, and any explicit fill the user already set.  Creates a blank cell when one is absent
    /// so banding paints across empty body cells (as Excel renders them).  Returns true when the cell's
    /// style actually changed.
    /// </summary>
    /// <param name="forceFill">
    /// When <see langword="true"/> the fill is always written, overriding any previously-set fill
    /// (including one written by an earlier pass in this method, e.g. the row-banding pass before a
    /// column-stripe pass).  Use for column-stripe overrides that must win over row fill.
    /// </param>
    private static bool MergeStyleOntoCell(
        Workbook workbook,
        Sheet sheet,
        uint row,
        uint col,
        StyleId visualStyleId,
        bool isHeaderOrTotals,
        StructuredTableStyleBanding banding,
        bool forceFill = false)
    {
        // R90-reband regression fix: do NOT eagerly materialize a blank Cell here just to hold
        // whatever style this pass computes -- a genuinely absent cell (no content, never styled)
        // must only sprout into existence when the computed style is an actual VISIBLE change from
        // a truly blank cell's rendering (a real fill color, a border, or bold), e.g. a striped
        // table's colored band or a Medium-family style's interior border. A no-op pass -- most
        // commonly the body-fill branch when ShowRowStripes is off and the style has no border,
        // which paints nothing but an (explicit, but visually indistinguishable-from-blank) white
        // fill -- must leave an untouched column alone, exactly like Excel never creates cell
        // content from a cosmetic restripe. This is checked separately from the existing/merged
        // content-equality check below (via HasVisibleTableFormatting), because
        // BuildBodyStyle/EffectiveBodyFill always writes an *explicit* CellColor.White rather than a
        // null "no fill" -- which would never structurally equal a truly blank cell's default style
        // even though it renders identically.
        var cell = sheet.GetCell(row, col);
        var existing = cell is not null ? workbook.GetStyle(cell.StyleId) : CellStyle.Default;
        var visual = workbook.GetStyle(visualStyleId);

        // Do not overwrite a fill the user (or the source file) explicitly set on a cell; Excel's
        // dynamic banding/header style yields to an explicit cell fill for header and totals cells
        // exactly the same as it does for body cells — direct formatting always wins over a computed
        // table style, per Excel's own precedence (mirrors PivotTableRefreshService's preserveExisting
        // VisualStyles handling of a pivot header's existing fill).
        // forceFill bypasses this guard — used by the column-stripe pass so it wins over the row-fill pass.
        var keepExistingFill = !forceFill && existing.FillColor is not null;

        var merged = existing.Clone();
        if (!keepExistingFill)
        {
            merged.FillColor = visual.FillColor;
            merged.FillThemeColor = null;
        }

        if (isHeaderOrTotals)
        {
            // Same precedence for the header/totals font: a cell whose bold/font-color was never
            // explicitly set carries the model's own default (Bold=false, FontColor=CellColor.Black,
            // FontThemeColor=null — see CellStyle), so any deviation from that default means the user
            // (or the source file) set it directly, and direct formatting wins over the table-style
            // banding font just like it does for fill.
            if (!existing.Bold)
                merged.Bold = visual.Bold;

            var keepExistingFontColor = existing.FontColor != CellColor.Black || existing.FontThemeColor is not null;
            if (!keepExistingFontColor)
            {
                merged.FontColor = visual.FontColor;
                merged.FontThemeColor = null;
            }
        }

        // Apply table borders when the style provides them (banding.Border is not null).
        // Only write borders that the visual style has; preserve any user-set borders on the other sides.
        // For header: only the bottom border (separator line).
        // For totals: only the top border (separator line).
        // For body: all four sides (interior grid).
        if (banding.Border is not null)
        {
            if (visual.BorderBottom.Style != BorderStyle.None)
                merged.BorderBottom = visual.BorderBottom;
            if (visual.BorderTop.Style != BorderStyle.None)
                merged.BorderTop = visual.BorderTop;
            if (visual.BorderLeft.Style != BorderStyle.None)
                merged.BorderLeft = visual.BorderLeft;
            if (visual.BorderRight.Style != BorderStyle.None)
                merged.BorderRight = visual.BorderRight;
        }

        if (merged.Equals(existing))
            return false;

        if (cell is null)
        {
            if (!HasVisibleTableFormatting(merged))
                return false;

            cell = new Cell { Value = BlankValue.Instance };
            sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
        }

        cell.StyleId = workbook.RegisterStyle(merged);
        return true;
    }

    /// <summary>
    /// True when <paramref name="style"/> would render as visibly different from a genuinely blank,
    /// never-styled cell -- a non-white fill (explicit or theme-backed), a border on any side, or
    /// bold text. Used to decide whether a banding pass is allowed to materialize a brand-new blank
    /// <see cref="Cell"/> purely to hold this style: an all-white, borderless, non-bold style (e.g.
    /// the default body-fill pass when a table has no row/column stripes and no style border) is
    /// indistinguishable from no styling at all, so it must not sprout a cell where none existed.
    /// </summary>
    private static bool HasVisibleTableFormatting(CellStyle style) =>
        (style.FillColor is { } fill && fill != CellColor.White) ||
        style.FillThemeColor is not null ||
        style.Bold ||
        style.BorderTop.Style != BorderStyle.None ||
        style.BorderRight.Style != BorderStyle.None ||
        style.BorderBottom.Style != BorderStyle.None ||
        style.BorderLeft.Style != BorderStyle.None;

    /// <summary>
    /// Applies bold emphasis onto a cell without touching its fill, font color, or other attributes.
    /// Used for first/last column highlighting which only adds bold (mirrors the StyleDiff(Bold:true)
    /// path in <c>StructuredTableCommand.BuildStyleCommands</c>).
    /// </summary>
    private static bool MergeBoldOntoCell(
        Workbook workbook,
        Sheet sheet,
        uint row,
        uint col,
        StyleId boldStyleId)
    {
        var cell = sheet.GetCell(row, col);
        if (cell is null)
        {
            cell = new Cell { Value = BlankValue.Instance };
            sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
        }

        var existing = workbook.GetStyle(cell.StyleId);
        if (existing.Bold == true)
            return false;  // already bold — nothing to do

        var merged = existing.Clone();
        merged.Bold = true;

        if (merged.Equals(existing))
            return false;

        cell.StyleId = workbook.RegisterStyle(merged);
        return true;
    }
}
