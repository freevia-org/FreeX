namespace FreeP.Core.Model;

/// <summary>Atomically updates the authored scale and display options for one chart axis.</summary>
public sealed class SetChartAxisOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartAxisOptions _newOptions;

    private string? _oldTitle;
    private bool _oldDelete;
    private double? _oldMinimum;
    private double? _oldMaximum;
    private double? _oldMajorUnit;
    private double? _oldMinorUnit;
    private string? _oldNumberFormatCode;
    private bool? _oldNumberFormatSourceLinked;
    private bool _oldMajorGridlines;
    private ChartTickMark? _oldMajorTickMark;
    private ChartTickMark? _oldMinorTickMark;
    private ChartTickLabelPosition? _oldTickLabelPosition;
    private ChartAxisCrossing? _oldCrosses;
    private double? _oldCrossesAt;
    private ChartCrossBetween? _oldCrossBetween;
    private ChartLabelAlignment? _oldLabelAlignment;
    private int? _oldLabelOffsetPercent;
    private bool? _oldNoMultiLevelLabels;
    private bool? _oldAutoCrossing;
    private bool _oldReverseOrder;
    private bool _oldSecondaryAxisExisted;

    public SetChartAxisOptionsCommand(int slideIndex, uint shapeId, ChartAxisOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Chart Axis Options";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null)
            return;

        _oldSecondaryAxisExisted = chart.SecondaryValueAxis is not null;
        var axis = ResolveAxis(chart, _newOptions.Axis, createSecondary: true)
            ?? throw new InvalidOperationException("The chart axis could not be resolved.");
        Capture(axis);
        axis.Title = string.IsNullOrWhiteSpace(_newOptions.Title) ? null : _newOptions.Title.Trim();
        axis.Delete = !_newOptions.ShowAxis;
        axis.Min = _newOptions.Minimum;
        axis.Max = _newOptions.Maximum;
        axis.MajorUnit = _newOptions.MajorUnit;
        axis.MinorUnit = _newOptions.MinorUnit;
        axis.NumberFormatCode = string.IsNullOrWhiteSpace(_newOptions.NumberFormatCode)
            ? null
            : _newOptions.NumberFormatCode.Trim();
        axis.NumberFormatSourceLinked = axis.NumberFormatCode is null ? null : false;
        axis.HasMajorGridlines = _newOptions.MajorGridlines;
        axis.MajorTickMark = _newOptions.MajorTickMark;
        axis.MinorTickMark = _newOptions.MinorTickMark;
        axis.TickLabelPosition = _newOptions.TickLabelPosition;
        axis.Crosses = _newOptions.CrossesAt is null ? _newOptions.Crosses : null;
        axis.CrossesAt = _newOptions.CrossesAt;
        axis.CrossBetween = _newOptions.CrossBetween;
        axis.LabelAlignment = _newOptions.LabelAlignment;
        axis.LabelOffsetPercent = _newOptions.LabelOffsetPercent is { } offset
            ? Math.Clamp(offset, 0, 100)
            : null;
        axis.NoMultiLevelLabels = _newOptions.NoMultiLevelLabels;
        axis.AutoCrossing = _newOptions.AutoCrossing;
        axis.ReverseOrder = _newOptions.ReverseOrder;
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null)
            return;

        var axis = ResolveAxis(chart, _newOptions.Axis, createSecondary: false);
        if (axis is null)
            return;
        axis.Title = _oldTitle;
        axis.Delete = _oldDelete;
        axis.Min = _oldMinimum;
        axis.Max = _oldMaximum;
        axis.MajorUnit = _oldMajorUnit;
        axis.MinorUnit = _oldMinorUnit;
        axis.NumberFormatCode = _oldNumberFormatCode;
        axis.NumberFormatSourceLinked = _oldNumberFormatSourceLinked;
        axis.HasMajorGridlines = _oldMajorGridlines;
        axis.MajorTickMark = _oldMajorTickMark;
        axis.MinorTickMark = _oldMinorTickMark;
        axis.TickLabelPosition = _oldTickLabelPosition;
        axis.Crosses = _oldCrosses;
        axis.CrossesAt = _oldCrossesAt;
        axis.CrossBetween = _oldCrossBetween;
        axis.LabelAlignment = _oldLabelAlignment;
        axis.LabelOffsetPercent = _oldLabelOffsetPercent;
        axis.NoMultiLevelLabels = _oldNoMultiLevelLabels;
        axis.AutoCrossing = _oldAutoCrossing;
        axis.ReverseOrder = _oldReverseOrder;
        if (_newOptions.Axis == ChartAxisKind.SecondaryValue && !_oldSecondaryAxisExisted)
            chart.SecondaryValueAxis = null;
        ChartHelper.MarkWorkbookDirty(chart);
    }

    private void Capture(ChartAxis axis)
    {
        _oldTitle = axis.Title;
        _oldDelete = axis.Delete;
        _oldMinimum = axis.Min;
        _oldMaximum = axis.Max;
        _oldMajorUnit = axis.MajorUnit;
        _oldMinorUnit = axis.MinorUnit;
        _oldNumberFormatCode = axis.NumberFormatCode;
        _oldNumberFormatSourceLinked = axis.NumberFormatSourceLinked;
        _oldMajorGridlines = axis.HasMajorGridlines;
        _oldMajorTickMark = axis.MajorTickMark;
        _oldMinorTickMark = axis.MinorTickMark;
        _oldTickLabelPosition = axis.TickLabelPosition;
        _oldCrosses = axis.Crosses;
        _oldCrossesAt = axis.CrossesAt;
        _oldCrossBetween = axis.CrossBetween;
        _oldLabelAlignment = axis.LabelAlignment;
        _oldLabelOffsetPercent = axis.LabelOffsetPercent;
        _oldNoMultiLevelLabels = axis.NoMultiLevelLabels;
        _oldAutoCrossing = axis.AutoCrossing;
        _oldReverseOrder = axis.ReverseOrder;
    }

    private static ChartAxis? ResolveAxis(ChartShape chart, ChartAxisKind kind, bool createSecondary)
        => kind switch
        {
            ChartAxisKind.Category => chart.CategoryAxis,
            ChartAxisKind.Value => chart.ValueAxis,
            ChartAxisKind.SecondaryValue => createSecondary
                ? chart.SecondaryValueAxis ??= new ChartAxis()
                : chart.SecondaryValueAxis,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
}
