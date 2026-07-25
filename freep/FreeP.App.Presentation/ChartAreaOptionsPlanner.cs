using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartAreaFormattingTargetOption(ChartAreaFormattingTarget Value, string Label);

public sealed record ChartAreaOptionsSurfacePlan(
    string CommandId,
    string Title,
    string TargetLabel,
    string FillLabel,
    string NoFillLabel,
    string OutlineLabel,
    string NoOutlineLabel,
    string WidthLabel,
    string Hint,
    string OkLabel,
    string CancelLabel);

/// <summary>Working-copy planner for PowerPoint chart-area and plot-area formatting.</summary>
public sealed class ChartAreaOptionsPlanner
{
    public const string CommandId = "freep.chart.area-options";
    public const string DialogTitle = "Chart Area Options";
    public const string TargetLabel = "Apply to";
    public const string FillLabel = "Fill color (#RRGGBB)";
    public const string NoFillLabel = "No fill";
    public const string OutlineLabel = "Outline color (#RRGGBB)";
    public const string NoOutlineLabel = "No outline";
    public const string WidthLabel = "Outline width (pt)";
    public const string Hint = "Blank fill and outline values restore the chart or theme default.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";

    public static IReadOnlyList<ChartAreaFormattingTargetOption> TargetOptions { get; } =
    [
        new(ChartAreaFormattingTarget.ChartArea, "Chart area"),
        new(ChartAreaFormattingTarget.PlotArea, "Plot area"),
    ];

    private readonly ChartShape _chart;
    private ChartAreaFormattingTarget _target = ChartAreaFormattingTarget.ChartArea;
    private string _fillColor = string.Empty;
    private bool _noFill;
    private string _outlineColor = string.Empty;
    private bool _noOutline;
    private double? _outlineWidthPt;

    private ChartAreaOptionsPlanner(ChartShape chart)
    {
        _chart = chart;
        LoadTarget();
    }

    public static ChartAreaOptionsSurfacePlan BuildSurfacePlan() => new(
        CommandId, DialogTitle, TargetLabel, FillLabel, NoFillLabel, OutlineLabel, NoOutlineLabel, WidthLabel,
        Hint, OkLabel, CancelLabel);

    public static ChartAreaOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartAreaOptionsPlanner(chart);
    }

    public ChartAreaFormattingTarget Target => _target;
    public string FillColor => _fillColor;
    public bool NoFill => _noFill;
    public string OutlineColor => _outlineColor;
    public bool NoOutline => _noOutline;
    public double? OutlineWidthPt => _outlineWidthPt;

    public void SetTarget(ChartAreaFormattingTarget target)
    {
        _target = target;
        LoadTarget();
    }

    public void SetFillColor(string? value)
    {
        _fillColor = value?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_fillColor))
            _noFill = false;
    }
    public void SetOutlineColor(string? value) => _outlineColor = value?.Trim() ?? string.Empty;
    public void SetNoFill(bool value) => _noFill = value;
    public void SetNoOutline(bool value) => _noOutline = value;
    public void SetOutlineWidth(double? value) =>
        _outlineWidthPt = value is { } number && double.IsFinite(number) && number > 0 ? number : null;

    public ChartAreaOptions BuildCommitPlan()
    {
        ShapeFill? fill = null;
        if (_noFill)
            fill = ShapeFill.None.Instance;
        else if (!string.IsNullOrWhiteSpace(_fillColor))
            fill = new ShapeFill.Solid(ChartPointOptionsPlanner.ParseColor(_fillColor, FillLabel)!);

        ShapeOutline? outline = null;
        if (_noOutline)
            outline = ShapeOutline.None.Instance;
        else if (!string.IsNullOrWhiteSpace(_outlineColor))
            outline = new ShapeOutline.Visible(
                ChartPointOptionsPlanner.ParseColor(_outlineColor, OutlineLabel)!,
                _outlineWidthPt ?? 0.75);
        return new ChartAreaOptions(_target, fill, outline);
    }

    private void LoadTarget()
    {
        var fill = _target == ChartAreaFormattingTarget.ChartArea ? _chart.ChartAreaFill : _chart.PlotAreaFill;
        var outline = _target == ChartAreaFormattingTarget.ChartArea ? _chart.ChartAreaOutline : _chart.PlotAreaOutline;
        _noFill = fill is ShapeFill.None;
        _fillColor = fill is ShapeFill.Solid solid ? solid.Color.Resolved.ToString() : string.Empty;
        _noOutline = outline is ShapeOutline.None;
        if (outline is ShapeOutline.Visible visible)
        {
            _outlineColor = visible.Color.Resolved.ToString();
            _outlineWidthPt = visible.WidthPt;
        }
        else
        {
            _outlineColor = string.Empty;
            _outlineWidthPt = null;
        }
    }
}
