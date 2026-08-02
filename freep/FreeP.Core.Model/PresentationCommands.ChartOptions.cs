namespace FreeP.Core.Model;

/// <summary>Sets one chart title as a single undoable accessibility edit.</summary>
public sealed class SetChartTitleCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly string _newTitle;
    private string? _oldTitle;
    private bool _oldAutomaticTitle;

    public SetChartTitleCommand(int slideIndex, uint shapeId, string title)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newTitle = title?.Trim() ?? string.Empty;
    }

    public string Label => "Add Chart Title";

    public void Apply(Presentation presentation)
    {
        var chart = ChartHelper.FindFormattingEditable(presentation, _slideIndex, _shapeId);
        if (chart is null || string.IsNullOrWhiteSpace(_newTitle))
            return;

        _oldTitle = chart.Title;
        _oldAutomaticTitle = chart.HasAutomaticTitle;
        chart.Title = _newTitle;
        chart.HasAutomaticTitle = false;
    }

    public void Revert(Presentation presentation)
    {
        var chart = ChartHelper.FindFormattingEditable(presentation, _slideIndex, _shapeId);
        if (chart is null)
            return;

        chart.Title = _oldTitle;
        chart.HasAutomaticTitle = _oldAutomaticTitle;
    }
}

/// <summary>Atomically updates common PowerPoint chart display options.</summary>
public sealed class SetChartDisplayOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartDisplayOptions _newOptions;

    private string? _oldTitle;
    private bool _oldAutomaticTitle;
    private bool? _oldTitleOverlay;
    private bool? _oldPlotVisibleOnly;
    private bool? _oldRoundedCorners;
    private LegendPosition? _oldLegend;
    private ChartDataLabels? _oldDataLabels;
    private bool _oldCategoryGridlines;
    private bool _oldValueGridlines;
    private int? _oldBarGapWidthPercent;
    private int? _oldBarOverlapPercent;
    private ChartDisplayBlanksAs? _oldDisplayBlanksAs;
    private bool? _oldShowDataLabelsOverMaximum;
    private bool _oldVaryColors;
    private bool? _oldLegendOverlay;
    private bool? _oldHighLowLines;
    private int? _oldStyleId;

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
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (chart is null)
            return;

        _oldTitle = chart.Title;
        _oldAutomaticTitle = chart.HasAutomaticTitle;
        _oldTitleOverlay = chart.TitleOverlay;
        _oldPlotVisibleOnly = chart.PlotVisibleOnly;
        _oldRoundedCorners = chart.RoundedCorners;
        _oldLegend = chart.Legend;
        _oldDataLabels = CloneDataLabels(chart.DataLabels);
        _oldCategoryGridlines = chart.CategoryAxis.HasMajorGridlines;
        _oldValueGridlines = chart.ValueAxis.HasMajorGridlines;
        _oldBarGapWidthPercent = chart.BarGapWidthPercent;
        _oldBarOverlapPercent = chart.BarOverlapPercent;
        _oldDisplayBlanksAs = chart.DisplayBlanksAs;
        _oldShowDataLabelsOverMaximum = chart.ShowDataLabelsOverMaximum;
        _oldVaryColors = chart.VaryColors;
        _oldLegendOverlay = chart.LegendOverlay;
        _oldHighLowLines = chart.ChartType == ChartType.Stock ? chart.HasHighLowLines : null;
        _oldStyleId = chart.StyleId;

        chart.Title = string.IsNullOrWhiteSpace(_newOptions.Title) ? null : _newOptions.Title;
        chart.HasAutomaticTitle = false;
        if (_newOptions.TitleOverlay.HasValue)
            chart.TitleOverlay = _newOptions.TitleOverlay;
        if (_newOptions.PlotVisibleOnly.HasValue)
            chart.PlotVisibleOnly = _newOptions.PlotVisibleOnly;
        if (_newOptions.RoundedCorners.HasValue)
            chart.RoundedCorners = _newOptions.RoundedCorners;
        chart.Legend = _newOptions.Legend;
        chart.CategoryAxis.HasMajorGridlines = _newOptions.CategoryGridlines;
        chart.ValueAxis.HasMajorGridlines = _newOptions.ValueGridlines;
        chart.BarGapWidthPercent = Normalize(_newOptions.BarGapWidthPercent, 0, 500);
        chart.BarOverlapPercent = Normalize(_newOptions.BarOverlapPercent, -100, 100);
        chart.DisplayBlanksAs = _newOptions.DisplayBlanksAs;
        chart.ShowDataLabelsOverMaximum = _newOptions.ShowDataLabelsOverMaximum;
        chart.VaryColors = _newOptions.VaryColors ?? chart.VaryColors;
        chart.LegendOverlay = _newOptions.LegendOverlay;
        chart.StyleId = _newOptions.StyleId;
        if (chart.ChartType == ChartType.Stock && _newOptions.HighLowLines.HasValue)
            chart.HasHighLowLines = _newOptions.HighLowLines.Value;

        if (chart.DataLabels is not null)
        {
            chart.DataLabels.ShowValue = _newOptions.ShowValueLabels;
            chart.DataLabels.ShowPercent = _newOptions.ShowPercentLabels;
            chart.DataLabels.ShowCategoryName = _newOptions.ShowCategoryLabels;
            chart.DataLabels.ShowSeriesName = _newOptions.ShowSeriesLabels;
            chart.DataLabels.ShowLegendKey = _newOptions.ShowLegendKeys;
            chart.DataLabels.ShowBubbleSize = _newOptions.ShowBubbleSize;
            chart.DataLabels.ShowLeaderLines = _newOptions.ShowLeaderLines;
            chart.DataLabels.Position = _newOptions.LabelPosition;
            chart.DataLabels.NumberFormat = _newOptions.LabelNumberFormat;
            chart.DataLabels.Separator = _newOptions.LabelSeparator;
            chart.DataLabels.TextStyle = CloneTextStyle(_newOptions.LabelTextStyle);
        }
        else if (_newOptions.ShowValueLabels ||
                 _newOptions.ShowPercentLabels ||
                 _newOptions.ShowCategoryLabels ||
                  _newOptions.ShowSeriesLabels ||
                  _newOptions.ShowLegendKeys ||
                  _newOptions.ShowBubbleSize ||
                  _newOptions.ShowLeaderLines.HasValue ||
                  !string.IsNullOrWhiteSpace(_newOptions.LabelNumberFormat) ||
                 !string.IsNullOrEmpty(_newOptions.LabelSeparator) ||
                 _newOptions.LabelTextStyle is not null)
        {
            chart.DataLabels = new ChartDataLabels
            {
                ShowValue = _newOptions.ShowValueLabels,
                ShowPercent = _newOptions.ShowPercentLabels,
                ShowCategoryName = _newOptions.ShowCategoryLabels,
                ShowSeriesName = _newOptions.ShowSeriesLabels,
                ShowLegendKey = _newOptions.ShowLegendKeys,
                ShowBubbleSize = _newOptions.ShowBubbleSize,
                ShowLeaderLines = _newOptions.ShowLeaderLines,
                Position = _newOptions.LabelPosition,
                NumberFormat = _newOptions.LabelNumberFormat,
                Separator = _newOptions.LabelSeparator,
                TextStyle = CloneTextStyle(_newOptions.LabelTextStyle),
            };
        }

        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (chart is null)
            return;

        chart.Title = _oldTitle;
        chart.HasAutomaticTitle = _oldAutomaticTitle;
        chart.TitleOverlay = _oldTitleOverlay;
        chart.PlotVisibleOnly = _oldPlotVisibleOnly;
        chart.RoundedCorners = _oldRoundedCorners;
        chart.Legend = _oldLegend;
        chart.DataLabels = CloneDataLabels(_oldDataLabels);
        chart.CategoryAxis.HasMajorGridlines = _oldCategoryGridlines;
        chart.ValueAxis.HasMajorGridlines = _oldValueGridlines;
        chart.BarGapWidthPercent = _oldBarGapWidthPercent;
        chart.BarOverlapPercent = _oldBarOverlapPercent;
        chart.DisplayBlanksAs = _oldDisplayBlanksAs;
        chart.ShowDataLabelsOverMaximum = _oldShowDataLabelsOverMaximum;
        chart.VaryColors = _oldVaryColors;
        chart.LegendOverlay = _oldLegendOverlay;
        chart.StyleId = _oldStyleId;
        if (chart.ChartType == ChartType.Stock && _oldHighLowLines.HasValue)
            chart.HasHighLowLines = _oldHighLowLines.Value;
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
            ShowBubbleSize = source.ShowBubbleSize,
            ShowLeaderLines = source.ShowLeaderLines,
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

    private static ChartTextStyle? CloneTextStyle(ChartTextStyle? source) => source is null
        ? null
        : new ChartTextStyle
        {
            IsImplicitDefault = source.IsImplicitDefault,
            FontSizePt = source.FontSizePt,
            Bold = source.Bold,
            Italic = source.Italic,
            Color = source.Color,
            FontFamily = source.FontFamily,
        };
}
