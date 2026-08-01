namespace FreeP.Core.Model;

/// <summary>
/// Vertical alignment anchor for a table cell's text body.
/// </summary>
public enum TableCellAnchor
{
    Top = 0,
    Middle = 1,
    Bottom = 2
}

/// <summary>
/// How an externally-authored row height is constrained. A null value means the
/// existing table-row behavior applies; rich RTF uses the two explicit values.
/// </summary>
public enum TableRowHeightRule
{
    AtLeast = 0,
    Exact = 1,
}

/// <summary>
/// Horizontal placement of an externally-authored table row. A null value keeps the
/// existing left-aligned behavior.
/// </summary>
public enum TableRowHorizontalAlignment
{
    Left = 0,
    Center = 1,
    Right = 2,
}

/// <summary>One of the four editable sides of a table cell border.</summary>
public enum TableCellBorderSide
{
    Left = 0,
    Right = 1,
    Top = 2,
    Bottom = 3
}

/// <summary>One of the four editable cell inset sides.</summary>
public enum TableCellInsetSide
{
    All = 4,
    Left = 0,
    Right = 1,
    Top = 2,
    Bottom = 3,
}

/// <summary>
/// Per-cell borders for a table cell. Each side may be null (use table-style default) or a
/// concrete outline (None = no border, Visible = drawn border).
/// </summary>
public sealed class TableCellBorders
{
    public ShapeOutline? Left   { get; set; }
    public ShapeOutline? Right  { get; set; }
    public ShapeOutline? Top    { get; set; }
    public ShapeOutline? Bottom { get; set; }
}

/// <summary>
/// A single cell in a table row.
/// </summary>
public sealed class TableCell
{
    /// <summary>Text content of the cell. Null if empty.</summary>
    public TextBody? TextBody { get; set; }

    /// <summary>Explicit cell fill. Null = use effective fill from table style + flags.</summary>
    public ShapeFill? Fill { get; set; }

    /// <summary>Explicit per-side cell borders. Null = use table style defaults.</summary>
    public TableCellBorders? Borders { get; set; }

    /// <summary>Number of columns this cell spans (1 = normal, &gt;1 = merged horizontally).</summary>
    public int GridSpan { get; set; } = 1;

    /// <summary>Number of rows this cell spans (1 = normal, &gt;1 = merged vertically).</summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>True if this cell is a continuation of a horizontal merge (rendered as empty).</summary>
    public bool HMerge { get; set; }

    /// <summary>True if this cell is a continuation of a vertical merge (rendered as empty).</summary>
    public bool VMerge { get; set; }

    // ── Cell insets (override table-style defaults) ──────────────────────────────

    /// <summary>Left inset in points. Null = use default (~7pt).</summary>
    public double? InsetLeftPt   { get; set; }
    /// <summary>Right inset in points.</summary>
    public double? InsetRightPt  { get; set; }
    /// <summary>Top inset in points.</summary>
    public double? InsetTopPt    { get; set; }
    /// <summary>Bottom inset in points.</summary>
    public double? InsetBottomPt { get; set; }

    /// <summary>Vertical text anchor within the cell. Null = inherit from table.</summary>
    public TableCellAnchor? Anchor { get; set; }
}

/// <summary>
/// A row in a table.
/// </summary>
public sealed class TableRow
{
    /// <summary>Row height in EMU.</summary>
    public long HeightEmu { get; set; }

    /// <summary>
    /// Optional height constraint retained from a rich-text source. RTF's positive
    /// <c>\trrh</c> value maps to <see cref="TableRowHeightRule.AtLeast"/> and
    /// its negative value maps to <see cref="TableRowHeightRule.Exact"/>.
    /// </summary>
    public TableRowHeightRule? HeightRule { get; set; }

    /// <summary>Horizontal placement of this row. Null defaults to left.</summary>
    public TableRowHorizontalAlignment? HorizontalAlignment { get; set; }

    /// <summary>Cells in this row, one per column (even merged cells occupy a slot).</summary>
    public List<TableCell> Cells { get; } = new();
}

/// <summary>
/// Table styling flags from <c>a:tblPr</c>. These drive which table-style band/header
/// fills apply to which rows/columns.
/// </summary>
public sealed class TableStyleFlags
{
    public bool FirstRow  { get; set; }
    public bool LastRow   { get; set; }
    public bool FirstCol  { get; set; }
    public bool LastCol   { get; set; }
    public bool BandRow   { get; set; } = true;
    public bool BandCol   { get; set; }
}

