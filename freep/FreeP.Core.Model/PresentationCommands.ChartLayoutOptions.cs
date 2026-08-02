namespace FreeP.Core.Model;

/// <summary>Atomically updates one chart manual-layout payload.</summary>
public sealed class SetChartLayoutOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartLayoutOptions _newOptions;
    private ChartManualLayout? _oldLayout;

    public SetChartLayoutOptionsCommand(int slideIndex, uint shapeId, ChartLayoutOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Chart Layout Options";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;

        _oldLayout = CloneLayout(GetLayout(chart, _newOptions.Target));
        SetLayout(chart, _newOptions.Target, BuildLayout(_newOptions));
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;

        SetLayout(chart, _newOptions.Target, CloneLayout(_oldLayout));
        ChartHelper.MarkWorkbookDirty(chart);
    }

    private static ChartManualLayout? BuildLayout(ChartLayoutOptions options)
    {
        var layout = new ChartManualLayout
        {
            LayoutTarget = string.IsNullOrWhiteSpace(options.LayoutTarget) ? null : options.LayoutTarget,
            XMode = options.XMode,
            YMode = options.YMode,
            WidthMode = options.WidthMode,
            HeightMode = options.HeightMode,
            RawXModeToken = options.RawXModeToken,
            RawYModeToken = options.RawYModeToken,
            RawWidthModeToken = options.RawWidthModeToken,
            RawHeightModeToken = options.RawHeightModeToken,
            X = options.X,
            Y = options.Y,
            Width = options.Width,
            Height = options.Height,
        };
        return layout.LayoutTarget is not null || layout.X.HasValue || layout.Y.HasValue ||
               layout.Width.HasValue || layout.Height.HasValue ||
               layout.XMode != ChartManualLayoutMode.Factor ||
               layout.YMode != ChartManualLayoutMode.Factor ||
               layout.WidthMode != ChartManualLayoutMode.Factor ||
               layout.HeightMode != ChartManualLayoutMode.Factor
            ? layout
            : null;
    }

    private static ChartManualLayout? GetLayout(ChartShape chart, ChartLayoutTarget target) =>
        target == ChartLayoutTarget.PlotArea ? chart.PlotAreaManualLayout : chart.LegendManualLayout;

    private static void SetLayout(ChartShape chart, ChartLayoutTarget target, ChartManualLayout? layout)
    {
        if (target == ChartLayoutTarget.PlotArea)
            chart.PlotAreaManualLayout = layout;
        else
            chart.LegendManualLayout = layout;
    }

    private static ChartManualLayout? CloneLayout(ChartManualLayout? source) => source is null
        ? null
        : new ChartManualLayout
        {
            LayoutTarget = source.LayoutTarget,
            XMode = source.XMode,
            YMode = source.YMode,
            WidthMode = source.WidthMode,
            HeightMode = source.HeightMode,
            RawXModeToken = source.RawXModeToken,
            RawYModeToken = source.RawYModeToken,
            RawWidthModeToken = source.RawWidthModeToken,
            RawHeightModeToken = source.RawHeightModeToken,
            X = source.X,
            Y = source.Y,
            Width = source.Width,
            Height = source.Height,
        };
}
