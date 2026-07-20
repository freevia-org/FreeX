using System.Globalization;
using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed class ChartDataDialogSeriesColumn
{
    private readonly ChartDataDialogPlanner _planner;

    internal ChartDataDialogSeriesColumn(
        ChartDataDialogPlanner planner,
        int seriesIndex,
        int valueIndex)
    {
        _planner = planner;
        SeriesIndex = seriesIndex;
        ValueIndex = valueIndex;
    }

    public int SeriesIndex { get; }

    public int ValueIndex { get; }

    public string Name
    {
        get => _planner.GetSeriesName(SeriesIndex);
        set => _planner.SetSeriesName(SeriesIndex, value);
    }
}

public sealed class ChartDataDialogValueCell
{
    private readonly ChartDataDialogPlanner _planner;

    internal ChartDataDialogValueCell(
        ChartDataDialogPlanner planner,
        int seriesIndex,
        int categoryIndex)
    {
        _planner = planner;
        SeriesIndex = seriesIndex;
        CategoryIndex = categoryIndex;
    }

    public int SeriesIndex { get; }

    public int CategoryIndex { get; }

    public double? Value
    {
        get => _planner.GetValue(SeriesIndex, CategoryIndex);
        set => _planner.SetValue(SeriesIndex, CategoryIndex, value);
    }
}

public sealed class ChartDataDialogTableRow
{
    private readonly ChartDataDialogPlanner _planner;

    internal ChartDataDialogTableRow(
        ChartDataDialogPlanner planner,
        int categoryIndex,
        IReadOnlyList<ChartDataDialogValueCell> values)
    {
        _planner = planner;
        CategoryIndex = categoryIndex;
        Values = values;
    }

    public int CategoryIndex { get; }

    public string Category
    {
        get => _planner.GetCategory(CategoryIndex);
        set => _planner.SetCategory(CategoryIndex, value);
    }

    public IReadOnlyList<ChartDataDialogValueCell> Values { get; }
}

public sealed record ChartDataDialogTableProjection(
    string CategoryColumnHeader,
    IReadOnlyList<ChartDataDialogSeriesColumn> SeriesColumns,
    IReadOnlyList<ChartDataDialogTableRow> Rows);

public sealed record ChartDataDialogCategoryEdit(
    int CategoryIndex,
    string? Category);

public sealed record ChartDataDialogSeriesNameEdit(
    int SeriesIndex,
    string? Name);

public sealed record ChartDataDialogValueEdit(
    int SeriesIndex,
    int CategoryIndex,
    object? Value);

public sealed record ChartDataDialogSurfacePlan(
    string CommandId,
    string Title,
    double Width,
    double Height,
    string AddSeriesLabel,
    string RemoveSeriesLabel,
    string AddCategoryLabel,
    string RemoveCategoryLabel,
    string OkLabel,
    string CancelLabel);

public sealed record ChartDataDialogCommitPlan(
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> SeriesNames,
    IReadOnlyList<IReadOnlyList<double?>> Values)
{
    public IEnumerable<IEnumerable<double?>> ValuesForCommand()
    {
        return Values.Select(values => (IEnumerable<double?>)values);
    }
}

public sealed class ChartDataDialogPlanner
{
    public const string EditDataCommandId = "freep.chart.edit-data";
    public const string EditDataCommandLabel = "Edit Data";
    public const string DialogTitle = "Edit Chart Data";
    public const string CategoryColumnHeader = "Category";
    public const string AddSeriesLabel = "+ Series";
    public const string RemoveSeriesLabel = "- Series";
    public const string AddCategoryLabel = "+ Category";
    public const string RemoveCategoryLabel = "- Category";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const string InvalidNumericValueMessage = "Enter a valid number or leave the value blank.";

    public const double DefaultDialogWidth = 640;
    public const double DefaultDialogHeight = 440;

    private readonly ChartDataGridPlanner _grid;

    private ChartDataDialogPlanner(ChartDataGridPlanner grid)
    {
        _grid = grid;
    }

    public int CategoryCount => _grid.CategoryCount;

    public int SeriesCount => _grid.SeriesCount;

    public static ChartDataDialogSurfacePlan BuildSurfacePlan()
    {
        return new ChartDataDialogSurfacePlan(
            EditDataCommandId,
            DialogTitle,
            DefaultDialogWidth,
            DefaultDialogHeight,
            AddSeriesLabel,
            RemoveSeriesLabel,
            AddCategoryLabel,
            RemoveCategoryLabel,
            OkLabel,
            CancelLabel);
    }

