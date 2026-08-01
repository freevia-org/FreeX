using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ChangePivotChartTypeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _chartId;
    private readonly ChartType _chartType;
    private ChartType? _previousType;
    private bool? _previousFirstColIsCategories;

    public string Label => "Change PivotChart Type";

    public ChangePivotChartTypeCommand(SheetId sheetId, Guid chartId, ChartType chartType)
    {
        _sheetId = sheetId;
        _chartId = chartId;
        _chartType = Enum.IsDefined(chartType) ? chartType : ChartType.Column;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } pivotProtectedOutcome)
            return pivotProtectedOutcome;

        if (!ChartCommandGuards.TryFindChart(sheet, _chartId, out var chart))
            return ChartCommandGuards.PivotChartNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-chart Locked override so an
        // author-unlocked PivotChart's type stays editable even while the sheet blocks "Edit objects".
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(sheet, chart) is { } protectedOutcome)
            return protectedOutcome;
        if (!chart.IsPivotChart || string.IsNullOrWhiteSpace(chart.PivotTableName))
            return ChartCommandGuards.SelectedChartIsNotPivotChart();
        if (ChartAuthoringPlanner.RejectIfUnsupported(_chartType) is { } unsupportedOutcome)
            return unsupportedOutcome;

        _previousType = chart.Type;
        _previousFirstColIsCategories = chart.FirstColIsCategories;
        chart.Type = _chartType;
        chart.FirstColIsCategories = _chartType is not (ChartType.Scatter or ChartType.Bubble);
        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousType is null || _previousFirstColIsCategories is null)
            return;

        if (!ChartCommandGuards.TryFindChart(ctx.GetSheet(_sheetId), _chartId, out var chart))
            return;

        chart.Type = _previousType.Value;
        chart.FirstColIsCategories = _previousFirstColIsCategories.Value;
        _previousType = null;
        _previousFirstColIsCategories = null;
    }
}

public sealed class SetChartStyleCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _chartId;
    private readonly int? _chartStyleId;
    private int? _previousChartStyleId;
    private bool _applied;

    public string Label => "Chart Style";

    public SetChartStyleCommand(SheetId sheetId, Guid chartId, int? chartStyleId)
    {
        _sheetId = sheetId;
        _chartId = chartId;
        _chartStyleId = NormalizeStyleId(chartStyleId);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!ChartCommandGuards.TryFindChart(sheet, _chartId, out var chart))
            return ChartCommandGuards.ChartNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-chart Locked override so an
        // author-unlocked chart's style stays editable even while the sheet blocks "Edit objects".
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(sheet, chart) is { } protectedOutcome)
            return protectedOutcome;

        _previousChartStyleId = chart.ChartStyleId;
        chart.ChartStyleId = _chartStyleId;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        if (!ChartCommandGuards.TryFindChart(ctx.GetSheet(_sheetId), _chartId, out var chart))
            return;

        chart.ChartStyleId = _previousChartStyleId;
        _previousChartStyleId = null;
        _applied = false;
    }

    private static int? NormalizeStyleId(int? chartStyleId)
    {
        if (chartStyleId is null)
            return null;

        return Math.Clamp(chartStyleId.Value, 1, 48);
    }
}

