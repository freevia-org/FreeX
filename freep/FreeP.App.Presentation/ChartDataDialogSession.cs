using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartDataDialogEdits(
    IReadOnlyList<ChartDataDialogSeriesNameEdit> SeriesNames,
    IReadOnlyList<ChartDataDialogCategoryEdit> Categories,
    IReadOnlyList<ChartDataDialogValueEdit> Values)
{
    public static ChartDataDialogEdits Empty { get; } = new([], [], []);
}

public sealed record ChartDataDialogValidationDecision(
    bool IsValid,
    int InvalidValueEditIndex,
    string Message)
{
    public static ChartDataDialogValidationDecision Valid { get; } =
        new(true, -1, string.Empty);

    internal static ChartDataDialogValidationDecision InvalidNumericValue(int valueEditIndex) =>
        new(false, valueEditIndex, ChartDataDialogPlanner.InvalidNumericValueMessage);
}

/// <summary>
/// Renderer-neutral state and orchestration for the chart-data dialog.
/// Renderers retain native grids, converters, focus, events, and validation rendering.
/// </summary>
public sealed class ChartDataDialogSession
{
    private readonly EditingSession _editor;
    private readonly ChartDataDialogPlanner _planner;
    private int _activeSeriesIndex = -1;
    private int _activeCategoryIndex = -1;

    public ChartDataDialogSession(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartDataDialogPlanner.FromChart(chart);
    }

    public int ActiveSeriesIndex => _activeSeriesIndex;

    public int ActiveCategoryIndex => _activeCategoryIndex;

    public int SeriesCount => _planner.SeriesCount;

    public int CategoryCount => _planner.CategoryCount;

    public ChartType SelectedChartType => _planner.SelectedChartType;

    public ChartDataDialogTableProjection BuildTableProjection() =>
        _planner.BuildTableProjection();

    public void SetChartType(ChartType chartType) =>
        _planner.SetChartType(chartType);

    public void SelectSeries(int seriesIndex)
    {
        _activeSeriesIndex = IsIndexInRange(seriesIndex, SeriesCount)
            ? seriesIndex
            : -1;
    }

    public void SelectCategory(int categoryIndex)
    {
        _activeCategoryIndex = IsIndexInRange(categoryIndex, CategoryCount)
            ? categoryIndex
            : -1;
    }

    public void AddSeries() => _planner.AddSeries();

    public bool RemoveActiveSeries()
    {
        var seriesIndex = _activeSeriesIndex >= 0
            ? _activeSeriesIndex
            : SeriesCount - 1;
        if (!_planner.RemoveSeriesAt(seriesIndex))
            return false;

        _activeSeriesIndex = SeriesCount == 0
            ? -1
            : Math.Min(_activeSeriesIndex, SeriesCount - 1);
        return true;
    }

    public bool MoveActiveSeries(int delta)
    {
        if (!_planner.MoveSeries(_activeSeriesIndex, _activeSeriesIndex + delta))
            return false;

        _activeSeriesIndex = Math.Clamp(
            _activeSeriesIndex + delta,
            0,
            SeriesCount - 1);
        return true;
    }

    public void AddCategory() => _planner.AddCategory();

    public bool RemoveActiveCategory()
    {
        var categoryIndex = _activeCategoryIndex >= 0
            ? _activeCategoryIndex
            : CategoryCount - 1;
        if (!_planner.RemoveCategoryAt(categoryIndex))
            return false;

        _activeCategoryIndex = CategoryCount == 0
            ? -1
            : Math.Min(_activeCategoryIndex, CategoryCount - 1);
        return true;
    }

    public bool MoveActiveCategory(int delta)
    {
        if (!_planner.MoveCategory(_activeCategoryIndex, _activeCategoryIndex + delta))
            return false;

        _activeCategoryIndex = Math.Clamp(
            _activeCategoryIndex + delta,
            0,
            CategoryCount - 1);
        return true;
    }

    public void SwitchRowsAndColumns() => _planner.SwitchRowsAndColumns();

    public ChartDataDialogValidationDecision ValidateValueEdit(
        object? value,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return IsInvalidNumericValue(value, culture)
            ? ChartDataDialogValidationDecision.InvalidNumericValue(0)
            : ChartDataDialogValidationDecision.Valid;
    }

    public bool TryApplyEdits(
        ChartDataDialogEdits edits,
        CultureInfo culture,
        out ChartDataDialogValidationDecision validation)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(culture);

        validation = ValidateEdits(edits, culture);
        if (!validation.IsValid)
            return false;

        _planner.ApplySeriesNameEdits(edits.SeriesNames);
        _planner.ApplyCategoryEdits(edits.Categories);
        _planner.ApplyValueEdits(edits.Values, culture);
        return true;
    }

    public bool TryCommit(
        ChartDataDialogEdits edits,
        CultureInfo culture,
        out ChartDataDialogValidationDecision validation)
    {
        if (!TryApplyEdits(edits, culture, out validation))
            return false;

        Commit(BuildCommitPlan());
        return true;
    }

    public ChartDataDialogCommitPlan BuildCommitPlan() =>
        _planner.BuildCommitPlan();

    private ChartDataDialogValidationDecision ValidateEdits(
        ChartDataDialogEdits edits,
        CultureInfo culture)
    {
        for (var index = 0; index < edits.Values.Count; index++)
        {
            if (IsInvalidNumericValue(edits.Values[index].Value, culture))
                return ChartDataDialogValidationDecision.InvalidNumericValue(index);
        }

        return ChartDataDialogValidationDecision.Valid;
    }

    private void Commit(ChartDataDialogCommitPlan commit)
    {
        _editor.ReplaceChartData(
            commit.Categories,
            commit.SeriesNames,
            commit.ValuesForCommand(),
            commit.ChartType,
            commit.XValuesForCommand(),
            commit.BubbleSizesForCommand());
    }

    private static bool IsInvalidNumericValue(object? value, CultureInfo culture)
    {
        if (value is null)
            return false;
        if (value is string text && string.IsNullOrWhiteSpace(text))
            return false;
        return ChartDataDialogPlanner.ParseCellValue(value, culture) is null;
    }

    private static bool IsIndexInRange(int index, int count) =>
        index >= 0 && index < count;
}
