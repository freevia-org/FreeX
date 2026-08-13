using FreeX.Core.Model;
using FreeX.App.Presentation.GridInteraction;

namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>
/// The data kind of a single selected column, used to decide which Quick Analysis suggestions apply.
/// </summary>
public enum QuickAnalysisColumnKind
{
    /// <summary>The column holds no analysable values (all blank).</summary>
    Empty,

    /// <summary>The column holds numeric values.</summary>
    Numeric,

    /// <summary>The column holds date/time values.</summary>
    Date,

    /// <summary>The column holds text values.</summary>
    Text
}

/// <summary>
/// A portable description of a selected range, sufficient to decide which Quick Analysis suggestions
/// apply. This is the only input to <see cref="QuickAnalysisModelBuilder"/>; it carries no host or
/// renderer types so the decision model stays portable.
/// </summary>
/// <param name="Range">The selected range on the sheet.</param>
/// <param name="HasHeaderRow">
/// True when the first row of the range is a header row (labels) rather than data. Affects whether the
/// selection looks tabular (Tables group) and how many data rows remain for totals/sparklines.
/// </param>
/// <param name="ColumnKinds">
/// The data kind of each column, left to right. When empty, every column is treated as
/// <see cref="QuickAnalysisColumnKind.Empty"/>.
/// </param>
public sealed record QuickAnalysisSelectionDescription(
    GridRange Range,
    bool HasHeaderRow,
    IReadOnlyList<QuickAnalysisColumnKind> ColumnKinds)
{
    /// <summary>
    /// Populated when the selection is wholly contained by a structured table. The shared model uses
    /// this to avoid offering conversion to a table that already owns the selected cells.
    /// </summary>
    public StructuredTableSelectionContext? StructuredTableContext { get; init; }

    /// <summary>
    /// Overrides heuristic row counting when a structured table has an explicit data body. Header and
    /// totals rows are table chrome, not analysis data.
    /// </summary>
    public uint? DataRowCountOverride { get; init; }

    /// <summary>True when any selected cell overlaps an existing structured table.</summary>
    public bool OverlapsStructuredTable { get; init; }

    /// <summary>Number of rows in the selection (including any header row).</summary>
    public uint RowCount => Range.RowCount;

    /// <summary>Number of columns in the selection.</summary>
    public uint ColCount => Range.ColCount;

    /// <summary>Number of data rows, excluding the header row when present.</summary>
    public uint DataRowCount =>
        DataRowCountOverride ?? (HasHeaderRow && RowCount > 0 ? RowCount - 1 : RowCount);

    /// <summary>True when the selected cells already belong to a structured table.</summary>
    public bool IsStructuredTableSelection => StructuredTableContext is not null;

    /// <summary>True when totals or sparklines can be placed immediately to the right.</summary>
    public bool CanWriteAdjacentColumn => Range.End.Col < CellAddress.MaxCol;

    /// <summary>True when the selection is a single cell, which offers no suggestions.</summary>
    public bool IsSingleCell => RowCount == 1 && ColCount == 1;

    /// <summary>
    /// True when the selection is degenerate for analysis: a single cell, or a selection with no
    /// described columns to reason about. Either way it offers no suggestions.
    /// </summary>
    public bool IsEmpty => IsSingleCell || ColumnKinds.Count == 0;

    /// <summary>True when any selected column holds numeric values.</summary>
    public bool HasNumericColumn => HasColumnOfKind(QuickAnalysisColumnKind.Numeric);

    /// <summary>True when any selected column holds date values.</summary>
    public bool HasDateColumn => HasColumnOfKind(QuickAnalysisColumnKind.Date);

    /// <summary>True when any selected column holds text values.</summary>
    public bool HasTextColumn => HasColumnOfKind(QuickAnalysisColumnKind.Text);

    /// <summary>Count of columns that hold numeric values.</summary>
    public int NumericColumnCount => CountColumnsOfKind(QuickAnalysisColumnKind.Numeric);

    /// <summary>
    /// True when the selection has at least one non-header data row to aggregate over. Totals and
    /// sparklines need real data rows to act on.
    /// </summary>
    public bool HasDataRows => DataRowCount >= 1;

    private bool HasColumnOfKind(QuickAnalysisColumnKind kind)
    {
        for (var i = 0; i < ColumnKinds.Count; i++)
        {
            if (ColumnKinds[i] == kind)
                return true;
        }

        return false;
    }

    private int CountColumnsOfKind(QuickAnalysisColumnKind kind)
    {
        var count = 0;
        for (var i = 0; i < ColumnKinds.Count; i++)
        {
            if (ColumnKinds[i] == kind)
                count++;
        }

        return count;
    }
}
