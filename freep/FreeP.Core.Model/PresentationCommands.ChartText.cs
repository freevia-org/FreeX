namespace FreeP.Core.Model;

/// <summary>Applies chart-wide default text formatting as one undoable edit.</summary>
public sealed class SetChartTextOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartTextOptions _newOptions;
    private ChartTextOptions? _oldOptions;

    public SetChartTextOptionsCommand(int slideIndex, uint shapeId, ChartTextOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Chart Text Options";

    public void Apply(Presentation presentation)
    {
        var chart = FindChart(presentation);
        if (chart is null) return;

        _oldOptions ??= ReadOptions(chart, _newOptions.Target);
        SetStyle(chart, _newOptions.Target, BuildStyle(_newOptions));
    }

    public void Revert(Presentation presentation)
    {
        var chart = FindChart(presentation);
        if (chart is not null && _oldOptions is not null)
            SetStyle(chart, _newOptions.Target, BuildStyle(_oldOptions));
    }

    private ChartShape? FindChart(Presentation presentation) =>
        ShapeHelper.Find(presentation, _slideIndex, _shapeId)?.Chart;

    private static ChartTextOptions ReadOptions(ChartShape chart, ChartTextTarget target)
    {
        var style = target == ChartTextTarget.Title ? chart.TitleStyle : chart.TextStyle;
        return style is null or { IsImplicitDefault: true }
            ? new ChartTextOptions(null, null, null, null, null, target)
            : new ChartTextOptions(style.FontFamily, style.FontSizePt, style.Bold, style.Italic, style.Color, target);
    }

    private static void SetStyle(ChartShape chart, ChartTextTarget target, ChartTextStyle? style)
    {
        if (target == ChartTextTarget.Title)
            chart.TitleStyle = style;
        else
            chart.TextStyle = style;
    }

    private static ChartTextStyle? BuildStyle(ChartTextOptions options) =>
        options.FontFamily is null && options.FontSizePt is null && options.Bold is null
            && options.Italic is null && options.Color is null
            ? null
            : new ChartTextStyle
            {
                IsImplicitDefault = false,
                FontFamily = options.FontFamily,
                FontSizePt = options.FontSizePt,
                Bold = options.Bold,
                Italic = options.Italic,
                Color = options.Color,
            };
}