/// <summary>
/// A resolved band/header/footer style entry from the table style XML, used to
/// compute effective fill/border/text color for cells that have no explicit tcPr overrides.
/// </summary>
public sealed class TableStyleEntry
{
    /// <summary>Fill for this style region. Null = transparent.</summary>
    public ShapeFill? Fill { get; set; }

    /// <summary>Default border outline for all sides. Null = no border.</summary>
    public ShapeOutline? BorderOutline { get; set; }

    /// <summary>Default text color. Null = inherit.</summary>
    public ThemeAwareColor? TextColor { get; set; }
}

/// <summary>
/// Parsed table style, capturing the relevant regions. Used for effective-fill resolution.
/// Only the regions needed for visible rendering are stored; diagonal/3D borders are not modelled.
/// </summary>
public sealed class TableStyleData
{
    /// <summary>The GUID of this style (from tableStyles.xml).</summary>
    public string StyleId { get; set; } = string.Empty;

    /// <summary>Whole-table default style.</summary>
    public TableStyleEntry? WholeTbl   { get; set; }

    /// <summary>First row (header) style.</summary>
    public TableStyleEntry? FirstRow   { get; set; }

    /// <summary>Last row (footer) style.</summary>
    public TableStyleEntry? LastRow    { get; set; }

    /// <summary>First column style.</summary>
    public TableStyleEntry? FirstCol   { get; set; }

    /// <summary>Last column style.</summary>
    public TableStyleEntry? LastCol    { get; set; }

    /// <summary>Even (band 1) row style.</summary>
    public TableStyleEntry? Band1H     { get; set; }

    /// <summary>Odd (band 2) row style.</summary>
    public TableStyleEntry? Band2H     { get; set; }

    /// <summary>Even (band 1) column style.</summary>
    public TableStyleEntry? Band1V     { get; set; }

    /// <summary>Odd (band 2) column style.</summary>
    public TableStyleEntry? Band2V     { get; set; }
}

/// <summary>
/// The table payload attached to a <see cref="SlideShape"/> when <c>Kind == Table</c>.
///
/// Effective cell fill/border resolution strategy (resolved in compositor, stored as
/// computed values in <see cref="TableCell.Fill"/> / <see cref="TableCell.Borders"/> after
/// layering is applied):
///
///   1. WholeTbl (base)
///   2. Band fill (BandRow → Band1H/Band2H, BandCol → Band1V/Band2V, by row/col index)
///   3. FirstRow/LastRow/FirstCol/LastCol overrides (by flags + position)
///   4. Explicit tcPr fill/border from the cell XML (always wins)
///
/// The compositor calls <see cref="ComputeEffectiveFill"/> / <see cref="ComputeEffectiveBorderOutline"/>
/// to resolve each cell rather than pre-baking the values, so the model stays clean.
/// </summary>
public sealed class TableShape
{
    /// <summary>Column widths in EMU, one per column.</summary>
    public List<long> ColumnWidthsEmu { get; } = new();

    /// <summary>Left table indent in points from an external rich-text source.</summary>
    public double? RichTextLeftIndentPt { get; set; }

    /// <summary>Gap between adjacent cells in points from an external rich-text source.</summary>
    public double? RichTextCellSpacingPt { get; set; }

    /// <summary>Rows in the table (each row contains one cell per column).</summary>
    public List<TableRow> Rows { get; } = new();

    /// <summary>Style flags (firstRow/lastRow/firstCol/lastCol/bandRow/bandCol).</summary>
    public TableStyleFlags Flags { get; set; } = new();

    /// <summary>GUID of the table style referenced from tableStyles.xml.</summary>
    public string? TableStyleId { get; set; }

    /// <summary>
    /// Parsed table style data. Populated by the reader when tableStyles.xml is present.
    /// Null if the style could not be resolved (compositor falls back to defaults).
    /// </summary>
    public TableStyleData? StyleData { get; set; }

    // ── Effective-formatting helpers ─────────────────────────────────────────────

