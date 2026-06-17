using FreeX.Core.Commands;

namespace FreeX.App.Presentation.Consolidate;

/// <summary>
/// One source range's data for a consolidation: a rectangular grid of cell values addressed
/// <c>[row][column]</c>. Rows need not all be the same width — the planner reads each cell defensively —
/// but for by-position consolidation the host is expected to supply equally sized sources, matching the
/// desktop hosts which reject mismatched source sizes before planning.
/// </summary>
public sealed class ConsolidateSource
{
    /// <summary>Creates a source from a row-major grid of cell values.</summary>
    public ConsolidateSource(IReadOnlyList<IReadOnlyList<ConsolidateCellValue>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Rows = rows;
    }

    /// <summary>The grid, addressed <c>[row][column]</c>.</summary>
    public IReadOnlyList<IReadOnlyList<ConsolidateCellValue>> Rows { get; }

    /// <summary>The number of rows in this source.</summary>
    public int RowCount => Rows.Count;

    /// <summary>The number of columns in the widest row, or zero when there are no rows.</summary>
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(row => row.Count);

    /// <summary>
    /// Returns the cell at <paramref name="row"/>/<paramref name="column"/>, or <see cref="ConsolidateCellValue.Blank"/>
    /// when the position is outside this source's grid.
    /// </summary>
    public ConsolidateCellValue CellAt(int row, int column)
    {
        if (row < 0 || row >= Rows.Count)
            return ConsolidateCellValue.Blank;

        var cells = Rows[row];
        return column < 0 || column >= cells.Count ? ConsolidateCellValue.Blank : cells[column];
    }

    /// <summary>Builds a source from a rectangular <see cref="ConsolidateCellValue"/> array.</summary>
    public static ConsolidateSource FromGrid(ConsolidateCellValue[,] grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var rowCount = grid.GetLength(0);
        var colCount = grid.GetLength(1);
        var rows = new List<IReadOnlyList<ConsolidateCellValue>>(rowCount);
        for (var row = 0; row < rowCount; row++)
        {
            var cells = new ConsolidateCellValue[colCount];
            for (var col = 0; col < colCount; col++)
                cells[col] = grid[row, col];
            rows.Add(cells);
        }

        return new ConsolidateSource(rows);
    }
}

/// <summary>
/// The options that drive a consolidation, mirroring the desktop hosts' Consolidate dialog: the
/// aggregation <see cref="Function"/>, whether to match cells by position or by labels, and which edges of
/// each source carry the labels.
/// </summary>
public sealed class ConsolidateOptions
{
    /// <summary>The aggregation applied per output cell. Defaults to <see cref="ConsolidateFunction.Sum"/>.</summary>
    public ConsolidateFunction Function { get; init; } = ConsolidateFunction.Sum;

    /// <summary>
    /// When true, the top row of each source carries the column labels and consolidation matches columns by
    /// label. Setting either this or <see cref="UseLeftColumnLabels"/> selects label-based consolidation.
    /// </summary>
    public bool UseTopRowLabels { get; init; }

    /// <summary>
    /// When true, the left column of each source carries the row labels and consolidation matches rows by
    /// label. Setting either this or <see cref="UseTopRowLabels"/> selects label-based consolidation.
    /// </summary>
    public bool UseLeftColumnLabels { get; init; }

    /// <summary>
    /// True when consolidation should match cells by row/column label rather than by position — i.e. when
    /// either label edge is enabled.
    /// </summary>
    public bool ConsolidateByLabels => UseTopRowLabels || UseLeftColumnLabels;
}

/// <summary>
/// A single planned output cell: its position in the result grid (<c>[Row][Column]</c>), and either the
/// aggregated number or a label header text. Label header cells carry <see cref="Text"/> and no number;
/// data cells carry <see cref="Number"/> and no text. The top-left corner cell of a fully labelled result
/// is blank (neither set).
/// </summary>
public sealed record ConsolidateOutputCell(int Row, int Column, double? Number, string? Text)
{
    /// <summary>True when this cell holds an aggregated numeric value.</summary>
    public bool IsNumber => Number.HasValue;

    /// <summary>True when this cell holds a label header text.</summary>
    public bool IsLabel => Text is not null;

    /// <summary>True when this cell is the empty top-left corner of a fully labelled result.</summary>
    public bool IsBlank => Number is null && Text is null;

    internal static ConsolidateOutputCell ForNumber(int row, int column, double value) => new(row, column, value, null);

    internal static ConsolidateOutputCell ForLabel(int row, int column, string text) => new(row, column, null, text);

    internal static ConsolidateOutputCell ForBlank(int row, int column) => new(row, column, null, null);
}

/// <summary>
/// The result of a consolidation: the output grid dimensions and every planned cell. The host writes each
/// cell into its destination, offset from the chosen destination anchor by the cell's <see cref="ConsolidateOutputCell.Row"/>
/// and <see cref="ConsolidateOutputCell.Column"/>. For label-based results the row/column label headers are
/// included as label cells; for by-position results every cell is a number.
/// </summary>
public sealed record ConsolidateResult(
    int RowCount,
    int ColumnCount,
    IReadOnlyList<ConsolidateOutputCell> Cells)
{
    /// <summary>An empty result with no cells.</summary>
    public static ConsolidateResult Empty { get; } = new(0, 0, []);

    /// <summary>True when the result has no cells.</summary>
    public bool IsEmpty => Cells.Count == 0;
}
