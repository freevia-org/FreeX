using System.Diagnostics.CodeAnalysis;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class AddChartCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly ChartModel _chart;
    private bool _added;

    public string Label => "Insert Chart";
    public Guid ChartId => _chart.Id;

    public AddChartCommand(
        SheetId sheetId,
        GridRange dataRange,
        ChartType type,
        string? title = null,
        double left = 20,
        double top = 20,
        double width = 400,
        double height = 300)
    {
        _sheetId = sheetId;
        var chartType = ValidEnumOrDefault(type, ChartType.Column);
        _chart = new ChartModel
        {
            Type = chartType,
            DataRange = dataRange,
            FirstColIsCategories = chartType is not (ChartType.Scatter or ChartType.Bubble),
            Title = title,
            Left = left,
            Top = top,
            Width = width,
            Height = height
        };
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (ChartAuthoringPlanner.RejectIfUnsupported(_chart.Type) is { } unsupportedOutcome)
            return unsupportedOutcome;
        if (_chart.DataRange.Start.Sheet != _sheetId || _chart.DataRange.End.Sheet != _sheetId)
            return ChartCommandGuards.ChartDataRangeOnTargetSheet();
        if (ChartCommandGuards.RejectInvalidSize(_chart.Width, _chart.Height) is { } invalidSize)
            return invalidSize;
        if (ChartTypeSupport.GetDataSeriesCount(_chart) <= 0)
            return ChartCommandGuards.ChartDataRangeRequiresDataSeries();
        if (ChartTypeSupport.GetDataPointCount(_chart) <= 0)
            return ChartCommandGuards.ChartDataRangeRequiresDataPoint();

        var sheet = ctx.GetSheet(_sheetId);
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        sheet.Charts.Add(_chart);
        _added = true;
        return new CommandOutcome(true, AffectedCells: [_chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_added)
            return;

        ctx.GetSheet(_sheetId).Charts.Remove(_chart);
        _added = false;
    }

    private static TEnum ValidEnumOrDefault<TEnum>(TEnum value, TEnum defaultValue)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value) ? value : defaultValue;
}

public sealed class AddChartSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sourceSheetId;
    private readonly GridRange _dataRange;
    private readonly ChartType _chartType;
    private readonly string? _title;
    private SheetId? _createdSheetId;

    public string Label => "Insert Chart Sheet";
    public SheetId? CreatedSheetId => _createdSheetId;

    public AddChartSheetCommand(
        SheetId sourceSheetId,
        GridRange dataRange,
        ChartType chartType,
        string? title = null)
    {
        _sourceSheetId = sourceSheetId;
        _dataRange = dataRange;
        _chartType = Enum.IsDefined(chartType) ? chartType : ChartType.Column;
        _title = title;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;
        if (ChartAuthoringPlanner.RejectIfUnsupported(_chartType) is { } unsupportedOutcome)
            return unsupportedOutcome;
        if (_dataRange.Start.Sheet != _sourceSheetId || _dataRange.End.Sheet != _sourceSheetId)
            return new CommandOutcome(false, "Chart data range must be on the source sheet.");

        var candidate = new ChartModel
        {
            Type = _chartType,
            DataRange = _dataRange,
            FirstColIsCategories = _chartType is not (ChartType.Scatter or ChartType.Bubble),
            Title = _title
        };
        if (ChartTypeSupport.GetDataSeriesCount(candidate) <= 0)
            return ChartCommandGuards.ChartDataRangeRequiresDataSeries();
        if (ChartTypeSupport.GetDataPointCount(candidate) <= 0)
            return ChartCommandGuards.ChartDataRangeRequiresDataPoint();

        Sheet target;
        if (_createdSheetId is { } existingSheetId)
        {
            // R17: redo. Workbook.AddSheet always mints a brand-new SheetId, which would give
            // the re-created chart sheet a DIFFERENT id than the first Apply produced --
            // breaking any later redo-stack command that captured the original id. Re-create
            // with the SAME id captured below instead, via the "reinsert an existing sheet
            // instance" overload (mirrors AddSheetCommand's R16 redo fix).
            target = new Sheet(existingSheetId, GetUniqueChartSheetName(ctx.Workbook));
            ctx.Workbook.InsertSheet(ctx.Workbook.Sheets.Count, target);
        }
        else
        {
            target = ctx.Workbook.AddSheet(GetUniqueChartSheetName(ctx.Workbook));
            _createdSheetId = target.Id;
        }
        target.ResetViewStateToA1();
        target.Charts.Add(candidate);
        return new CommandOutcome(true, AffectedCells: [_dataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_createdSheetId is null)
            return;

        ctx.Workbook.RemoveSheet(_createdSheetId.Value);
    }

    private static string GetUniqueChartSheetName(Workbook workbook)
    {
        for (var i = 1; ; i++)
        {
            var candidate = $"Chart{i}";
            if (workbook.ValidateSheetName(candidate) is null)
                return candidate;
        }
    }
}