    /// <summary>
    /// Computes the effective fill for a cell at (rowIndex, colIndex), layering:
    /// wholeTbl → band → firstRow/lastRow/firstCol/lastCol → explicit tcPr fill.
    /// Returns null for transparent (no fill).
    /// </summary>
    public ShapeFill? ComputeEffectiveFill(int rowIndex, int colIndex, TableCell cell)
    {
        // Start from wholeTbl base.
        ShapeFill? fill = StyleData?.WholeTbl?.Fill;

        if (StyleData != null)
        {
            int rowCount = Rows.Count;
            int colCount = ColumnWidthsEmu.Count;

            // Band fills (lower priority than first/last row/col).
            if (Flags.BandRow)
            {
                // If firstRow is enabled, the first data row starts at index 1
                // (index 0 is the header, which gets firstRow treatment below).
                int bandBase = Flags.FirstRow ? 1 : 0;
                bool isFirstRowRegion = Flags.FirstRow && rowIndex == 0;

                if (!isFirstRowRegion)
                {
                    int adjustedRow = rowIndex - bandBase;
                    bool isBand1 = adjustedRow % 2 == 0;
                    fill = (isBand1 ? StyleData.Band1H?.Fill : StyleData.Band2H?.Fill) ?? fill;
                }
            }
            else if (Flags.BandCol)
            {
                int bandBase = Flags.FirstCol ? 1 : 0;
                bool isFirstColRegion = Flags.FirstCol && colIndex == 0;

                if (!isFirstColRegion)
                {
                    int adjustedCol = colIndex - bandBase;
                    bool isBand1 = adjustedCol % 2 == 0;
                    fill = (isBand1 ? StyleData.Band1V?.Fill : StyleData.Band2V?.Fill) ?? fill;
                }
            }

            // Row/col position overrides (higher priority than bands).
            if (Flags.FirstRow && rowIndex == 0)
                fill = StyleData.FirstRow?.Fill ?? fill;
            if (Flags.LastRow && rowIndex == rowCount - 1)
                fill = StyleData.LastRow?.Fill ?? fill;
            if (Flags.FirstCol && colIndex == 0)
                fill = StyleData.FirstCol?.Fill ?? fill;
            if (Flags.LastCol && colIndex == colCount - 1)
                fill = StyleData.LastCol?.Fill ?? fill;
        }

        // Explicit tcPr fill always wins.
        if (cell.Fill is not null)
            fill = cell.Fill;

        return fill;
    }

    /// <summary>
    /// Computes the effective border outline for a cell using the same layering as fills.
    /// Returns null for no border.
    /// </summary>
    public ShapeOutline? ComputeEffectiveBorderOutline(int rowIndex, int colIndex, TableCell cell)
    {
        // Start from wholeTbl border.
        ShapeOutline? border = StyleData?.WholeTbl?.BorderOutline;

        if (StyleData != null)
        {
            int rowCount = Rows.Count;
            int colCount = ColumnWidthsEmu.Count;

            if (Flags.BandRow)
            {
                int bandBase = Flags.FirstRow ? 1 : 0;
                bool isFirstRowRegion = Flags.FirstRow && rowIndex == 0;
                if (!isFirstRowRegion)
                {
                    int adjustedRow = rowIndex - bandBase;
                    bool isBand1 = adjustedRow % 2 == 0;
                    border = (isBand1 ? StyleData.Band1H?.BorderOutline : StyleData.Band2H?.BorderOutline) ?? border;
                }
            }

            if (Flags.FirstRow && rowIndex == 0)
                border = StyleData.FirstRow?.BorderOutline ?? border;
            if (Flags.LastRow && rowIndex == rowCount - 1)
                border = StyleData.LastRow?.BorderOutline ?? border;
            if (Flags.FirstCol && colIndex == 0)
                border = StyleData.FirstCol?.BorderOutline ?? border;
            if (Flags.LastCol && colIndex == colCount - 1)
                border = StyleData.LastCol?.BorderOutline ?? border;
        }

        return border;
    }

    /// <summary>
    /// Computes the effective text color for a cell using the same layering as fills.
    /// Returns null to inherit from run/paragraph defaults.
    /// </summary>
    public ThemeAwareColor? ComputeEffectiveTextColor(int rowIndex, int colIndex)
    {
        ThemeAwareColor? color = StyleData?.WholeTbl?.TextColor;

        if (StyleData != null)
        {
            int rowCount = Rows.Count;
            int colCount = ColumnWidthsEmu.Count;

            if (Flags.BandRow)
            {
                int bandBase = Flags.FirstRow ? 1 : 0;
                bool isFirstRowRegion = Flags.FirstRow && rowIndex == 0;
                if (!isFirstRowRegion)
                {
                    int adjustedRow = rowIndex - bandBase;
                    bool isBand1 = adjustedRow % 2 == 0;
                    color = (isBand1 ? StyleData.Band1H?.TextColor : StyleData.Band2H?.TextColor) ?? color;
                }
            }

            if (Flags.FirstRow && rowIndex == 0)
                color = StyleData.FirstRow?.TextColor ?? color;
            if (Flags.LastRow && rowIndex == rowCount - 1)
                color = StyleData.LastRow?.TextColor ?? color;
            if (Flags.FirstCol && colIndex == 0)
                color = StyleData.FirstCol?.TextColor ?? color;
            if (Flags.LastCol && colIndex == colCount - 1)
                color = StyleData.LastCol?.TextColor ?? color;
        }

        return color;
    }
}
