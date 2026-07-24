namespace FreeP.Core.Model;

/// <summary>Atomically updates the authored scale and display options for one chart axis.</summary>
public sealed class SetChartAxisOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartAxisOptions _newOptions;

    private string? _oldTitle;
    private double? _oldMinimum;
    private double? _oldMaximum;
    private double? _oldMajorUnit;
    private double? _oldMinorUnit;
    private string? _oldNumberFormatCode;
    private bool? _oldNumberFormatSourceLinked;
    private bool _oldMajorGridlines;

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

        var axis = ResolveAxis(chart, _newOptions.Axis);
        Capture(axis);
        axis.Title = string.IsNullOrWhiteSpace(_newOptions.Title) ? null : _newOptions.Title.Trim();
        axis.Min = _newOptions.Minimum;
        axis.Max = _newOptions.Maximum;
        axis.MajorUnit = _newOptions.MajorUnit;
        axis.MinorUnit = _newOptions.MinorUnit;
        axis.NumberFormatCode = string.IsNullOrWhiteSpace(_newOptions.NumberFormatCode)
            ? null
            : _newOptions.NumberFormatCode.Trim();
        axis.NumberFormatSourceLinked = axis.NumberFormatCode is null ? null : false;
        axis.HasMajorGridlines = _newOptions.MajorGridlines;
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null)
            return;

        var axis = ResolveAxis(chart, _newOptions.Axis);
        axis.Title = _oldTitle;
        axis.Min = _oldMinimum;
        axis.Max = _oldMaximum;
        axis.MajorUnit = _oldMajorUnit;
        axis.MinorUnit = _oldMinorUnit;
        axis.NumberFormatCode = _oldNumberFormatCode;
        axis.NumberFormatSourceLinked = _oldNumberFormatSourceLinked;
        axis.HasMajorGridlines = _oldMajorGridlines;
        ChartHelper.MarkWorkbookDirty(chart);
    }

    private void Capture(ChartAxis axis)
    {
        _oldTitle = axis.Title;
        _oldMinimum = axis.Min;
        _oldMaximum = axis.Max;
        _oldMajorUnit = axis.MajorUnit;
        _oldMinorUnit = axis.MinorUnit;
        _oldNumberFormatCode = axis.NumberFormatCode;
        _oldNumberFormatSourceLinked = axis.NumberFormatSourceLinked;
        _oldMajorGridlines = axis.HasMajorGridlines;
    }

    private static ChartAxis ResolveAxis(ChartShape chart, ChartAxisKind kind) =>
        kind == ChartAxisKind.Category ? chart.CategoryAxis : chart.ValueAxis;
}
