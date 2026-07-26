using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartPieOptionsSurfacePlan(
    string CommandId,
    string Title,
    string FirstSliceAngleLabel,
    string DoughnutHoleLabel,
    string Hint,
    string OkLabel,
    string CancelLabel);

/// <summary>Working-copy planner for the modeled pie/doughnut rotation and hole settings.</summary>
public sealed class ChartPieOptionsPlanner
{
    public const string CommandId = "freep.chart.pie-options";
    public const string DialogTitle = "Pie/Doughnut Options";
    public const string FirstSliceAngleLabel = "First slice angle (degrees)";
    public const string DoughnutHoleLabel = "Doughnut hole (%)";
    public const string Hint = "Angle accepts 0-359. Hole size applies to doughnut charts.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 430;
    public const double DefaultDialogHeight = 250;

    private int? _firstSliceAngleDegrees;
    private int _doughnutHolePercent;

    private ChartPieOptionsPlanner(ChartShape chart)
    {
        _firstSliceAngleDegrees = chart.FirstSliceAngleDegrees;
        _doughnutHolePercent = Math.Clamp(chart.DoughnutHolePercent, 10, 90);
    }

    public static ChartPieOptionsSurfacePlan BuildSurfacePlan() => new(
        CommandId,
        DialogTitle,
        FirstSliceAngleLabel,
        DoughnutHoleLabel,
        Hint,
        OkLabel,
        CancelLabel);

    public static ChartPieOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (chart.ChartType is not (ChartType.Pie or ChartType.Doughnut))
            throw new InvalidOperationException("Select a pie or doughnut chart before editing pie options.");
        return new ChartPieOptionsPlanner(chart);
    }

    public int? FirstSliceAngleDegrees => _firstSliceAngleDegrees;
    public int DoughnutHolePercent => _doughnutHolePercent;

    public void SetFirstSliceAngleDegrees(int? value) =>
        _firstSliceAngleDegrees = value is null ? null : Math.Clamp(value.Value, 0, 359);

    public void SetDoughnutHolePercent(int value) =>
        _doughnutHolePercent = Math.Clamp(value, 10, 90);

    public ChartPieOptions BuildCommitPlan() => new(
        _firstSliceAngleDegrees,
        _doughnutHolePercent);
}
