using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class AddChartCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly ChartModel _chart;
    private bool _added;

    public string Label => "Insert Chart";

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
        if (!ChartTypeSupport.IsRenderable(_chart.Type))
            return new CommandOutcome(false, "This chart family is recognized for XLSX preservation but cannot be authored yet.");
        if (_chart.DataRange.Start.Sheet != _sheetId || _chart.DataRange.End.Sheet != _sheetId)
            return new CommandOutcome(false, "Chart data range must be on the target sheet.");
        if (!double.IsFinite(_chart.Width) || !double.IsFinite(_chart.Height) || _chart.Width <= 0 || _chart.Height <= 0)
            return new CommandOutcome(false, "Chart size must be positive.");
        if (ChartTypeSupport.GetDataSeriesCount(_chart) <= 0)
            return new CommandOutcome(false, "Chart data range must include at least one data series.");
        if (ChartTypeSupport.GetDataPointCount(_chart) <= 0)
            return new CommandOutcome(false, "Chart data range must include at least one data point.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
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
        if (!ChartTypeSupport.IsRenderable(_chartType))
            return new CommandOutcome(false, "This chart family is recognized for XLSX preservation but cannot be authored yet.");
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
            return new CommandOutcome(false, "Chart data range must include at least one data series.");
        if (ChartTypeSupport.GetDataPointCount(candidate) <= 0)
            return new CommandOutcome(false, "Chart data range must include at least one data point.");

        var target = ctx.Workbook.AddSheet(GetUniqueChartSheetName(ctx.Workbook));
        target.Charts.Add(candidate);
        _createdSheetId = target.Id;
        return new CommandOutcome(true, AffectedCells: [_dataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_createdSheetId is null)
            return;

        ctx.Workbook.RemoveSheet(_createdSheetId.Value);
        _createdSheetId = null;
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
            return new CommandOutcome(false, "PivotTable name is required.");
        if (!ChartTypeSupport.IsRenderable(_chartType))
            return new CommandOutcome(false, "This chart family is recognized for XLSX preservation but cannot be authored yet.");
        if (!double.IsFinite(_width) || !double.IsFinite(_height) || _width <= 0 || _height <= 0)
            return new CommandOutcome(false, "Chart size must be positive.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } pivotProtectedOutcome)
            return pivotProtectedOutcome;

        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

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

        ctx.GetSheet(_sheetId).Charts.Remove(_addedChart);
        _addedChart = null;
    }
}