public sealed class ChangeChartTypeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _chartId;
    private readonly ChartType _chartType;
    private ChartType? _previousType;
    private bool? _previousFirstColIsCategories;

    public string Label => "Change Chart Type";

    public ChangeChartTypeCommand(SheetId sheetId, Guid chartId, ChartType chartType)
    {
        _sheetId = sheetId;
        _chartId = chartId;
        _chartType = Enum.IsDefined(chartType) ? chartType : ChartType.Column;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!ChartCommandGuards.TryFindChart(sheet, _chartId, out var chart))
            return ChartCommandGuards.ChartNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-chart Locked override so an
        // author-unlocked chart's type stays editable even while the sheet blocks "Edit objects".
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(sheet, chart) is { } protectedOutcome)
            return protectedOutcome;
        if (chart.IsPivotChart)
            return ChartCommandGuards.SelectedChartIsPivotChart();
        if (ChartAuthoringPlanner.RejectIfUnsupported(_chartType) is { } unsupportedOutcome)
            return unsupportedOutcome;

        var firstColIsCategories = _chartType is not (ChartType.Scatter or ChartType.Bubble);
        if (!HasUsableChartData(_chartType, chart.DataRange, chart.FirstRowIsHeader, firstColIsCategories, chart.SeriesInRows))
            return new CommandOutcome(false, "Chart data range is not valid for the selected chart type.");

        _previousType = chart.Type;
        _previousFirstColIsCategories = chart.FirstColIsCategories;
        chart.Type = _chartType;
        chart.FirstColIsCategories = firstColIsCategories;
        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousType is null || _previousFirstColIsCategories is null)
            return;

        if (!ChartCommandGuards.TryFindChart(ctx.GetSheet(_sheetId), _chartId, out var chart))
            return;

        chart.Type = _previousType.Value;
        chart.FirstColIsCategories = _previousFirstColIsCategories.Value;
        _previousType = null;
        _previousFirstColIsCategories = null;
    }

    internal static bool HasUsableChartData(
        ChartType chartType,
        GridRange dataRange,
        bool firstRowIsHeader,
        bool firstColIsCategories,
        bool seriesInRows = false)
    {
        var candidate = new ChartModel
        {
            Type = chartType,
            DataRange = dataRange,
            FirstRowIsHeader = firstRowIsHeader,
            FirstColIsCategories = firstColIsCategories,
            SeriesInRows = seriesInRows
        };

        return ChartTypeSupport.GetDataSeriesCount(candidate) > 0
            && ChartTypeSupport.GetDataPointCount(candidate) > 0;
    }
}

