using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartExSeriesLayoutChoice(string LayoutId, string Label);

public sealed record ChartExSeriesLayoutSelection(
    int SeriesOptionIndex,
    IReadOnlyList<ChartExSeriesLayoutChoice> LayoutChoices,
    int LayoutIndex);

public sealed class ChartExSeriesLayoutDialogSession
{
    private const string InvalidSelectionMessage =
        "Choose a series and layout from this ChartEx payload.";

    private readonly EditingSession _editor;
    private readonly ChartShape _chart;

    public ChartExSeriesLayoutDialogSession(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        if (!ChartExSeriesLayoutPlanner.CanEdit(_chart))
        {
            throw new InvalidOperationException(
                "The selected chart has no editable native ChartEx series layouts.");
        }

        SeriesOptions = ChartExSeriesLayoutPlanner.BuildOptions(_chart);
        Selection = SelectSeries(0);
    }

    public IReadOnlyList<ChartExSeriesLayoutOption> SeriesOptions { get; }

    public ChartExSeriesLayoutSelection Selection { get; private set; }

    public int SelectedSeriesIndex =>
        Selection.SeriesOptionIndex >= 0
        && Selection.SeriesOptionIndex < SeriesOptions.Count
            ? SeriesOptions[Selection.SeriesOptionIndex].SeriesIndex
            : -1;

    public ChartExSeriesLayoutSelection SelectSeries(int seriesOptionIndex)
    {
        if (seriesOptionIndex < 0 || seriesOptionIndex >= SeriesOptions.Count)
        {
            Selection = new(
                -1,
                Array.Empty<ChartExSeriesLayoutChoice>(),
                -1);
            return Selection;
        }

        var option = SeriesOptions[seriesOptionIndex];
        var choices = ChartExSeriesLayoutPlanner.BuildLayoutChoices(_chart)
            .Select(layoutId => new ChartExSeriesLayoutChoice(
                layoutId,
                ChartExSeriesLayoutPlanner.FormatLayoutLabel(layoutId)))
            .ToArray();
        var layoutIndex = Array.FindIndex(
            choices,
            choice => string.Equals(
                choice.LayoutId,
                option.LayoutId,
                StringComparison.OrdinalIgnoreCase));

        Selection = new(
            seriesOptionIndex,
            choices,
            Math.Max(0, layoutIndex));
        return Selection;
    }

    public string? LayoutIdAt(int layoutIndex) =>
        layoutIndex >= 0 && layoutIndex < Selection.LayoutChoices.Count
            ? Selection.LayoutChoices[layoutIndex].LayoutId
            : null;

    public bool TryApply(int layoutIndex, out string error)
    {
        var layoutId = LayoutIdAt(layoutIndex);
        if (SelectedSeriesIndex < 0 || layoutId is null)
        {
            error = InvalidSelectionMessage;
            return false;
        }

        try
        {
            var plan = ChartExSeriesLayoutPlanner.BuildCommitPlan(
                _chart,
                SelectedSeriesIndex,
                layoutId);
            _editor.SetChartExSeriesLayout(plan.SeriesIndex, plan.LayoutId);
            error = string.Empty;
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
