using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartAxisKindOption(ChartAxisKind Value, string Label);

public sealed record ChartAxisOptionsSurfacePlan(
    string CommandId,
    string Title,
    string AxisLabel,
    string CategoryAxisLabel,
    string ValueAxisLabel,
    string AxisTitleLabel,
    string MinimumLabel,
    string MaximumLabel,
    string MajorUnitLabel,
    string MinorUnitLabel,
    string NumberFormatLabel,
    string MajorGridlinesLabel,
    string AutoHint,
    string OkLabel,
    string CancelLabel);

/// <summary>
/// Working-copy planner for the chart axis scale and display controls already supported by the
/// chart model, renderer, and OOXML reader/writer.
/// </summary>
public sealed class ChartAxisOptionsPlanner
{
    public const string CommandId = "freep.chart.axis-options";
    public const string DialogTitle = "Chart Axis Options";
    public const string AxisLabel = "Axis";
    public const string CategoryAxisLabel = "Category axis";
    public const string ValueAxisLabel = "Value axis";
    public const string AxisTitleLabel = "Axis title";
    public const string MinimumLabel = "Minimum";
    public const string MaximumLabel = "Maximum";
    public const string MajorUnitLabel = "Major unit";
    public const string MinorUnitLabel = "Minor unit";
    public const string NumberFormatLabel = "Number format";
    public const string MajorGridlinesLabel = "Major gridlines";
    public const string AutoHint = "Blank values use automatic chart scaling.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 480;
    public const double DefaultDialogHeight = 430;

    public static IReadOnlyList<ChartAxisKindOption> AxisOptions { get; } =
    [
        new(ChartAxisKind.Category, CategoryAxisLabel),
        new(ChartAxisKind.Value, ValueAxisLabel),
    ];

    private readonly ChartShape _chart;
    private ChartAxisKind _axisKind;
    private string _title = string.Empty;
    private double? _minimum;
    private double? _maximum;
    private double? _majorUnit;
    private double? _minorUnit;
    private string _numberFormat = string.Empty;
    private bool _majorGridlines;

    private ChartAxisOptionsPlanner(ChartShape chart)
    {
        _chart = chart;
        SetAxis(ChartAxisKind.Value);
    }

    public static ChartAxisOptionsSurfacePlan BuildSurfacePlan() =>
        new(
            CommandId,
            DialogTitle,
            AxisLabel,
            CategoryAxisLabel,
            ValueAxisLabel,
            AxisTitleLabel,
            MinimumLabel,
            MaximumLabel,
            MajorUnitLabel,
            MinorUnitLabel,
            NumberFormatLabel,
            MajorGridlinesLabel,
            AutoHint,
            OkLabel,
            CancelLabel);

    public static ChartAxisOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartAxisOptionsPlanner(chart);
    }

    public ChartAxisKind Axis => _axisKind;
    public string Title => _title;
    public double? Minimum => _minimum;
    public double? Maximum => _maximum;
    public double? MajorUnit => _majorUnit;
    public double? MinorUnit => _minorUnit;
    public string NumberFormatCode => _numberFormat;
    public bool MajorGridlines => _majorGridlines;

    public void SetAxis(ChartAxisKind axisKind)
    {
        _axisKind = axisKind;
        var axis = ResolveAxis();
        _title = axis.Title ?? string.Empty;
        _minimum = axis.Min;
        _maximum = axis.Max;
        _majorUnit = axis.MajorUnit;
        _minorUnit = axis.MinorUnit;
        _numberFormat = axis.NumberFormatCode ?? string.Empty;
        _majorGridlines = axis.HasMajorGridlines;
    }

    public void SetTitle(string? title) => _title = title ?? string.Empty;
    public void SetMinimum(double? minimum) => _minimum = minimum;
    public void SetMaximum(double? maximum) => _maximum = maximum;
    public void SetMajorUnit(double? majorUnit) => _majorUnit = majorUnit;
    public void SetMinorUnit(double? minorUnit) => _minorUnit = minorUnit;
    public void SetNumberFormatCode(string? formatCode) => _numberFormat = formatCode ?? string.Empty;
    public void SetMajorGridlines(bool show) => _majorGridlines = show;

    public ChartAxisOptions BuildCommitPlan() => new(
        _axisKind,
        string.IsNullOrWhiteSpace(_title) ? null : _title.Trim(),
        _minimum,
        _maximum,
        _majorUnit,
        _minorUnit,
        string.IsNullOrWhiteSpace(_numberFormat) ? null : _numberFormat.Trim(),
        _majorGridlines);

    private ChartAxis ResolveAxis() =>
        _axisKind == ChartAxisKind.Category ? _chart.CategoryAxis : _chart.ValueAxis;
}
