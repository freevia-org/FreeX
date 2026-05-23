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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } pivotProtectedOutcome)
            return pivotProtectedOutcome;

        var chart = sheet.Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
            return new CommandOutcome(false, "PivotChart was not found.");
        if (!chart.IsPivotChart || string.IsNullOrWhiteSpace(chart.PivotTableName))
            return new CommandOutcome(false, "Selected chart is not a PivotChart.");
        if (!ChartTypeSupport.IsRenderable(_chartType))
            return new CommandOutcome(false, "This chart family is recognized for XLSX preservation but cannot be authored yet.");

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

        var chart = ctx.GetSheet(_sheetId).Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        var chart = sheet.Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
            return new CommandOutcome(false, "Chart was not found.");

        _previousChartStyleId = chart.ChartStyleId;
        chart.ChartStyleId = _chartStyleId;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        var chart = ctx.GetSheet(_sheetId).Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        var chart = sheet.Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
            return new CommandOutcome(false, "Chart was not found.");
        if (chart.IsPivotChart)
            return new CommandOutcome(false, "Selected chart is a PivotChart.");
        if (!ChartTypeSupport.IsRenderable(_chartType))
            return new CommandOutcome(false, "This chart family is recognized for XLSX preservation but cannot be authored yet.");

        var firstColIsCategories = _chartType is not (ChartType.Scatter or ChartType.Bubble);
        if (!HasUsableChartData(_chartType, chart.DataRange, chart.FirstRowIsHeader, firstColIsCategories))
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

        var chart = ctx.GetSheet(_sheetId).Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
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
        bool firstColIsCategories)
    {
        var candidate = new ChartModel
        {
            Type = chartType,
            DataRange = dataRange,
            FirstRowIsHeader = firstRowIsHeader,
            FirstColIsCategories = firstColIsCategories
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
    private GridRange? _previousDataRange;
    private bool? _previousFirstRowIsHeader;
    private bool? _previousFirstColIsCategories;

    public string Label => "Select Chart Data";

    public ChangeChartSourceCommand(
        SheetId sheetId,
        Guid chartId,
        GridRange dataRange,
        bool? firstRowIsHeader = null,
        bool? firstColIsCategories = null)
    {
        _sheetId = sheetId;
        _chartId = chartId;
        _dataRange = dataRange;
        _firstRowIsHeader = firstRowIsHeader;
        _firstColIsCategories = firstColIsCategories;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        var chart = sheet.Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
            return new CommandOutcome(false, "Chart was not found.");
        if (chart.IsPivotChart)
            return new CommandOutcome(false, "Selected chart is a PivotChart.");
        if (_dataRange.Start.Sheet != _sheetId || _dataRange.End.Sheet != _sheetId)
            return new CommandOutcome(false, "Chart data range must be on the target sheet.");

        var nextFirstRowIsHeader = _firstRowIsHeader ?? chart.FirstRowIsHeader;
        var nextFirstColIsCategories = _firstColIsCategories ?? chart.FirstColIsCategories;
        if (!ChangeChartTypeCommand.HasUsableChartData(
                chart.Type,
                _dataRange,
                nextFirstRowIsHeader,
                nextFirstColIsCategories))
            return new CommandOutcome(false, "Chart data range must include at least one data series and one data point.");

        _previousDataRange = chart.DataRange;
        _previousFirstRowIsHeader = chart.FirstRowIsHeader;
        _previousFirstColIsCategories = chart.FirstColIsCategories;
        chart.DataRange = _dataRange;
        chart.FirstRowIsHeader = nextFirstRowIsHeader;
        chart.FirstColIsCategories = nextFirstColIsCategories;
        return new CommandOutcome(true, AffectedCells: [_dataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousDataRange is null || _previousFirstRowIsHeader is null || _previousFirstColIsCategories is null)
            return;

        var chart = ctx.GetSheet(_sheetId).Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
            return;

        chart.DataRange = _previousDataRange.Value;
        chart.FirstRowIsHeader = _previousFirstRowIsHeader.Value;
        chart.FirstColIsCategories = _previousFirstColIsCategories.Value;
        _previousDataRange = null;
        _previousFirstRowIsHeader = null;
        _previousFirstColIsCategories = null;
    }
}

public sealed class MoveChartCommand : IWorkbookCommand
{
    private readonly SheetId _sourceSheetId;
    private readonly Guid _chartId;
    private readonly SheetId _targetSheetId;
    private ChartModel? _movedChart;
    private bool _applied;

    public string Label => "Move Chart";

    public MoveChartCommand(SheetId sourceSheetId, Guid chartId, SheetId targetSheetId)
    {
        _sourceSheetId = sourceSheetId;
        _chartId = chartId;
        _targetSheetId = targetSheetId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var source = ctx.Workbook.GetSheet(_sourceSheetId);
        if (source is null)
            return new CommandOutcome(false, "Source sheet was not found.");
        if (CommandGuards.RejectIfProtectedWithoutPermission(source, SheetProtectionPermission.EditObjects) is { } sourceProtectedOutcome)
            return sourceProtectedOutcome;

        var target = ctx.Workbook.GetSheet(_targetSheetId);
        if (target is null)
            return new CommandOutcome(false, "Target sheet was not found.");
        if (CommandGuards.RejectIfProtectedWithoutPermission(target, SheetProtectionPermission.EditObjects) is { } targetProtectedOutcome)
            return targetProtectedOutcome;

        var chart = source.Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
            return new CommandOutcome(false, "Chart was not found.");
        if (chart.IsPivotChart)
            return new CommandOutcome(false, "Selected chart is a PivotChart.");
        if (_sourceSheetId == _targetSheetId)
            return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);

        source.Charts.Remove(chart);
        target.Charts.Add(chart);
        _movedChart = chart;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || _movedChart is null)
            return;

        var source = ctx.Workbook.GetSheet(_sourceSheetId);
        var target = ctx.Workbook.GetSheet(_targetSheetId);
        if (source is null || target is null)
            return;

        target.Charts.Remove(_movedChart);
        source.Charts.Add(_movedChart);
        _movedChart = null;
        _applied = false;
    }
}