public sealed class ChangeChartSourceCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _chartId;
    private readonly GridRange _dataRange;
    private readonly bool? _firstRowIsHeader;
    private readonly bool? _firstColIsCategories;
    private readonly bool? _seriesInRows;
    private GridRange? _previousDataRange;
    private bool? _previousFirstRowIsHeader;
    private bool? _previousFirstColIsCategories;
    private bool? _previousSeriesInRows;
    private List<ChartSeriesColumnMapping>? _previousSeriesColumnMappings;
    private List<ChartSeriesVerbatimFormulas>? _previousVerbatimSeriesFormulas;
    private List<ChartSeriesOrderOverride>? _previousSeriesOrderOverrides;
    private List<ChartSeriesRawXmlEntry>? _previousMultiLevelCategoryXml;
    private List<ChartPointMarkerFormat>? _previousPointMarkerFormats;
    private List<ChartPointExplosion>? _previousExplodedSlices;
    private List<ChartRangeDataLabel>? _previousRangeDataLabels;
    private List<ChartSeriesRangeDataLabels>? _previousSeriesRangeDataLabels;
    private List<int>? _previousSecondaryAxisSeriesIndexes;
    private List<int>? _previousComboLineSeriesIndexes;
    private List<int>? _previousComboScatterSeriesIndexes;
    private int? _previousTrendlineSeriesIndex;
    private int? _previousErrorBarSeriesIndex;
    private bool? _previousShowLinearTrendline;
    private bool? _previousShowErrorBars;
    private List<ChartSeriesFormat>? _previousSeriesFormats;
    private List<ChartPointFillFormat>? _previousPointFillColors;
    private List<ChartSeriesDataLabelFormat>? _previousSeriesDataLabelFormats;
    private List<ChartPointDataLabelFormat>? _previousPointDataLabelFormats;
    private List<ChartSeriesRawXmlEntry>? _previousAdditionalSeriesErrorBarsXml;
    private List<ChartSeriesRawXmlEntry>? _previousAdditionalSeriesTrendlinesXml;
    private List<int>? _previousSeriesPlotOrder;
    private List<ChartLegendEntryModel>? _previousLegendEntries;
    private List<ChartSeriesNameOverride>? _previousSeriesNameOverrides;
    private bool _clearedMappingsForSourceChange;

    public string Label => "Select Chart Data";

    public ChangeChartSourceCommand(
        SheetId sheetId,
        Guid chartId,
        GridRange dataRange,
        bool? firstRowIsHeader = null,
        bool? firstColIsCategories = null,
        bool? seriesInRows = null)
    {
        _sheetId = sheetId;
        _chartId = chartId;
        _dataRange = dataRange;
        _firstRowIsHeader = firstRowIsHeader;
        _firstColIsCategories = firstColIsCategories;
        _seriesInRows = seriesInRows;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!ChartCommandGuards.TryFindChart(sheet, _chartId, out var chart))
            return ChartCommandGuards.ChartNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-chart Locked override so an
        // author-unlocked chart's data source stays editable even while the sheet blocks
        // "Edit objects".
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(sheet, chart) is { } protectedOutcome)
            return protectedOutcome;
        if (chart.IsPivotChart)
            return ChartCommandGuards.SelectedChartIsPivotChart();
        if (_dataRange.Start.Sheet != _sheetId || _dataRange.End.Sheet != _sheetId)
            return ChartCommandGuards.ChartDataRangeOnTargetSheet();

        var nextFirstRowIsHeader = _firstRowIsHeader ?? chart.FirstRowIsHeader;
        var nextFirstColIsCategories = _firstColIsCategories ?? chart.FirstColIsCategories;
        var nextSeriesInRows = _seriesInRows ?? chart.SeriesInRows;
        if (!ChangeChartTypeCommand.HasUsableChartData(
                chart.Type,
                _dataRange,
                nextFirstRowIsHeader,
                nextFirstColIsCategories,
                nextSeriesInRows))
            return new CommandOutcome(false, "Chart data range must include at least one data series and one data point.");

        _previousDataRange = chart.DataRange;
        _previousFirstRowIsHeader = chart.FirstRowIsHeader;
        _previousFirstColIsCategories = chart.FirstColIsCategories;
        _previousSeriesInRows = chart.SeriesInRows;
        if (nextSeriesInRows != chart.SeriesInRows || _dataRange != chart.DataRange)
        {
            // Column-based series mappings and per-series verbatim formulas describe the OLD
            // source (either the old orientation or the old DataRange); keeping them would
            // mis-index series (renderer) or override the newly selected range's formulas (XLSX
            // writer: XlsxChartXmlWriter.Series.cs prefers verbatim?.ValFormula over the
            // range-computed formula), silently reverting a plain "Select Data" range edit on
            // reload. Clear on ANY data-range or orientation change, not just orientation flips.
            // Per-series/per-point overrides (plot order, marker formatting, multi-level category
            // XML) are keyed by SeriesIndex too, so they must be cleared for the same reason -
            // otherwise they silently mis-apply to whichever unrelated series now sits at that
            // index after the re-index. The same applies to pie-slice explosions, "value from
            // cells" range data labels, secondary-axis/combo-line/combo-scatter series-index
            // lists, the scalar trendline/error-bar series indexes, and per-series/per-point
            // formatting (fill/line/marker colors, data-label formats, and the verbatim
            // extra-errBars/trendline XML passthroughs) -- all keyed by SeriesIndex too. The same
            // applies to per-series custom "Series name" cell-reference overrides captured from a
            // <c:tx> formula (R103-io-chart-series-tx-1): the writer's ResolveSeriesTitleXml always
            // prefers a SeriesNameOverrides entry for the current SeriesIndex over the recomputed
            // header title, so a stale entry would silently attach the wrong custom name to
            // whichever series now sits at that index.
            _previousSeriesColumnMappings = chart.SeriesColumnMappings;
            _previousVerbatimSeriesFormulas = chart.VerbatimSeriesFormulas;
            _previousSeriesOrderOverrides = chart.SeriesOrderOverrides;
            _previousMultiLevelCategoryXml = chart.MultiLevelCategoryXml;
            _previousPointMarkerFormats = chart.PointMarkerFormats;
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
            _previousSeriesNameOverrides = chart.SeriesNameOverrides;
            _clearedMappingsForSourceChange = true;
            chart.SeriesColumnMappings = [];
            chart.VerbatimSeriesFormulas = null;
            chart.SeriesOrderOverrides = [];
            chart.MultiLevelCategoryXml = [];
            chart.PointMarkerFormats = [];
            chart.ExplodedSlices = [];
            chart.RangeDataLabels = [];
            chart.SeriesRangeDataLabels = [];
            chart.SecondaryAxisSeriesIndexes = [];
            chart.ComboLineSeriesIndexes = [];
            chart.ComboScatterSeriesIndexes = [];
            chart.TrendlineSeriesIndex = 0;
            chart.ErrorBarSeriesIndex = 0;
            chart.ShowLinearTrendline = false;
            chart.ShowErrorBars = false;
            chart.SeriesFormats = [];
            chart.PointFillColors = [];
            chart.SeriesDataLabelFormats = [];
            chart.PointDataLabelFormats = [];
            chart.AdditionalSeriesErrorBarsXml = [];
            chart.AdditionalSeriesTrendlinesXml = [];
            chart.SeriesPlotOrder = [];
            chart.LegendEntries = [];
            chart.SeriesNameOverrides = [];
        }

        chart.DataRange = _dataRange;
        chart.FirstRowIsHeader = nextFirstRowIsHeader;
        chart.FirstColIsCategories = nextFirstColIsCategories;
        chart.SeriesInRows = nextSeriesInRows;
        return new CommandOutcome(true, AffectedCells: [_dataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousDataRange is null || _previousFirstRowIsHeader is null || _previousFirstColIsCategories is null)
            return;

        if (!ChartCommandGuards.TryFindChart(ctx.GetSheet(_sheetId), _chartId, out var chart))
            return;

        chart.DataRange = _previousDataRange.Value;
        chart.FirstRowIsHeader = _previousFirstRowIsHeader.Value;
        chart.FirstColIsCategories = _previousFirstColIsCategories.Value;
        chart.SeriesInRows = _previousSeriesInRows ?? chart.SeriesInRows;
        if (_clearedMappingsForSourceChange)
        {
            chart.SeriesColumnMappings = _previousSeriesColumnMappings ?? [];
            chart.VerbatimSeriesFormulas = _previousVerbatimSeriesFormulas;
            chart.SeriesOrderOverrides = _previousSeriesOrderOverrides ?? [];
            chart.MultiLevelCategoryXml = _previousMultiLevelCategoryXml ?? [];
            chart.PointMarkerFormats = _previousPointMarkerFormats ?? [];
            chart.ExplodedSlices = _previousExplodedSlices ?? [];
            chart.RangeDataLabels = _previousRangeDataLabels ?? [];
            chart.SeriesRangeDataLabels = _previousSeriesRangeDataLabels ?? [];
            chart.SecondaryAxisSeriesIndexes = _previousSecondaryAxisSeriesIndexes ?? [];
            chart.ComboLineSeriesIndexes = _previousComboLineSeriesIndexes ?? [];
            chart.ComboScatterSeriesIndexes = _previousComboScatterSeriesIndexes ?? [];
            chart.TrendlineSeriesIndex = _previousTrendlineSeriesIndex ?? 0;
            chart.ErrorBarSeriesIndex = _previousErrorBarSeriesIndex ?? 0;
            chart.ShowLinearTrendline = _previousShowLinearTrendline ?? false;
            chart.ShowErrorBars = _previousShowErrorBars ?? false;
            chart.SeriesFormats = _previousSeriesFormats ?? [];
            chart.PointFillColors = _previousPointFillColors ?? [];
            chart.SeriesDataLabelFormats = _previousSeriesDataLabelFormats ?? [];
            chart.PointDataLabelFormats = _previousPointDataLabelFormats ?? [];
            chart.AdditionalSeriesErrorBarsXml = _previousAdditionalSeriesErrorBarsXml ?? [];
            chart.AdditionalSeriesTrendlinesXml = _previousAdditionalSeriesTrendlinesXml ?? [];
            chart.SeriesPlotOrder = _previousSeriesPlotOrder ?? [];
            chart.LegendEntries = _previousLegendEntries ?? [];
            chart.SeriesNameOverrides = _previousSeriesNameOverrides ?? [];
        }

        _previousDataRange = null;
        _previousFirstRowIsHeader = null;
        _previousFirstColIsCategories = null;
        _previousSeriesInRows = null;
        _previousSeriesColumnMappings = null;
        _previousVerbatimSeriesFormulas = null;
        _previousSeriesOrderOverrides = null;
        _previousMultiLevelCategoryXml = null;
        _previousPointMarkerFormats = null;
        _previousExplodedSlices = null;
        _previousRangeDataLabels = null;
        _previousSeriesRangeDataLabels = null;
        _previousSecondaryAxisSeriesIndexes = null;
        _previousComboLineSeriesIndexes = null;
        _previousComboScatterSeriesIndexes = null;
        _previousTrendlineSeriesIndex = null;
        _previousErrorBarSeriesIndex = null;
        _previousShowLinearTrendline = null;
        _previousShowErrorBars = null;
        _previousSeriesFormats = null;
        _previousPointFillColors = null;
        _previousSeriesDataLabelFormats = null;
        _previousPointDataLabelFormats = null;
        _previousAdditionalSeriesErrorBarsXml = null;
        _previousAdditionalSeriesTrendlinesXml = null;
        _previousSeriesPlotOrder = null;
        _previousLegendEntries = null;
        _previousSeriesNameOverrides = null;
        _clearedMappingsForSourceChange = false;
    }
}

