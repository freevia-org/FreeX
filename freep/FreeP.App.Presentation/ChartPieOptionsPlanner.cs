using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartPieOptionsSurfacePlan(
    string CommandId,
    string Title,
    string FirstSliceAngleLabel,
    string DoughnutHoleLabel,
    string OfPieTypeLabel,
    string OfPieSplitTypeLabel,
    string OfPieSplitPositionLabel,
    string OfPieSecondPieSizeLabel,
    string OfPieCustomPointIndicesLabel,
    string Hint,
    string OkLabel,
    string CancelLabel);

/// <summary>Working-copy planner for pie, doughnut, and pie-of-pie settings.</summary>
public sealed class ChartPieOptionsPlanner
{
    public const string CommandId = "freep.chart.pie-options";
    public const string DialogTitle = "Pie/Doughnut/OfPie Options";
    public const string FirstSliceAngleLabel = "First slice angle (degrees)";
    public const string DoughnutHoleLabel = "Doughnut hole (%)";
    public const string OfPieTypeLabel = "Secondary plot";
    public const string OfPieSplitTypeLabel = "Split rule";
    public const string OfPieSplitPositionLabel = "Split position / threshold";
    public const string OfPieSecondPieSizeLabel = "Secondary plot size (%)";
    public const string OfPieCustomPointIndicesLabel = "Custom secondary points (0-based, comma separated)";
    public const string Hint = "Angle accepts 0-359. OfPie values apply only to pie-of-pie/bar-of-pie charts.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 560;
    public const double DefaultDialogHeight = 430;

    private readonly ChartShape _chart;
    private int? _firstSliceAngleDegrees;
    private int _doughnutHolePercent;
    private OfPieType _ofPieType;
    private OfPieSplitType _ofPieSplitType;
    private double? _ofPieSplitPosition;
    private int _ofPieSecondPieSizePercent;
    private List<int> _ofPieCustomPointIndices;

    private ChartPieOptionsPlanner(ChartShape chart)
    {
        _chart = chart;
        _firstSliceAngleDegrees = chart.FirstSliceAngleDegrees;
        _doughnutHolePercent = Math.Clamp(chart.DoughnutHolePercent, 10, 90);
        _ofPieType = chart.OfPieType;
        _ofPieSplitType = chart.OfPieSplitType ?? OfPieSplitType.Auto;
        _ofPieSplitPosition = chart.OfPieSplitPosition;
        _ofPieSecondPieSizePercent = Math.Clamp(chart.OfPieSecondPieSizePercent ?? 100, 5, 200);
        _ofPieCustomPointIndices = chart.OfPieCustomPointIndices.ToList();
    }

    public static ChartPieOptionsSurfacePlan BuildSurfacePlan() => new(
        CommandId,
        DialogTitle,
        FirstSliceAngleLabel,
        DoughnutHoleLabel,
        OfPieTypeLabel,
        OfPieSplitTypeLabel,
        OfPieSplitPositionLabel,
        OfPieSecondPieSizeLabel,
        OfPieCustomPointIndicesLabel,
        Hint,
        OkLabel,
        CancelLabel);

    public static ChartPieOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (chart.ChartType is not (ChartType.Pie or ChartType.Doughnut or ChartType.OfPie))
            throw new InvalidOperationException("Select a pie, doughnut, or pie-of-pie chart before editing pie options.");
        return new ChartPieOptionsPlanner(chart);
    }

    public int? FirstSliceAngleDegrees => _firstSliceAngleDegrees;
    public int DoughnutHolePercent => _doughnutHolePercent;
    public OfPieType OfPieType => _ofPieType;
    public OfPieSplitType OfPieSplitType => _ofPieSplitType;
    public double? OfPieSplitPosition => _ofPieSplitPosition;
    public int OfPieSecondPieSizePercent => _ofPieSecondPieSizePercent;
    public IReadOnlyList<int> OfPieCustomPointIndices => _ofPieCustomPointIndices;

    public void SetFirstSliceAngleDegrees(int? value) =>
        _firstSliceAngleDegrees = value is null ? null : Math.Clamp(value.Value, 0, 359);

    public void SetDoughnutHolePercent(int value) =>
        _doughnutHolePercent = Math.Clamp(value, 10, 90);

    public void SetOfPieType(OfPieType value) => _ofPieType = value;

    public void SetOfPieSplitType(OfPieSplitType value) => _ofPieSplitType = value;

    public void SetOfPieSplitPosition(double? value) =>
        _ofPieSplitPosition = value is null ? null : Math.Max(0, value.Value);

    public void SetOfPieSecondPieSizePercent(int value) =>
        _ofPieSecondPieSizePercent = Math.Clamp(value, 5, 200);

    public void SetOfPieCustomPointIndices(IEnumerable<int>? values)
    {
        int pointCount = _chart.Series.FirstOrDefault()?.Values.Count ?? _chart.Categories.Count;
        _ofPieCustomPointIndices = (values ?? Array.Empty<int>())
            .Where(index => index >= 0 && index < pointCount)
            .Distinct()
            .OrderBy(index => index)
            .ToList();
    }

    public ChartPieOptions BuildCommitPlan() => new(
        _firstSliceAngleDegrees,
        _doughnutHolePercent,
        _chart.ChartType == ChartType.OfPie ? _ofPieType : null,
        _chart.ChartType == ChartType.OfPie ? _ofPieSplitType : null,
        _chart.ChartType == ChartType.OfPie ? _ofPieSplitPosition : null,
        _chart.ChartType == ChartType.OfPie ? _ofPieSecondPieSizePercent : null,
        _chart.ChartType == ChartType.OfPie ? _ofPieCustomPointIndices.ToArray() : null);
}
