using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed class ChartDataDialogPlanner
{
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
            chart.Categories.ToList(),
            chart.Series.Select(series => series.Name).ToList(),
            chart.Series.Select(series => series.Values.ToList()).ToList());
    }

    public string GetCategory(int categoryIndex)
    {
        return IsValidCategoryIndex(categoryIndex)
            ? _categories[categoryIndex]
            : string.Empty;
    }

    public void SetCategory(int categoryIndex, string label)
    {
        if (IsValidCategoryIndex(categoryIndex))
        {
            _categories[categoryIndex] = label;
        }
    }

    public string GetSeriesName(int seriesIndex)
    {
        return IsValidSeriesIndex(seriesIndex)
            ? _seriesNames[seriesIndex]
            : string.Empty;
    }

    public void SetSeriesName(int seriesIndex, string name)
    {
        if (IsValidSeriesIndex(seriesIndex))
        {
            _seriesNames[seriesIndex] = name;
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
        _seriesNames.Add($"Series {_seriesNames.Count + 1}");
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
        _categories.Add($"Cat {_categories.Count + 1}");
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
}
