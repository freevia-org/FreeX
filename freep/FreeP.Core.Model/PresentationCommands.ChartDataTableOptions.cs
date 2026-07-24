namespace FreeP.Core.Model;

/// <summary>Atomically updates chart data-table visibility and border options.</summary>
public sealed class SetChartDataTableOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartDataTableOptions _newOptions;
    private ChartDataTableSettings? _oldDataTable;

    public SetChartDataTableOptionsCommand(int slideIndex, uint shapeId, ChartDataTableOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Chart Data Table Options";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null)
            return;

        _oldDataTable = Clone(chart.DataTable);
        chart.DataTable = _newOptions.ShowDataTable
            ? new ChartDataTableSettings
            {
                ShowHorizontalBorder = _newOptions.ShowHorizontalBorder,
                ShowVerticalBorder = _newOptions.ShowVerticalBorder,
                ShowOutlineBorder = _newOptions.ShowOutlineBorder,
                ShowLegendKeys = _newOptions.ShowLegendKeys,
                BackgroundFill = _oldDataTable?.BackgroundFill,
                BorderOutline = _oldDataTable?.BorderOutline,
                TextStyle = _oldDataTable?.TextStyle,
            }
            : null;

        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null)
            return;

        chart.DataTable = Clone(_oldDataTable);
        ChartHelper.MarkWorkbookDirty(chart);
    }

    private static ChartDataTableSettings? Clone(ChartDataTableSettings? source) => source is null
        ? null
        : new ChartDataTableSettings
        {
            ShowHorizontalBorder = source.ShowHorizontalBorder,
            ShowVerticalBorder = source.ShowVerticalBorder,
            ShowOutlineBorder = source.ShowOutlineBorder,
            ShowLegendKeys = source.ShowLegendKeys,
            BackgroundFill = source.BackgroundFill,
            BorderOutline = source.BorderOutline,
            TextStyle = source.TextStyle,
        };
}
