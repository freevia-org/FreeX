namespace FreeP.Core.Model;

/// <summary>Changes all modeled chart protection flags as one undoable operation.</summary>
public sealed class SetChartProtectionOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartProtectionOptions _newOptions;
    private ChartProtectionOptions? _oldOptions;

    public SetChartProtectionOptionsCommand(
        int slideIndex,
        uint shapeId,
        ChartProtectionOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Chart Protection";

    public void Apply(Presentation presentation)
    {
        var chart = ChartHelper.Find(presentation, _slideIndex, _shapeId);
        if (chart is null)
            return;

        _oldOptions = ReadOptions(chart);
        ApplyOptions(chart, _newOptions);
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation presentation)
    {
        var chart = ChartHelper.Find(presentation, _slideIndex, _shapeId);
        if (chart is null || _oldOptions is null)
            return;

        ApplyOptions(chart, _oldOptions);
        ChartHelper.MarkWorkbookDirty(chart);
    }

    private static ChartProtectionOptions ReadOptions(ChartShape chart) => new(
        chart.ChartObjectProtected,
        chart.ChartDataProtected,
        chart.ChartFormattingProtected,
        chart.ChartSelectionProtected);

    private static void ApplyOptions(ChartShape chart, ChartProtectionOptions options)
    {
        chart.ChartObjectProtected = options.ChartObject;
        chart.ChartDataProtected = options.Data;
        chart.ChartFormattingProtected = options.Formatting;
        chart.ChartSelectionProtected = options.Selection;
    }
}
