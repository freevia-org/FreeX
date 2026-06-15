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

        var headerFill = workbook.RegisterStyle(BuildHeaderStyle(banding));
        var oddFill = workbook.RegisterStyle(BuildBodyStyle(banding.OddRowFill));
        var evenFill = workbook.RegisterStyle(BuildBodyStyle(banding.EvenRowFill));
        var bodyFill = workbook.RegisterStyle(BuildBodyStyle(banding.EffectiveBodyFill));

        var styledAny = false;

        if (hasHeaderRow)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                styledAny |= MergeStyleOntoCell(workbook, sheet, range.Start.Row, col, headerFill, isHeaderOrTotals: true);
        }

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
                    styledAny |= MergeStyleOntoCell(workbook, sheet, row, col, rowStyle, isHeaderOrTotals: false);
            }
        }

        if (hasTotalsRow)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                styledAny |= MergeStyleOntoCell(workbook, sheet, range.End.Row, col, headerFill, isHeaderOrTotals: true);
        }

        return styledAny;
    }

    private static CellStyle BuildHeaderStyle(StructuredTableStyleBanding banding) => new()
    {
        Bold = true,
        FontColor = banding.HeaderFontColor,
        FillColor = banding.HeaderFill
    };

    private static CellStyle BuildBodyStyle(CellColor fill) => new()
    {
        FillColor = fill
    };

    /// <summary>
    /// Merges the visual fill/font from <paramref name="visualStyleId"/> onto the cell at
    /// (<paramref name="row"/>, <paramref name="col"/>) while preserving the cell's own number format,
    /// borders, alignment, and any explicit fill the user already set.  Creates a blank cell when one
    /// is absent so banding paints across empty body cells (as Excel renders them).  Returns true when
    /// the cell's style actually changed.
    /// </summary>
    private static bool MergeStyleOntoCell(
        Workbook workbook,
        Sheet sheet,
        uint row,
        uint col,
        StyleId visualStyleId,
        bool isHeaderOrTotals)
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
        var keepExistingFill = !isHeaderOrTotals && existing.FillColor is not null;

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

        if (merged.Equals(existing))
            return false;

        cell.StyleId = workbook.RegisterStyle(merged);
        return true;
    }
}
