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

    private static bool ApplyTableStyle(Workbook workbook, Sheet sheet, StructuredTableModel table)
    {
        var range = table.Range;
        if (range.Start.Sheet != sheet.Id ||
            range.End.Row < range.Start.Row ||
            range.End.Col < range.Start.Col)
        {
            return false;
        }

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
            for (var row = dataStartRow; row <= dataEndRow; row++)
            {
                // Match Excel's (and the table-creation command's) banding parity: the first data row
                // is the "even" (typically unfilled) stripe, the next is the "odd" (tinted) stripe.
                var rowOffset = row - dataStartRow;
                var rowStyle = table.ShowRowStripes
                    ? (rowOffset % 2 == 0 ? evenFill : oddFill)
                    : bodyFill;
                for (var col = range.Start.Col; col <= range.End.Col; col++)
                    styledAny |= MergeStyleOntoCell(workbook, sheet, row, col, rowStyle, isHeaderOrTotals: false, banding: banding);
            }

            // ── Column stripes (overrides row fill per column, mirrors StructuredTableCommand) ──
            // When ShowColumnStripes is true Excel draws vertical bands instead of (or layered over)
            // row bands.  Mirror the apply-command: iterate columns and paint even/odd column fills
            // onto the data body range.  forceFill=true so column banding wins over any row fill
            // already written by the row-banding pass above (the row-fill pass may have set a non-null
            // white fill, which the keepExistingFill guard would otherwise preserve).
            if (table.ShowColumnStripes)
            {
                for (var col = range.Start.Col; col <= range.End.Col; col++)
                {
                    var colOffset = col - range.Start.Col;
                    var colFill = colOffset % 2 == 0 ? evenFill : oddFill;
                    for (var row = dataStartRow; row <= dataEndRow; row++)
                        styledAny |= MergeStyleOntoCell(workbook, sheet, row, col, colFill, isHeaderOrTotals: false, banding: banding, forceFill: true);
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
        var cell = sheet.GetCell(row, col);
        if (cell is null)
        {
            cell = new Cell { Value = BlankValue.Instance };
            sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
        }

        var existing = workbook.GetStyle(cell.StyleId);
        var visual = workbook.GetStyle(visualStyleId);

        // Do not overwrite a fill the user (or the source file) explicitly set on a body cell; Excel's
        // dynamic banding yields to an explicit cell fill.  Header/totals rows always take the style
        // fill so they read as a styled band like in Excel.
        // forceFill bypasses this guard — used by the column-stripe pass so it wins over the row-fill pass.
        var keepExistingFill = !isHeaderOrTotals && !forceFill && existing.FillColor is not null;

        var merged = existing.Clone();
        if (!keepExistingFill)
        {
            merged.FillColor = visual.FillColor;
            merged.FillThemeColor = null;
        }

        if (isHeaderOrTotals)
        {
            merged.Bold = visual.Bold;
            merged.FontColor = visual.FontColor;
            merged.FontThemeColor = null;
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

        cell.StyleId = workbook.RegisterStyle(merged);
        return true;
    }

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
