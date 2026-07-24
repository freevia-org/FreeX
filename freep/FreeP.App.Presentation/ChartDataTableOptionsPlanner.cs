using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartDataTableOptionsSurfacePlan(
    string CommandId,
    string Title,
    string ShowDataTableLabel,
    string HorizontalBorderLabel,
    string VerticalBorderLabel,
    string OutlineBorderLabel,
    string LegendKeysLabel,
    string OkLabel,
    string CancelLabel);

/// <summary>Working-copy planner for chart data-table authoring options.</summary>
public sealed class ChartDataTableOptionsPlanner
{
    public const string CommandId = "freep.chart.data-table-options";
    public const string DialogTitle = "Chart Data Table Options";
    public const string ShowDataTableLabel = "Show data table";
    public const string HorizontalBorderLabel = "Horizontal borders";
    public const string VerticalBorderLabel = "Vertical borders";
    public const string OutlineBorderLabel = "Outline border";
    public const string LegendKeysLabel = "Legend keys";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 380;
    public const double DefaultDialogHeight = 300;

    private bool _showDataTable;
    private bool _showHorizontalBorder = true;
    private bool _showVerticalBorder = true;
    private bool _showOutlineBorder = true;
    private bool _showLegendKeys;

    private ChartDataTableOptionsPlanner(ChartShape chart)
    {
        _showDataTable = chart.DataTable is not null;
        if (chart.DataTable is { } dataTable)
        {
            _showHorizontalBorder = dataTable.ShowHorizontalBorder;
            _showVerticalBorder = dataTable.ShowVerticalBorder;
            _showOutlineBorder = dataTable.ShowOutlineBorder;
            _showLegendKeys = dataTable.ShowLegendKeys;
        }
    }

    public static ChartDataTableOptionsSurfacePlan BuildSurfacePlan() => new(
        CommandId,
        DialogTitle,
        ShowDataTableLabel,
        HorizontalBorderLabel,
        VerticalBorderLabel,
        OutlineBorderLabel,
        LegendKeysLabel,
        OkLabel,
        CancelLabel);

    public static ChartDataTableOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartDataTableOptionsPlanner(chart);
    }

    public bool ShowDataTable => _showDataTable;
    public bool ShowHorizontalBorder => _showHorizontalBorder;
    public bool ShowVerticalBorder => _showVerticalBorder;
    public bool ShowOutlineBorder => _showOutlineBorder;
    public bool ShowLegendKeys => _showLegendKeys;

    public void SetShowDataTable(bool value) => _showDataTable = value;
    public void SetShowHorizontalBorder(bool value) => _showHorizontalBorder = value;
    public void SetShowVerticalBorder(bool value) => _showVerticalBorder = value;
    public void SetShowOutlineBorder(bool value) => _showOutlineBorder = value;
    public void SetShowLegendKeys(bool value) => _showLegendKeys = value;

    public ChartDataTableOptions BuildCommitPlan() => new(
        _showDataTable,
        _showHorizontalBorder,
        _showVerticalBorder,
        _showOutlineBorder,
        _showLegendKeys);
}
