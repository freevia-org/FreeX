namespace FreeP.Core.Model;

/// <summary>Atomically updates common PowerPoint chart display options.</summary>
public sealed class SetChartDisplayOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartDisplayOptions _newOptions;

    private string? _oldTitle;
    private bool _oldAutomaticTitle;
    private LegendPosition? _oldLegend;
    private ChartDataLabels? _oldDataLabels;
    private bool _oldCategoryGridlines;
    private bool _oldValueGridlines;
    private int? _oldBarGapWidthPercent;
    private int? _oldBarOverlapPercent;

    public SetChartDisplayOptionsCommand(
        int slideIndex,
        uint shapeId,
        ChartDisplayOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Chart Options";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null)
            return;

        _oldTitle = chart.Title;
        _oldAutomaticTitle = chart.HasAutomaticTitle;
        _oldLegend = chart.Legend;
        _oldDataLabels = CloneDataLabels(chart.DataLabels);
        _oldCategoryGridlines = chart.CategoryAxis.HasMajorGridlines;
        _oldValueGridlines = chart.ValueAxis.HasMajorGridlines;
        _oldBarGapWidthPercent = chart.BarGapWidthPercent;
        _oldBarOverlapPercent = chart.BarOverlapPercent;

        chart.Title = string.IsNullOrWhiteSpace(_newOptions.Title) ? null : _newOptions.Title;
        chart.HasAutomaticTitle = false;
        chart.Legend = _newOptions.Legend;
        chart.CategoryAxis.HasMajorGridlines = _newOptions.CategoryGridlines;
        chart.ValueAxis.HasMajorGridlines = _newOptions.ValueGridlines;
        chart.BarGapWidthPercent = Normalize(_newOptions.BarGapWidthPercent, 0, 500);
        chart.BarOverlapPercent = Normalize(_newOptions.BarOverlapPercent, -100, 100);

        if (chart.DataLabels is not null)
        {
            chart.DataLabels.ShowValue = _newOptions.ShowValueLabels;
            chart.DataLabels.ShowPercent = _newOptions.ShowPercentLabels;
            chart.DataLabels.ShowCategoryName = _newOptions.ShowCategoryLabels;
            chart.DataLabels.ShowSeriesName = _newOptions.ShowSeriesLabels;
            chart.DataLabels.ShowLegendKey = _newOptions.ShowLegendKeys;
            chart.DataLabels.Position = _newOptions.LabelPosition;
            chart.DataLabels.NumberFormat = _newOptions.LabelNumberFormat;
            chart.DataLabels.Separator = _newOptions.LabelSeparator;
        }
        else if (_newOptions.ShowValueLabels ||
                 _newOptions.ShowPercentLabels ||
                 _newOptions.ShowCategoryLabels ||
                 _newOptions.ShowSeriesLabels ||
                 _newOptions.ShowLegendKeys ||
                 !string.IsNullOrWhiteSpace(_newOptions.LabelNumberFormat) ||
                 !string.IsNullOrEmpty(_newOptions.LabelSeparator))
        {
            chart.DataLabels = new ChartDataLabels
            {
                ShowValue = _newOptions.ShowValueLabels,
                ShowPercent = _newOptions.ShowPercentLabels,
                ShowCategoryName = _newOptions.ShowCategoryLabels,
                ShowSeriesName = _newOptions.ShowSeriesLabels,
                ShowLegendKey = _newOptions.ShowLegendKeys,
                Position = _newOptions.LabelPosition,
                NumberFormat = _newOptions.LabelNumberFormat,
                Separator = _newOptions.LabelSeparator,
            };
        }

        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null)
            return;

        chart.Title = _oldTitle;
        chart.HasAutomaticTitle = _oldAutomaticTitle;
        chart.Legend = _oldLegend;
        chart.DataLabels = CloneDataLabels(_oldDataLabels);
        chart.CategoryAxis.HasMajorGridlines = _oldCategoryGridlines;
        chart.ValueAxis.HasMajorGridlines = _oldValueGridlines;
        chart.BarGapWidthPercent = _oldBarGapWidthPercent;
        chart.BarOverlapPercent = _oldBarOverlapPercent;
        ChartHelper.MarkWorkbookDirty(chart);
    }

    private static ChartDataLabels? CloneDataLabels(ChartDataLabels? source) => source is null
        ? null
        : new ChartDataLabels
        {
            ShowValue = source.ShowValue,
            ShowPercent = source.ShowPercent,
            ShowCategoryName = source.ShowCategoryName,
            ShowSeriesName = source.ShowSeriesName,
            ShowLegendKey = source.ShowLegendKey,
            Position = source.Position,
            NumberFormat = source.NumberFormat,
            Separator = source.Separator,
            TextStyle = source.TextStyle is null
                ? null
                : new ChartTextStyle
                {
                    IsImplicitDefault = source.TextStyle.IsImplicitDefault,
                    FontSizePt = source.TextStyle.FontSizePt,
                    Bold = source.TextStyle.Bold,
                    Italic = source.TextStyle.Italic,
                    Color = source.TextStyle.Color,
                    FontFamily = source.TextStyle.FontFamily,
                },
        };

    private static int? Normalize(int? value, int minimum, int maximum) =>
        value is null ? null : Math.Clamp(value.Value, minimum, maximum);
}
