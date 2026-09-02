namespace FreeP.Core.Model;

/// <summary>Atomically updates the authored sizing semantics of a bubble chart.</summary>
public sealed class SetChartBubbleOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartBubbleOptions _newOptions;
    private ChartBubbleOptions? _oldOptions;

    public SetChartBubbleOptionsCommand(int slideIndex, uint shapeId, ChartBubbleOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Bubble Chart Options";

    // r202: mirrors the guard Apply opens with -- otherwise the bus pushes an undo entry for a
    // command that changed nothing, and that push clears the redo stack.
    public bool HasEffect(Presentation p) =>
        ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId) is { ChartType: ChartType.Bubble };

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (chart is null || chart.ChartType != ChartType.Bubble)
            return;

        _oldOptions = ReadOptions(chart);
        Apply(chart, _newOptions);
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (chart is null || chart.ChartType != ChartType.Bubble || _oldOptions is null)
            return;

        Apply(chart, _oldOptions);
        ChartHelper.MarkWorkbookDirty(chart);
    }

    private static ChartBubbleOptions ReadOptions(ChartShape chart) => new(
        Math.Clamp(chart.BubbleScalePercent, 0, 300),
        chart.BubbleSizeRepresents,
        chart.ShowNegativeBubbles);

    private static void Apply(ChartShape chart, ChartBubbleOptions options)
    {
        chart.BubbleScalePercent = Math.Clamp(options.BubbleScalePercent, 0, 300);
        chart.BubbleSizeRepresents = options.SizeRepresents;
        chart.ShowNegativeBubbles = options.ShowNegativeBubbles;
    }
}
