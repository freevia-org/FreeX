namespace Free.Shared.AppServices;

public sealed record ChartDataGridCategoryEdit(int CategoryIndex, string? Category);

public sealed record ChartDataGridSeriesNameEdit(int SeriesIndex, string? Name);

public sealed record ChartDataGridValueEdit(int SeriesIndex, int CategoryIndex, double? Value);

public sealed class ChartDataGridPlanner
{
    private readonly List<string> _categories;
    private readonly List<string> _seriesNames;
    private readonly List<List<double?>> _values;

    private ChartDataGridPlanner(
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

    public static ChartDataGridPlanner Create(
        IEnumerable<string?> categories,
        IEnumerable<string?> seriesNames,
        IEnumerable<IEnumerable<double?>> values)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(seriesNames);
        ArgumentNullException.ThrowIfNull(values);

        return new ChartDataGridPlanner(
            categories.Select(NormalizeLabel).ToList(),
            seriesNames.Select(NormalizeLabel).ToList(),
            values.Select(row => row.ToList()).ToList());
    }

    public string GetCategory(int categoryIndex) =>
        IsValidCategoryIndex(categoryIndex)
            ? _categories[categoryIndex]
            : string.Empty;

    public void SetCategory(int categoryIndex, string? label)
    {
        if (IsValidCategoryIndex(categoryIndex))
            _categories[categoryIndex] = NormalizeLabel(label);
    }

    public string GetSeriesName(int seriesIndex) =>
        IsValidSeriesIndex(seriesIndex)
            ? _seriesNames[seriesIndex]
            : string.Empty;

    public void SetSeriesName(int seriesIndex, string? name)
    {
        if (IsValidSeriesIndex(seriesIndex))
            _seriesNames[seriesIndex] = NormalizeLabel(name);
    }

    public double? GetValue(int seriesIndex, int categoryIndex)
    {
        return IsValidSeriesIndex(seriesIndex) && IsValidCategoryIndex(categoryIndex)
            ? _values[seriesIndex][categoryIndex]
            : null;
    }

    public void SetValue(int seriesIndex, int categoryIndex, double? value)
    {
        if (IsValidSeriesIndex(seriesIndex) && IsValidCategoryIndex(categoryIndex))
            _values[seriesIndex][categoryIndex] = value;
    }

    public void AddSeries(string name)
    {
        _seriesNames.Add(NormalizeLabel(name));
        _values.Add(Enumerable.Repeat((double?)null, _categories.Count).ToList());
    }

    public void RemoveLastSeries()
    {
        RemoveSeriesAt(_seriesNames.Count - 1);
    }

    public bool RemoveSeriesAt(int seriesIndex)
    {
        if (!IsValidSeriesIndex(seriesIndex))
            return false;

        _seriesNames.RemoveAt(seriesIndex);
        if (seriesIndex < _values.Count)
            _values.RemoveAt(seriesIndex);
        return true;
    }

    public bool MoveSeries(int seriesIndex, int targetIndex)
    {
        if (!IsValidSeriesIndex(seriesIndex) || !IsValidSeriesIndex(targetIndex) ||
            seriesIndex == targetIndex)
            return false;

        var name = _seriesNames[seriesIndex];
        _seriesNames.RemoveAt(seriesIndex);
        _seriesNames.Insert(targetIndex, name);

        var values = _values[seriesIndex];
        _values.RemoveAt(seriesIndex);
        _values.Insert(targetIndex, values);
        return true;
    }

    public void AddCategory(string category)
    {
        _categories.Add(NormalizeLabel(category));
        foreach (var seriesValues in _values)
            seriesValues.Add(null);
    }

    public void RemoveLastCategory()
    {
        RemoveCategoryAt(_categories.Count - 1);
    }

    public bool RemoveCategoryAt(int categoryIndex)
    {
        if (!IsValidCategoryIndex(categoryIndex))
            return false;

        _categories.RemoveAt(categoryIndex);
        foreach (var seriesValues in _values)
        {
            if (categoryIndex < seriesValues.Count)
                seriesValues.RemoveAt(categoryIndex);
        }
        return true;
    }

    /// <summary>
    /// Transposes the chart data matrix, making the current series names the category
    /// labels and the current category labels the new series names.
    /// </summary>
    public void SwitchRowsAndColumns()
    {
        var oldCategories = _categories.ToList();
        var oldSeriesNames = _seriesNames.ToList();
        var oldValues = _values
            .Select(values => values.ToList())
            .ToList();

        _categories.Clear();
        _categories.AddRange(oldSeriesNames);

        _seriesNames.Clear();
        _seriesNames.AddRange(oldCategories);

        _values.Clear();
        for (var newSeriesIndex = 0; newSeriesIndex < oldCategories.Count; newSeriesIndex++)
        {
            var values = new List<double?>(oldSeriesNames.Count);
            for (var newCategoryIndex = 0; newCategoryIndex < oldSeriesNames.Count; newCategoryIndex++)
            {
                values.Add(
                    newCategoryIndex < oldValues.Count &&
                    newSeriesIndex < oldValues[newCategoryIndex].Count
                        ? oldValues[newCategoryIndex][newSeriesIndex]
                        : null);
            }

            _values.Add(values);
        }

        EnsureRectangular();
    }

    public void ApplyCategoryEdits(IEnumerable<ChartDataGridCategoryEdit> categoryEdits)
    {
        ArgumentNullException.ThrowIfNull(categoryEdits);

        foreach (var edit in categoryEdits)
            SetCategory(edit.CategoryIndex, edit.Category);
    }

    public void ApplySeriesNameEdits(IEnumerable<ChartDataGridSeriesNameEdit> seriesNameEdits)
    {
        ArgumentNullException.ThrowIfNull(seriesNameEdits);

        foreach (var edit in seriesNameEdits)
            SetSeriesName(edit.SeriesIndex, edit.Name);
    }

    public void ApplyValueEdits(IEnumerable<ChartDataGridValueEdit> valueEdits)
    {
        ArgumentNullException.ThrowIfNull(valueEdits);

        foreach (var edit in valueEdits)
            SetValue(edit.SeriesIndex, edit.CategoryIndex, edit.Value);
    }

    public IReadOnlyList<string> CategoriesSnapshot() => _categories.ToList();

    public IReadOnlyList<string> SeriesNamesSnapshot() => _seriesNames.ToList();

    public IReadOnlyList<IReadOnlyList<double?>> ValuesSnapshot() =>
        _values.Select(values => (IReadOnlyList<double?>)values.ToList()).ToList();

    private void EnsureRectangular()
    {
        var categoryCount = _categories.Count;
        while (_values.Count < _seriesNames.Count)
            _values.Add(Enumerable.Repeat((double?)null, categoryCount).ToList());

        while (_values.Count > _seriesNames.Count)
            _values.RemoveAt(_values.Count - 1);

        foreach (var seriesValues in _values)
        {
            while (seriesValues.Count < categoryCount)
                seriesValues.Add(null);

            while (seriesValues.Count > categoryCount)
                seriesValues.RemoveAt(seriesValues.Count - 1);
        }
    }

    private bool IsValidCategoryIndex(int categoryIndex) =>
        categoryIndex >= 0 && categoryIndex < _categories.Count;

    private bool IsValidSeriesIndex(int seriesIndex) =>
        seriesIndex >= 0 && seriesIndex < _seriesNames.Count;

    private static string NormalizeLabel(string? label) => label ?? string.Empty;
}
