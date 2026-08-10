using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Removes one series from a chart's Select Data Source "Legend Entries (Series)" list
/// (R92-app-chart-data-edit-5-1). FreeX charts have no independent per-series range storage --
/// series are columns (or, when <see cref="ChartModel.SeriesInRows"/>, rows) of a single contiguous
/// <see cref="ChartModel.DataRange"/> -- so "removing a series" means excluding its worksheet column
/// from the plotted set via an authoritative <see cref="ChartModel.SeriesColumnMappings"/> list
/// (the same mapping <c>ChartRenderer.SeriesFormatting.cs</c>'s <c>ShouldRenderColumnAsSeries</c>/
/// <c>GetSeriesIndex</c> already honor for every chart type), rather than shrinking DataRange (which
/// would also drop every OTHER still-wanted series' column if the removed series sits in the middle).
/// <para>
/// Scoped to the case this can represent soundly: column-major charts (not
/// <see cref="ChartModel.SeriesInRows"/> -- a row-major chart has no equivalent "skip this row"
/// mapping) and not Bubble/Scatter (whose column layout has a different, non-1-series-per-column
/// meaning). Removing an individual series independent of a contiguous same-sheet range, and
/// Add/Edit Series with an arbitrary (esp. cross-sheet) range, remain out of reach without adding a
/// new per-series range/name storage field to <see cref="ChartModel"/> that the renderer (every
/// ChartRenderer.*.cs series-iteration site), the Avalonia chart renderer, and the XLSX chart
/// writer/reader would all need to honor -- deferred as a separate, larger subsystem change.
/// </para>
/// <para>
/// When a series is removed, every SeriesIndex-keyed per-series/per-point override is remapped
/// (indexes above the removed one shift down by one; entries pointing AT the removed series are
/// dropped/cleared) rather than wholesale-cleared, following the same "an index-shift invalidates
/// stale SeriesIndex-keyed state" principle <see cref="ChangeChartSourceCommand"/> established
/// (r84/r86) -- just precise instead of blunt, since only ONE series' worth of indexes actually move.
/// </para>
/// </summary>
public sealed class RemoveChartSeriesCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _chartId;
    private readonly int _seriesIndex;

    private bool _applied;
    private List<ChartSeriesColumnMapping>? _previousSeriesColumnMappings;
    private List<ChartSeriesVerbatimFormulas>? _previousVerbatimSeriesFormulas;
    private List<ChartSeriesOrderOverride>? _previousSeriesOrderOverrides;
    private List<ChartPointMarkerFormat>? _previousPointMarkerFormats;
    private List<ChartSeriesRawXmlEntry>? _previousMultiLevelCategoryXml;
    private List<ChartPointExplosion>? _previousExplodedSlices;
    private List<ChartRangeDataLabel>? _previousRangeDataLabels;
    private List<ChartSeriesRangeDataLabels>? _previousSeriesRangeDataLabels;
    private List<int>? _previousSecondaryAxisSeriesIndexes;
    private List<int>? _previousComboLineSeriesIndexes;
    private List<int>? _previousComboScatterSeriesIndexes;
    private int _previousTrendlineSeriesIndex;
    private int _previousErrorBarSeriesIndex;
    private bool _previousShowLinearTrendline;
    private bool _previousShowErrorBars;
    private List<ChartSeriesFormat>? _previousSeriesFormats;
    private List<ChartPointFillFormat>? _previousPointFillColors;
    private List<ChartSeriesDataLabelFormat>? _previousSeriesDataLabelFormats;
    private List<ChartPointDataLabelFormat>? _previousPointDataLabelFormats;
    private List<ChartSeriesRawXmlEntry>? _previousAdditionalSeriesErrorBarsXml;
    private List<ChartSeriesRawXmlEntry>? _previousAdditionalSeriesTrendlinesXml;
    private List<int>? _previousSeriesPlotOrder;
    private List<ChartLegendEntryModel>? _previousLegendEntries;

    public string Label => "Remove Chart Series";

    public RemoveChartSeriesCommand(SheetId sheetId, Guid chartId, int seriesIndex)
    {
        _sheetId = sheetId;
        _chartId = chartId;
        _seriesIndex = seriesIndex;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!ChartCommandGuards.TryFindChart(sheet, _chartId, out var chart))
            return ChartCommandGuards.ChartNotFound();

        // R112-model-drawing-object-lock-1-1 sibling fix: layer in the per-chart Locked override so
        // an author-unlocked chart's series list stays editable even while the sheet blocks "Edit
        // objects".
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(sheet, chart) is { } protectedOutcome)
            return protectedOutcome;
        if (chart.IsPivotChart)
            return ChartCommandGuards.SelectedChartIsPivotChart();
        if (chart.SeriesInRows || chart.Type is ChartType.Bubble or ChartType.Scatter)
            return new CommandOutcome(false, "Removing an individual series is only supported for column-based charts (not Switch Row/Column, Bubble, or Scatter).");

        var startCol = chart.DataRange.Start.Col;
        var endCol = chart.DataRange.End.Col;
        var dataStartCol = chart.FirstColIsCategories && endCol > startCol ? startCol + 1 : startCol;
        if (dataStartCol > endCol)
            return new CommandOutcome(false, "Chart has no series to remove.");

        var columns = ChartSeriesColumnPolicy.GetCurrentSeriesColumns(chart, dataStartCol, endCol);
        if (_seriesIndex < 0 || _seriesIndex >= columns.Count)
            return new CommandOutcome(false, "Series index is out of range.");
        if (columns.Count <= 1)
            return new CommandOutcome(false, "A chart must keep at least one data series.");

        var removedSeriesIndex = columns[_seriesIndex].SeriesIndex;

        _previousSeriesColumnMappings = chart.SeriesColumnMappings;
        _previousVerbatimSeriesFormulas = chart.VerbatimSeriesFormulas;
        _previousSeriesOrderOverrides = chart.SeriesOrderOverrides;
        _previousPointMarkerFormats = chart.PointMarkerFormats;
        _previousMultiLevelCategoryXml = chart.MultiLevelCategoryXml;
        _previousExplodedSlices = chart.ExplodedSlices;
        _previousRangeDataLabels = chart.RangeDataLabels;
        _previousSeriesRangeDataLabels = chart.SeriesRangeDataLabels;
        _previousSecondaryAxisSeriesIndexes = chart.SecondaryAxisSeriesIndexes;
        _previousComboLineSeriesIndexes = chart.ComboLineSeriesIndexes;
        _previousComboScatterSeriesIndexes = chart.ComboScatterSeriesIndexes;
        _previousTrendlineSeriesIndex = chart.TrendlineSeriesIndex;
        _previousErrorBarSeriesIndex = chart.ErrorBarSeriesIndex;
        _previousShowLinearTrendline = chart.ShowLinearTrendline;
        _previousShowErrorBars = chart.ShowErrorBars;
        _previousSeriesFormats = chart.SeriesFormats;
        _previousPointFillColors = chart.PointFillColors;
        _previousSeriesDataLabelFormats = chart.SeriesDataLabelFormats;
        _previousPointDataLabelFormats = chart.PointDataLabelFormats;
        _previousAdditionalSeriesErrorBarsXml = chart.AdditionalSeriesErrorBarsXml;
        _previousAdditionalSeriesTrendlinesXml = chart.AdditionalSeriesTrendlinesXml;
        _previousSeriesPlotOrder = chart.SeriesPlotOrder;
        _previousLegendEntries = chart.LegendEntries;
        _applied = true;

        var remainingColumns = new List<(int SeriesIndex, uint Column)>(columns.Count - 1);
        for (var i = 0; i < columns.Count; i++)
        {
            if (i != _seriesIndex)
                remainingColumns.Add((columns[i].SeriesIndex, columns[i].Column));
        }

        chart.SeriesColumnMappings = remainingColumns
            .Select((entry, newIndex) => new ChartSeriesColumnMapping(newIndex, entry.Column))
            .ToList();

        if (chart.VerbatimSeriesFormulas is { } verbatimFormulas)
        {
            chart.VerbatimSeriesFormulas = verbatimFormulas
                .Where(v => v.SeriesIndex != removedSeriesIndex)
                .Select(v => v.SeriesIndex > removedSeriesIndex ? v with { SeriesIndex = v.SeriesIndex - 1 } : v)
                .ToList();
        }
        chart.SeriesOrderOverrides = chart.SeriesOrderOverrides
            .Where(o => o.SeriesIndex != removedSeriesIndex)
            .Select(o => o.SeriesIndex > removedSeriesIndex ? o with { SeriesIndex = o.SeriesIndex - 1 } : o)
            .ToList();
        chart.PointMarkerFormats = chart.PointMarkerFormats
            .Where(f => f.SeriesIndex != removedSeriesIndex)
            .Select(f => f.SeriesIndex > removedSeriesIndex ? f with { SeriesIndex = f.SeriesIndex - 1 } : f)
            .ToList();
        chart.MultiLevelCategoryXml = chart.MultiLevelCategoryXml
            .Where(x => x.SeriesIndex != removedSeriesIndex)
            .Select(x => x.SeriesIndex > removedSeriesIndex ? x with { SeriesIndex = x.SeriesIndex - 1 } : x)
            .ToList();
        chart.ExplodedSlices = chart.ExplodedSlices
            .Where(s => s.SeriesIndex != removedSeriesIndex)
            .Select(s => s.SeriesIndex > removedSeriesIndex ? s with { SeriesIndex = s.SeriesIndex - 1 } : s)
            .ToList();
        chart.RangeDataLabels = chart.RangeDataLabels
            .Where(l => l.SeriesIndex != removedSeriesIndex)
            .Select(l => l.SeriesIndex > removedSeriesIndex ? l with { SeriesIndex = l.SeriesIndex - 1 } : l)
            .ToList();
        chart.SeriesRangeDataLabels = chart.SeriesRangeDataLabels
            .Where(l => l.SeriesIndex != removedSeriesIndex)
            .Select(l => l.SeriesIndex > removedSeriesIndex ? l with { SeriesIndex = l.SeriesIndex - 1 } : l)
            .ToList();
        chart.SecondaryAxisSeriesIndexes = RemapIndexList(chart.SecondaryAxisSeriesIndexes, removedSeriesIndex);
        chart.ComboLineSeriesIndexes = RemapIndexList(chart.ComboLineSeriesIndexes, removedSeriesIndex);
        chart.ComboScatterSeriesIndexes = RemapIndexList(chart.ComboScatterSeriesIndexes, removedSeriesIndex);
        chart.SeriesFormats = chart.SeriesFormats
            .Where(f => f.SeriesIndex != removedSeriesIndex)
            .Select(f => f.SeriesIndex > removedSeriesIndex ? f with { SeriesIndex = f.SeriesIndex - 1 } : f)
            .ToList();
        chart.PointFillColors = chart.PointFillColors
            .Where(p => p.SeriesIndex != removedSeriesIndex)
            .Select(p => p.SeriesIndex > removedSeriesIndex ? p with { SeriesIndex = p.SeriesIndex - 1 } : p)
            .ToList();
        chart.SeriesDataLabelFormats = chart.SeriesDataLabelFormats
            .Where(f => f.SeriesIndex != removedSeriesIndex)
            .Select(f => f.SeriesIndex > removedSeriesIndex ? f with { SeriesIndex = f.SeriesIndex - 1 } : f)
            .ToList();
        chart.PointDataLabelFormats = chart.PointDataLabelFormats
            .Where(f => f.SeriesIndex != removedSeriesIndex)
            .Select(f => f.SeriesIndex > removedSeriesIndex ? f with { SeriesIndex = f.SeriesIndex - 1 } : f)
            .ToList();
        chart.AdditionalSeriesErrorBarsXml = chart.AdditionalSeriesErrorBarsXml
            .Where(x => x.SeriesIndex != removedSeriesIndex)
            .Select(x => x.SeriesIndex > removedSeriesIndex ? x with { SeriesIndex = x.SeriesIndex - 1 } : x)
            .ToList();
        chart.AdditionalSeriesTrendlinesXml = chart.AdditionalSeriesTrendlinesXml
            .Where(x => x.SeriesIndex != removedSeriesIndex)
            .Select(x => x.SeriesIndex > removedSeriesIndex ? x with { SeriesIndex = x.SeriesIndex - 1 } : x)
            .ToList();

        if (chart.TrendlineSeriesIndex == removedSeriesIndex)
            chart.ShowLinearTrendline = false;
        else if (chart.TrendlineSeriesIndex > removedSeriesIndex)
            chart.TrendlineSeriesIndex--;

        if (chart.ErrorBarSeriesIndex == removedSeriesIndex)
            chart.ShowErrorBars = false;
        else if (chart.ErrorBarSeriesIndex > removedSeriesIndex)
            chart.ErrorBarSeriesIndex--;

        RemapPlotOrderAndLegendEntries(chart, removedSeriesIndex);

        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        if (!ChartCommandGuards.TryFindChart(ctx.GetSheet(_sheetId), _chartId, out var chart))
            return;

        chart.SeriesColumnMappings = _previousSeriesColumnMappings ?? [];
        chart.VerbatimSeriesFormulas = _previousVerbatimSeriesFormulas;
        chart.SeriesOrderOverrides = _previousSeriesOrderOverrides ?? [];
        chart.PointMarkerFormats = _previousPointMarkerFormats ?? [];
        chart.MultiLevelCategoryXml = _previousMultiLevelCategoryXml ?? [];
        chart.ExplodedSlices = _previousExplodedSlices ?? [];
        chart.RangeDataLabels = _previousRangeDataLabels ?? [];
        chart.SeriesRangeDataLabels = _previousSeriesRangeDataLabels ?? [];
        chart.SecondaryAxisSeriesIndexes = _previousSecondaryAxisSeriesIndexes ?? [];
        chart.ComboLineSeriesIndexes = _previousComboLineSeriesIndexes ?? [];
        chart.ComboScatterSeriesIndexes = _previousComboScatterSeriesIndexes ?? [];
        chart.TrendlineSeriesIndex = _previousTrendlineSeriesIndex;
        chart.ErrorBarSeriesIndex = _previousErrorBarSeriesIndex;
        chart.ShowLinearTrendline = _previousShowLinearTrendline;
        chart.ShowErrorBars = _previousShowErrorBars;
        chart.SeriesFormats = _previousSeriesFormats ?? [];
        chart.PointFillColors = _previousPointFillColors ?? [];
        chart.SeriesDataLabelFormats = _previousSeriesDataLabelFormats ?? [];
        chart.PointDataLabelFormats = _previousPointDataLabelFormats ?? [];
        chart.AdditionalSeriesErrorBarsXml = _previousAdditionalSeriesErrorBarsXml ?? [];
        chart.AdditionalSeriesTrendlinesXml = _previousAdditionalSeriesTrendlinesXml ?? [];
        chart.SeriesPlotOrder = _previousSeriesPlotOrder ?? [];
        chart.LegendEntries = _previousLegendEntries ?? [];

        _applied = false;
        _previousSeriesColumnMappings = null;
        _previousVerbatimSeriesFormulas = null;
        _previousSeriesOrderOverrides = null;
        _previousPointMarkerFormats = null;
        _previousMultiLevelCategoryXml = null;
        _previousExplodedSlices = null;
        _previousRangeDataLabels = null;
        _previousSeriesRangeDataLabels = null;
        _previousSecondaryAxisSeriesIndexes = null;
        _previousComboLineSeriesIndexes = null;
        _previousComboScatterSeriesIndexes = null;
        _previousSeriesFormats = null;
        _previousPointFillColors = null;
        _previousSeriesDataLabelFormats = null;
        _previousPointDataLabelFormats = null;
        _previousAdditionalSeriesErrorBarsXml = null;
        _previousAdditionalSeriesTrendlinesXml = null;
        _previousSeriesPlotOrder = null;
        _previousLegendEntries = null;
    }

    private static List<int> RemapIndexList(List<int> indexes, int removedSeriesIndex) =>
        indexes
            .Where(i => i != removedSeriesIndex)
            .Select(i => i > removedSeriesIndex ? i - 1 : i)
            .ToList();

    /// <summary>
    /// Remaps <see cref="ChartModel.SeriesPlotOrder"/> (declaration-order idx list) and
    /// <see cref="ChartModel.LegendEntries"/> (legend-POSITION-keyed overrides, resolved through
    /// plot order by <c>ChartRenderer.SeriesFormatting.cs</c>'s <c>IsLegendEntryDeleted</c>) so a
    /// removed series doesn't leave stale position/idx references that silently un-hide or
    /// mis-hide an unrelated legend key. See remarks on the class for the full scenario.
    /// </summary>
    private static void RemapPlotOrderAndLegendEntries(ChartModel chart, int removedSeriesIndex)
    {
        var oldPlotOrder = chart.SeriesPlotOrder;
        if (oldPlotOrder.Count == 0)
        {
            // Legacy case: declaration order equals idx order, so a LegendEntry's Index IS the
            // series idx directly (see IsLegendEntryDeleted's fallback) -- remap exactly like
            // every other SeriesIndex-keyed list above.
            chart.LegendEntries = chart.LegendEntries
                .Where(e => e.Index != removedSeriesIndex)
                .Select(e => e.Index > removedSeriesIndex ? e with { Index = e.Index - 1 } : e)
                .ToList();
            return;
        }

        var removedPosition = oldPlotOrder.IndexOf(removedSeriesIndex);
        if (removedPosition < 0)
        {
            // Defensive: the removed series idx wasn't declared in the plot order (shouldn't
            // happen for a well-formed chart). Keep the list length stable and just shift down
            // idx values above the removed one; leave LegendEntries untouched since we cannot
            // safely resolve which position, if any, referred to the removed series.
            chart.SeriesPlotOrder = oldPlotOrder
                .Select(idx => idx > removedSeriesIndex ? idx - 1 : idx)
                .ToList();
            return;
        }

        chart.SeriesPlotOrder = oldPlotOrder
            .Where((_, position) => position != removedPosition)
            .Select(idx => idx > removedSeriesIndex ? idx - 1 : idx)
            .ToList();

        chart.LegendEntries = chart.LegendEntries
            .Where(e => e.Index != removedPosition)
            .Select(e => e.Index > removedPosition ? e with { Index = e.Index - 1 } : e)
            .ToList();
    }

}
