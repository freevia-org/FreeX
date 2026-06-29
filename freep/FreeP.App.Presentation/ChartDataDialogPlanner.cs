using System.Globalization;
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
    public const string CategoryColumnHeader = "Category";

    private readonly List<string> _categories;
    private readonly List<string> _seriesNames;
    private readonly List<List<double?>> _values;

    private ChartDataDialogPlanner(
        List<string> categories,
        List<string> seriesNames,
        List<List<double?>> values)
    {
        _categories = categories;
        _seriesNames = seriesNames;
        _values = values;
        EnsureRectangular();
    }

    public int CategoryCount => _categories.Count;

    public int SeriesCount => _seriesNames.Count;

    public static ChartDataDialogPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return new ChartDataDialogPlanner(
            chart.Categories.Select(NormalizeLabel).ToList(),
            chart.Series.Select(series => NormalizeLabel(series.Name)).ToList(),
            chart.Series.Select(series => series.Values.ToList()).ToList());
    }

    public string GetCategory(int categoryIndex)
    {
        return IsValidCategoryIndex(categoryIndex)
            ? _categories[categoryIndex]
            : string.Empty;
    }

    public void SetCategory(int categoryIndex, string? label)
    {
        if (IsValidCategoryIndex(categoryIndex))
        {
            _categories[categoryIndex] = NormalizeLabel(label);
        }
    }

    public string GetSeriesName(int seriesIndex)
    {
        return IsValidSeriesIndex(seriesIndex)
            ? _seriesNames[seriesIndex]
            : string.Empty;
    }

    public void SetSeriesName(int seriesIndex, string? name)
    {
        if (IsValidSeriesIndex(seriesIndex))
        {
            _seriesNames[seriesIndex] = NormalizeLabel(name);
        }
    }

    public double? GetValue(int seriesIndex, int categoryIndex)
    {
        if (!IsValidSeriesIndex(seriesIndex) || !IsValidCategoryIndex(categoryIndex))
        {
            return null;
        }

        return _values[seriesIndex][categoryIndex];
    }

    public void SetValue(int seriesIndex, int categoryIndex, double? value)
    {
        if (!IsValidSeriesIndex(seriesIndex) || !IsValidCategoryIndex(categoryIndex))
        {
            return;
        }

        _values[seriesIndex][categoryIndex] = value;
    }

    public void AddSeries()
    {
        _seriesNames.Add(DefaultSeriesName(_seriesNames.Count + 1));
        _values.Add(Enumerable.Repeat((double?)null, _categories.Count).ToList());
        EnsureRectangular();
    }

    public void RemoveLastSeries()
    {
        if (_seriesNames.Count == 0)
        {
            return;
        }

        _seriesNames.RemoveAt(_seriesNames.Count - 1);
        if (_values.Count > 0)
        {
            _values.RemoveAt(_values.Count - 1);
        }
    }

    public void AddCategory()
    {
        _categories.Add(DefaultCategoryName(_categories.Count + 1));
        foreach (var seriesValues in _values)
        {
            seriesValues.Add(null);
        }
    }

    public void RemoveLastCategory()
    {
        if (_categories.Count == 0)
        {
            return;
        }

        _categories.RemoveAt(_categories.Count - 1);
        foreach (var seriesValues in _values)
        {
            if (seriesValues.Count > 0)
            {
                seriesValues.RemoveAt(seriesValues.Count - 1);
            }
        }
    }

    public ChartDataDialogTableProjection BuildTableProjection()
    {
        var columns = Enumerable.Range(0, _seriesNames.Count)
            .Select(seriesIndex => new ChartDataDialogSeriesColumn(
                this,
                seriesIndex,
                valueIndex: seriesIndex))
            .ToList();

        var rows = Enumerable.Range(0, _categories.Count)
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

        foreach (var edit in categoryEdits)
        {
            SetCategory(edit.CategoryIndex, edit.Category);
        }
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
        return _categories.ToList();
    }

    public IReadOnlyList<string> SeriesNamesForCommit()
    {
        return _seriesNames.ToList();
    }

    public IReadOnlyList<IReadOnlyList<double?>> ValuesForCommit()
    {
        return _values.Select(values => (IReadOnlyList<double?>)values.ToList()).ToList();
    }

    public static string FormatCellValue(double? value, CultureInfo culture)
    {
        return value is double numeric
            ? numeric.ToString("G6", culture)
            : string.Empty;
    }

    public static double? ParseCellValue(object? value, CultureInfo culture)
    {
        if (value is not string text)
        {
            return null;
        }

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        return double.TryParse(trimmed, NumberStyles.Any, culture, out double numeric)
            ? numeric
            : null;
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

    private void EnsureRectangular()
    {
        int categoryCount = _categories.Count;
        foreach (var seriesValues in _values)
        {
            while (seriesValues.Count < categoryCount)
            {
                seriesValues.Add(null);
            }

            while (seriesValues.Count > categoryCount)
            {
                seriesValues.RemoveAt(seriesValues.Count - 1);
            }
        }
    }

    private bool IsValidCategoryIndex(int categoryIndex)
    {
        return categoryIndex >= 0 && categoryIndex < _categories.Count;
    }

    private bool IsValidSeriesIndex(int seriesIndex)
    {
        return seriesIndex >= 0 && seriesIndex < _seriesNames.Count;
    }

    private static string NormalizeLabel(string? label)
    {
        return label ?? string.Empty;
    }
}