    public static ChartDataDialogPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return new ChartDataDialogPlanner(ChartDataGridPlanner.Create(
            chart.Categories,
            chart.Series.Select(series => series.Name),
            chart.Series.Select(series => series.Values)));
    }

    public string GetCategory(int categoryIndex)
    {
        return _grid.GetCategory(categoryIndex);
    }

    public void SetCategory(int categoryIndex, string? label)
    {
        _grid.SetCategory(categoryIndex, label);
    }

    public string GetSeriesName(int seriesIndex)
    {
        return _grid.GetSeriesName(seriesIndex);
    }

    public void SetSeriesName(int seriesIndex, string? name)
    {
        _grid.SetSeriesName(seriesIndex, name);
    }

    public double? GetValue(int seriesIndex, int categoryIndex)
    {
        return _grid.GetValue(seriesIndex, categoryIndex);
    }

    public void SetValue(int seriesIndex, int categoryIndex, double? value)
    {
        _grid.SetValue(seriesIndex, categoryIndex, value);
    }

    public void AddSeries()
    {
        _grid.AddSeries(DefaultSeriesName(_grid.SeriesCount + 1));
    }

    public void RemoveLastSeries()
    {
        _grid.RemoveLastSeries();
    }

    public void AddCategory()
    {
        _grid.AddCategory(DefaultCategoryName(_grid.CategoryCount + 1));
    }

    public void RemoveLastCategory()
    {
        _grid.RemoveLastCategory();
    }

    public ChartDataDialogTableProjection BuildTableProjection()
    {
        var columns = Enumerable.Range(0, _grid.SeriesCount)
            .Select(seriesIndex => new ChartDataDialogSeriesColumn(
                this,
                seriesIndex,
                valueIndex: seriesIndex))
            .ToList();

        var rows = Enumerable.Range(0, _grid.CategoryCount)
            .Select(categoryIndex => new ChartDataDialogTableRow(
                this,
                categoryIndex,
                columns
                    .Select(column => new ChartDataDialogValueCell(
                        this,
                        column.SeriesIndex,
                        categoryIndex))
                    .ToList()))
            .ToList();

        return new ChartDataDialogTableProjection(
            CategoryColumnHeader,
            columns,
            rows);
    }

    public void ApplyCategoryEdits(IEnumerable<ChartDataDialogCategoryEdit> categoryEdits)
    {
        ArgumentNullException.ThrowIfNull(categoryEdits);

        _grid.ApplyCategoryEdits(categoryEdits.Select(edit =>
            new ChartDataGridCategoryEdit(edit.CategoryIndex, edit.Category)));
    }

    public void ApplySeriesNameEdits(IEnumerable<ChartDataDialogSeriesNameEdit> seriesNameEdits)
    {
        ArgumentNullException.ThrowIfNull(seriesNameEdits);

        _grid.ApplySeriesNameEdits(seriesNameEdits.Select(edit =>
            new ChartDataGridSeriesNameEdit(edit.SeriesIndex, edit.Name)));
    }

    public void ApplyValueEdits(
        IEnumerable<ChartDataDialogValueEdit> valueEdits,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(valueEdits);
        ArgumentNullException.ThrowIfNull(culture);

        _grid.ApplyValueEdits(valueEdits.Select(edit =>
            new ChartDataGridValueEdit(
                edit.SeriesIndex,
                edit.CategoryIndex,
                ParseCellValue(edit.Value, culture))));
    }

    public ChartDataDialogCommitPlan BuildCommitPlan(
        IEnumerable<ChartDataDialogCategoryEdit>? categoryEdits = null)
    {
        if (categoryEdits is not null)
        {
            ApplyCategoryEdits(categoryEdits);
        }

        return new ChartDataDialogCommitPlan(
            CategoriesForCommit(),
            SeriesNamesForCommit(),
            ValuesForCommit());
    }

    public IReadOnlyList<string> CategoriesForCommit()
    {
        return _grid.CategoriesSnapshot();
    }

    public IReadOnlyList<string> SeriesNamesForCommit()
    {
        return _grid.SeriesNamesSnapshot();
    }

    public IReadOnlyList<IReadOnlyList<double?>> ValuesForCommit()
    {
        return _grid.ValuesSnapshot();
    }

    public static string FormatCellValue(double? value, CultureInfo culture)
    {
        return DialogNumericTextPolicy.FormatNullableDouble(value, culture);
    }

    public static double? ParseCellValue(object? value, CultureInfo culture)
    {
        return DialogNumericTextPolicy.ParseNullableDouble(value, culture);
    }

    public static string DefaultSeriesName(int oneBasedIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(oneBasedIndex, 1);
        return $"Series {oneBasedIndex}";
    }

    public static string DefaultCategoryName(int oneBasedIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(oneBasedIndex, 1);
        return $"Cat {oneBasedIndex}";
    }

}
