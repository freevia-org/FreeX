using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartLayoutTargetOption(ChartLayoutTarget Value, string Label);
public sealed record ChartLayoutTargetSemanticOption(string? Value, string Label);
public sealed record ChartLayoutModeOption(ChartManualLayoutMode Value, string Label);

public sealed record ChartLayoutOptionsSurfacePlan(
    string CommandId,
    string Title,
    string TargetLabel,
    string LayoutTargetLabel,
    string XModeLabel,
    string YModeLabel,
    string WidthModeLabel,
    string HeightModeLabel,
    string XLabel,
    string YLabel,
    string WidthLabel,
    string HeightLabel,
    string Hint,
    string OkLabel,
    string CancelLabel);

/// <summary>Working-copy planner for plot-area and legend manual layouts.</summary>
public sealed class ChartLayoutOptionsPlanner
{
    public const string CommandId = "freep.chart.layout-options";
    public const string DialogTitle = "Chart Layout Options";
    public const string TargetLabel = "Target";
    public const string LayoutTargetLabel = "Layout target";
    public const string XModeLabel = "X mode";
    public const string YModeLabel = "Y mode";
    public const string WidthModeLabel = "Width mode";
    public const string HeightModeLabel = "Height mode";
    public const string XLabel = "X";
    public const string YLabel = "Y";
    public const string WidthLabel = "Width";
    public const string HeightLabel = "Height";
    public const string Hint = "Factor values are relative to the selected chart area; edge mode uses the OOXML edge coordinate semantics.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 520;
    public const double DefaultDialogHeight = 520;

    public static IReadOnlyList<ChartLayoutTargetOption> TargetOptions { get; } =
    [
        new(ChartLayoutTarget.PlotArea, "Plot area"),
        new(ChartLayoutTarget.Legend, "Legend"),
    ];

    public static IReadOnlyList<ChartLayoutModeOption> ModeOptions { get; } =
    [
        new(ChartManualLayoutMode.Factor, "Factor"),
        new(ChartManualLayoutMode.Edge, "Edge"),
    ];

    public static IReadOnlyList<ChartLayoutTargetSemanticOption> LayoutTargetOptionsFor(string? currentValue)
    {
        var options = new List<ChartLayoutTargetSemanticOption>
        {
            new(null, "Automatic (outer)"),
            new("inner", "Inner"),
            new("outer", "Outer"),
        };

        if (!string.IsNullOrWhiteSpace(currentValue) &&
            !options.Any(option => string.Equals(option.Value, currentValue, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new(currentValue, $"Imported ({currentValue})"));
        }

        return options;
    }

    private readonly ChartShape _chart;
    private ChartLayoutTarget _target;
    private string? _layoutTarget;
    private ChartManualLayoutMode _xMode;
    private ChartManualLayoutMode _yMode;
    private ChartManualLayoutMode _widthMode;
    private ChartManualLayoutMode _heightMode;
    private double? _x;
    private double? _y;
    private double? _width;
    private double? _height;

    private ChartLayoutOptionsPlanner(ChartShape chart)
    {
        _chart = chart;
        SetTarget(ChartLayoutTarget.PlotArea);
    }

    public static ChartLayoutOptionsSurfacePlan BuildSurfacePlan() => new(
        CommandId, DialogTitle, TargetLabel, LayoutTargetLabel, XModeLabel, YModeLabel,
        WidthModeLabel, HeightModeLabel, XLabel, YLabel, WidthLabel, HeightLabel,
        Hint, OkLabel, CancelLabel);

    public static ChartLayoutOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartLayoutOptionsPlanner(chart);
    }

    public ChartLayoutTarget Target => _target;
    public string? LayoutTarget => _layoutTarget;
    public ChartManualLayoutMode XMode => _xMode;
    public ChartManualLayoutMode YMode => _yMode;
    public ChartManualLayoutMode WidthMode => _widthMode;
    public ChartManualLayoutMode HeightMode => _heightMode;
    public double? X => _x;
    public double? Y => _y;
    public double? Width => _width;
    public double? Height => _height;

    public void SetTarget(ChartLayoutTarget target)
    {
        _target = target;
        var layout = target == ChartLayoutTarget.PlotArea
            ? _chart.PlotAreaManualLayout
            : _chart.LegendManualLayout;
        _layoutTarget = layout?.LayoutTarget;
        _xMode = NormalizeMode(layout?.XMode);
        _yMode = NormalizeMode(layout?.YMode);
        _widthMode = NormalizeMode(layout?.WidthMode);
        _heightMode = NormalizeMode(layout?.HeightMode);
        _x = layout?.X;
        _y = layout?.Y;
        _width = layout?.Width;
        _height = layout?.Height;
    }

    public void SetLayoutTarget(string? value) => _layoutTarget = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public void SetXMode(ChartManualLayoutMode value) => _xMode = NormalizeMode(value);
    public void SetYMode(ChartManualLayoutMode value) => _yMode = NormalizeMode(value);
    public void SetWidthMode(ChartManualLayoutMode value) => _widthMode = NormalizeMode(value);
    public void SetHeightMode(ChartManualLayoutMode value) => _heightMode = NormalizeMode(value);
    public void SetX(double? value) => _x = value;
    public void SetY(double? value) => _y = value;
    public void SetWidth(double? value) => _width = value;
    public void SetHeight(double? value) => _height = value;

    public ChartLayoutOptions BuildCommitPlan() => new(
        _target, _layoutTarget, _xMode, _yMode, _widthMode, _heightMode,
        _x, _y, _width, _height);

    private static ChartManualLayoutMode NormalizeMode(ChartManualLayoutMode? mode) =>
        mode is ChartManualLayoutMode.Edge ? ChartManualLayoutMode.Edge : ChartManualLayoutMode.Factor;
}
