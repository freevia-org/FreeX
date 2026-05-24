using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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