public sealed class AddPivotChartCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly ChartType _chartType;
    private readonly string? _title;
    private readonly double _left;
    private readonly double _top;
    private readonly double _width;
    private readonly double _height;
    private ChartModel? _addedChart;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;
    private GridRange? _lastRenderedRangeSnapshot;

    public string Label => "Insert PivotChart";

    public AddPivotChartCommand(
        SheetId sheetId,
        string pivotTableName,
        ChartType chartType,
        string? title = null,
        double left = 20,
        double top = 20,
        double width = 400,
        double height = 300)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _chartType = Enum.IsDefined(chartType) ? chartType : ChartType.Column;
        _title = title;
        _left = left;
        _top = top;
        _width = width;
        _height = height;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(_pivotTableName))
            return CommandGuards.RejectPivotTableNameRequired();
        if (ChartAuthoringPlanner.RejectIfUnsupported(_chartType) is { } unsupportedOutcome)
            return unsupportedOutcome;
        if (ChartCommandGuards.RejectInvalidSize(_width, _height) is { } invalidSize)
            return invalidSize;

        var sheet = ctx.GetSheet(_sheetId);
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } pivotProtectedOutcome)
            return pivotProtectedOutcome;

        if (!CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
            return CommandGuards.RejectPivotTableNotFound();

        // Refresh mutates the sheet's pivot-rendered cells as a side effect of inserting the
        // chart (it reads live source data, not a frozen cache). Snapshot the pre-refresh
        // state here -- mirroring every sibling pivot-editing command (e.g.
        // RefreshPivotTableCommand, ConfigurePivotTableLayoutCommand) -- so Revert can restore
        // it, not just remove the chart.
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.LastRenderedRange ?? pivotTable.TargetRange);
        _lastRenderedRangeSnapshot = pivotTable.LastRenderedRange;
        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        var dataRange = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivotTable);
        var chart = new ChartModel
        {
            Type = _chartType,
            DataRange = dataRange,
            FirstColIsCategories = _chartType is not (ChartType.Scatter or ChartType.Bubble),
            IsPivotChart = true,
            PivotTableName = pivotTable.Name,
            PivotCacheId = pivotTable.CacheId,
            Title = _title,
            Left = _left,
            Top = _top,
            Width = _width,
            Height = _height
        };

        if (ChartTypeSupport.GetDataSeriesCount(chart) <= 0)
            return new CommandOutcome(false, "PivotChart source must include at least one data series.");
        if (ChartTypeSupport.GetDataPointCount(chart) <= 0)
            return new CommandOutcome(false, "PivotChart source must include at least one data point.");

        sheet.Charts.Add(chart);
        _addedChart = chart;
        return new CommandOutcome(true, AffectedCells: [dataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_addedChart is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        sheet.Charts.Remove(_addedChart);
        _addedChart = null;

        if (_targetSnapshot is not null)
        {
            if (CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
            {
                PivotTableRefreshService.ClearRenderedRange(sheet, pivotTable.LastRenderedRange);
                pivotTable.LastRenderedRange = _lastRenderedRangeSnapshot;
            }
            AddPivotTableCommand.Restore(sheet, _targetSnapshot);
            _targetSnapshot = null;
            _lastRenderedRangeSnapshot = null;
        }
    }
}

internal static class ChartCommandGuards
{
    private const string ChartNotFoundMessage = "Chart was not found.";
    private const string PivotChartNotFoundMessage = "PivotChart was not found.";
    private const string InvalidChartSizeMessage = "Chart size must be positive.";
    private const string SelectedChartIsPivotChartMessage = "Selected chart is a PivotChart.";
    private const string SelectedChartIsNotPivotChartMessage = "Selected chart is not a PivotChart.";
    private const string ChartDataRangeOnTargetSheetMessage = "Chart data range must be on the target sheet.";
    private const string ChartDataRangeRequiresDataSeriesMessage = "Chart data range must include at least one data series.";
    private const string ChartDataRangeRequiresDataPointMessage = "Chart data range must include at least one data point.";

    public static CommandOutcome ChartNotFound() =>
        new(false, ChartNotFoundMessage);

    public static CommandOutcome PivotChartNotFound() =>
        new(false, PivotChartNotFoundMessage);

    public static bool TryFindChart(
        Sheet sheet,
        Guid chartId,
        [NotNullWhen(true)] out ChartModel? chart)
    {
        foreach (var item in sheet.Charts)
        {
            if (item.Id != chartId)
                continue;

            chart = item;
            return true;
        }

        chart = null;
        return false;
    }

    public static CommandOutcome SelectedChartIsPivotChart() =>
        new(false, SelectedChartIsPivotChartMessage);

    public static CommandOutcome SelectedChartIsNotPivotChart() =>
        new(false, SelectedChartIsNotPivotChartMessage);

    public static CommandOutcome ChartDataRangeOnTargetSheet() =>
        new(false, ChartDataRangeOnTargetSheetMessage);

    public static CommandOutcome ChartDataRangeRequiresDataSeries() =>
        new(false, ChartDataRangeRequiresDataSeriesMessage);

    public static CommandOutcome ChartDataRangeRequiresDataPoint() =>
        new(false, ChartDataRangeRequiresDataPointMessage);

    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);

    /// <summary>
    /// R111-model-drawing-object-lock-1-1: same sheet-level "Edit objects" protection check as
    /// <see cref="RejectIfEditObjectsBlocked(Sheet)"/>, but layers in the per-chart
    /// <see cref="ChartModel.Locked"/> flag -- mirrors
    /// <see cref="DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(Sheet, DrawingShapeModel)"/>: an
    /// author-unlocked chart (<c>Locked == false</c>) stays movable/resizable even while the sheet is
    /// protected with "Edit objects" blocked, matching Excel's per-object Locked checkbox. A locked
    /// chart (the default) is rejected exactly like the sheet-only overload.
    /// </summary>
    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet, ChartModel chart) =>
        chart.Locked ? RejectIfEditObjectsBlocked(sheet) : null;

    public static CommandOutcome? RejectInvalidSize(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0
            ? null
            : new CommandOutcome(false, InvalidChartSizeMessage);
}