public sealed class MoveChartToNewSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sourceSheetId;
    private readonly Guid _chartId;
    private readonly string _sheetName;
    private SheetId? _createdSheetId;
    private ChartModel? _movedChart;

    public string Label => "Move Chart";

    public MoveChartToNewSheetCommand(SheetId sourceSheetId, Guid chartId, string sheetName)
    {
        _sourceSheetId = sourceSheetId;
        _chartId = chartId;
        _sheetName = sheetName;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var validationError = ctx.Workbook.ValidateSheetName(_sheetName);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        var source = ctx.Workbook.GetSheet(_sourceSheetId);
        if (source is null)
            return new CommandOutcome(false, "Source sheet was not found.");
        if (CommandGuards.RejectIfProtectedWithoutPermission(source, SheetProtectionPermission.EditObjects) is { } sourceProtectedOutcome)
            return sourceProtectedOutcome;

        var chart = source.Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
            return new CommandOutcome(false, "Chart was not found.");
        if (chart.IsPivotChart)
            return new CommandOutcome(false, "Selected chart is a PivotChart.");

        var target = ctx.Workbook.AddSheet(_sheetName);
        source.Charts.Remove(chart);
        target.Charts.Add(chart);
        _createdSheetId = target.Id;
        _movedChart = chart;
        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_createdSheetId is null || _movedChart is null)
            return;

        var source = ctx.Workbook.GetSheet(_sourceSheetId);
        var target = ctx.Workbook.GetSheet(_createdSheetId.Value);
        if (source is not null && target is not null)
        {
            target.Charts.Remove(_movedChart);
            source.Charts.Add(_movedChart);
        }

        ctx.Workbook.RemoveSheet(_createdSheetId.Value);
        _createdSheetId = null;
        _movedChart = null;
    }
}
