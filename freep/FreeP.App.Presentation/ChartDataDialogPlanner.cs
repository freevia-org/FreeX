using System.Globalization;
using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum ChartDataDialogValueKind
{
    Value,
    XValue,
    BubbleSize,
}

public sealed class ChartDataDialogSeriesColumn
{
    private readonly ChartDataDialogPlanner _planner;

    internal ChartDataDialogSeriesColumn(
        ChartDataDialogPlanner planner,
        int seriesIndex,
        int valueIndex,
        ChartDataDialogValueKind kind)
    {
        _planner = planner;
        SeriesIndex = seriesIndex;
        ValueIndex = valueIndex;
        Kind = kind;
    }

    public int SeriesIndex { get; }

    public int ValueIndex { get; }

    public ChartDataDialogValueKind Kind { get; }

    public bool IsSeriesNameColumn => Kind == ChartDataDialogValueKind.Value;

    public string Name
    {
        get => _planner.GetSeriesName(SeriesIndex);
        set
        {
            if (IsSeriesNameColumn)
                _planner.SetSeriesName(SeriesIndex, value);
        }
    }

    public string Header => _planner.GetSeriesColumnHeader(SeriesIndex, Kind);
}

public sealed class ChartDataDialogValueCell
{
    private readonly ChartDataDialogPlanner _planner;

    internal ChartDataDialogValueCell(
        ChartDataDialogPlanner planner,
        int seriesIndex,
        int categoryIndex,
        ChartDataDialogValueKind kind = ChartDataDialogValueKind.Value)
    {
        _planner = planner;
        SeriesIndex = seriesIndex;
        CategoryIndex = categoryIndex;
        Kind = kind;
    }

    public int SeriesIndex { get; }

    public int CategoryIndex { get; }

    public ChartDataDialogValueKind Kind { get; }

    public double? Value
    {
        get => _planner.GetValue(SeriesIndex, CategoryIndex, Kind);
        set => _planner.SetValue(SeriesIndex, CategoryIndex, value, Kind);
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
    object? Value,
    ChartDataDialogValueKind Kind = ChartDataDialogValueKind.Value);

public sealed record ChartDataDialogSurfacePlan(
    string CommandId,
    string Title,
    double Width,
    double Height,
    string AddSeriesLabel,
    string RemoveSeriesLabel,
    string MoveSeriesUpLabel,
    string MoveSeriesDownLabel,
    string AddCategoryLabel,
    string RemoveCategoryLabel,
    string MoveCategoryLeftLabel,
    string MoveCategoryRightLabel,
    string SwitchRowsAndColumnsLabel,
    string ChartTypeLabel,
    string OkLabel,
    string CancelLabel);

public sealed record ChartDataDialogChartTypeOption(ChartType Value, string Label);

public sealed record ChartDataDialogCommitPlan(
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> SeriesNames,
    IReadOnlyList<IReadOnlyList<double?>> Values,
    ChartType ChartType)
{
    public IReadOnlyList<IReadOnlyList<double?>> XValues { get; init; } =
        Array.Empty<IReadOnlyList<double?>>();

    public IReadOnlyList<IReadOnlyList<double?>> BubbleSizes { get; init; } =
        Array.Empty<IReadOnlyList<double?>>();

    public IEnumerable<IEnumerable<double?>> ValuesForCommand()
    {
        return Values.Select(values => (IEnumerable<double?>)values);
    }

    public IEnumerable<IEnumerable<double?>> XValuesForCommand() =>
        XValues.Select(values => (IEnumerable<double?>)values);

    public IEnumerable<IEnumerable<double?>> BubbleSizesForCommand() =>
        BubbleSizes.Select(values => (IEnumerable<double?>)values);
}

public sealed class ChartDataDialogPlanner
{
    public const string EditDataCommandId = "freep.chart.edit-data";
    public const string ChangeChartTypeCommandId = "freep.chart.change-type";
    public const string EditDataCommandLabel = "Edit Data";
    public const string DialogTitle = "Edit Chart Data";
    public const string CategoryColumnHeader = "Category";
    public const string AddSeriesLabel = "+ Series";
    public const string RemoveSeriesLabel = "- Series";
    public const string MoveSeriesUpLabel = "Move Series Up";
    public const string MoveSeriesDownLabel = "Move Series Down";
    public const string AddCategoryLabel = "+ Category";
    public const string RemoveCategoryLabel = "- Category";
    public const string MoveCategoryLeftLabel = "Move Category Left";
    public const string MoveCategoryRightLabel = "Move Category Right";
    public const string SwitchRowsAndColumnsLabel = "Switch Row/Column";
    public const string ChartTypeLabel = "Chart Type";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const string InvalidNumericValueMessage = "Enter a valid number or leave the value blank.";

    public const double DefaultDialogWidth = 640;
    public const double DefaultDialogHeight = 440;

    private readonly ChartDataGridPlanner _grid;
    private readonly List<List<double?>> _xValues;
    private readonly List<List<double?>> _bubbleSizes;
    private ChartType _chartType;

    private ChartDataDialogPlanner(
        ChartDataGridPlanner grid,
        ChartType chartType,
        List<List<double?>> xValues,
        List<List<double?>> bubbleSizes)
    {
        _grid = grid;
        _chartType = chartType;
        _xValues = xValues;
        _bubbleSizes = bubbleSizes;
    }

    public static IReadOnlyList<ChartDataDialogChartTypeOption> ChartTypeOptions { get; } =
        Enum.GetValues<ChartType>()
            .Where(chartType => chartType != ChartType.Unknown)
            .Select(chartType => new ChartDataDialogChartTypeOption(
                chartType,
                FormatChartTypeLabel(chartType)))
            .ToArray();

    public static string ChangeChartTypeOptionCommandId(ChartType chartType) =>
        $"{ChangeChartTypeCommandId}.{chartType.ToString().ToLowerInvariant()}";

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
            MoveSeriesUpLabel,
            MoveSeriesDownLabel,
            AddCategoryLabel,
            RemoveCategoryLabel,
            MoveCategoryLeftLabel,
            MoveCategoryRightLabel,
            SwitchRowsAndColumnsLabel,
            ChartTypeLabel,
            OkLabel,
            CancelLabel);
    }

    public static ChartDataDialogPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var planner = new ChartDataDialogPlanner(ChartDataGridPlanner.Create(
            chart.Categories,
            chart.Series.Select(series => series.Name),
            chart.Series.Select(series => series.Values)),
            chart.ChartType,
            SnapshotCoordinates(chart, series => series.XValues),
            SnapshotCoordinates(chart, series => series.BubbleSizes));
        planner.EnsureCoordinateShape();
        planner.SeedMissingCoordinates();
        return planner;
    }

    public ChartType SelectedChartType => _chartType;

    public void SetChartType(ChartType chartType)
    {
        if (chartType != ChartType.Unknown)
        {
            _chartType = chartType;
            EnsureCoordinateShape();
            if (chartType == ChartType.Stock)
            {
                EnsureStockSeriesShape();
            }
            if (chartType == ChartType.Funnel)
            {
                EnsureFunnelSeriesShape();
            }
            if (IsScatterLike(chartType))
            {
                SeedMissingCoordinates();
            }
        }
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
        return GetValue(seriesIndex, categoryIndex, ChartDataDialogValueKind.Value);
    }

    public void SetValue(int seriesIndex, int categoryIndex, double? value)
    {
        SetValue(seriesIndex, categoryIndex, value, ChartDataDialogValueKind.Value);
    }

    public double? GetValue(
        int seriesIndex,
        int categoryIndex,
        ChartDataDialogValueKind kind)
    {
        return kind switch
        {
            ChartDataDialogValueKind.Value => _grid.GetValue(seriesIndex, categoryIndex),
            ChartDataDialogValueKind.XValue => GetCoordinate(_xValues, seriesIndex, categoryIndex),
            ChartDataDialogValueKind.BubbleSize => GetCoordinate(_bubbleSizes, seriesIndex, categoryIndex),
            _ => null,
        };
    }

    public void SetValue(
        int seriesIndex,
        int categoryIndex,
        double? value,
        ChartDataDialogValueKind kind)
    {
        switch (kind)
        {
            case ChartDataDialogValueKind.Value:
                _grid.SetValue(seriesIndex, categoryIndex, value);
                break;
            case ChartDataDialogValueKind.XValue:
                SetCoordinate(_xValues, seriesIndex, categoryIndex, value);
                break;
            case ChartDataDialogValueKind.BubbleSize:
                SetCoordinate(_bubbleSizes, seriesIndex, categoryIndex, value);
                break;
        }
    }

    public string GetSeriesColumnHeader(int seriesIndex, ChartDataDialogValueKind kind)
    {
        var name = GetSeriesName(seriesIndex);
        return kind switch
        {
            ChartDataDialogValueKind.XValue => $"{name} X",
            ChartDataDialogValueKind.BubbleSize => $"{name} Size",
            _ => name,
        };
    }

    public void AddSeries()
    {
        _grid.AddSeries(DefaultSeriesName(_grid.SeriesCount + 1));
        _xValues.Add(NewCoordinateRow());
        _bubbleSizes.Add(NewCoordinateRow());
        SeedMissingCoordinates();
    }

    public void RemoveLastSeries()
    {
        RemoveSeriesAt(_grid.SeriesCount - 1);
    }

    public bool RemoveSeriesAt(int seriesIndex)
    {
        if (!_grid.RemoveSeriesAt(seriesIndex))
            return false;

        RemoveCoordinateRow(_xValues, seriesIndex);
        RemoveCoordinateRow(_bubbleSizes, seriesIndex);
        return true;
    }

    public bool MoveSeries(int seriesIndex, int targetIndex)
    {
        if (!_grid.MoveSeries(seriesIndex, targetIndex))
            return false;

        MoveCoordinateRow(_xValues, seriesIndex, targetIndex);
        MoveCoordinateRow(_bubbleSizes, seriesIndex, targetIndex);
        return true;
    }

    public bool MoveCategory(int categoryIndex, int targetIndex)
    {
        if (!_grid.MoveCategory(categoryIndex, targetIndex))
            return false;

        MoveCoordinateValue(_xValues, categoryIndex, targetIndex);
        MoveCoordinateValue(_bubbleSizes, categoryIndex, targetIndex);
        return true;
    }

    public void AddCategory()
    {
        _grid.AddCategory(DefaultCategoryName(_grid.CategoryCount + 1));
        EnsureCoordinateShape();
        var categoryIndex = _grid.CategoryCount - 1;
        foreach (var values in _xValues)
            values[categoryIndex] = IsScatterLike(_chartType) ? categoryIndex + 1.0 : null;
        foreach (var values in _bubbleSizes)
            values[categoryIndex] = _chartType == ChartType.Bubble ? 1.0 : null;
    }

    public void RemoveLastCategory()
    {
        RemoveCategoryAt(_grid.CategoryCount - 1);
    }

    public bool RemoveCategoryAt(int categoryIndex)
    {
        if (!_grid.RemoveCategoryAt(categoryIndex))
            return false;

        RemoveCoordinateValue(_xValues, categoryIndex);
        RemoveCoordinateValue(_bubbleSizes, categoryIndex);
        return true;
    }

    public void SwitchRowsAndColumns()
    {
        var oldXValues = CloneMatrix(_xValues);
        var oldBubbleSizes = CloneMatrix(_bubbleSizes);
        _grid.SwitchRowsAndColumns();
        ReplaceMatrix(_xValues, Transpose(oldXValues));
        ReplaceMatrix(_bubbleSizes, Transpose(oldBubbleSizes));
    }

    public ChartDataDialogTableProjection BuildTableProjection()
    {
        var columns = new List<ChartDataDialogSeriesColumn>();
        foreach (var seriesIndex in Enumerable.Range(0, _grid.SeriesCount))
        {
            foreach (var kind in ColumnKinds(_chartType))
            {
                columns.Add(new ChartDataDialogSeriesColumn(
                    this,
                    seriesIndex,
                    valueIndex: columns.Count,
                    kind));
            }
        }

        var rows = Enumerable.Range(0, _grid.CategoryCount)
            .Select(categoryIndex => new ChartDataDialogTableRow(
                this,
                categoryIndex,
                columns
                    .Select(column => new ChartDataDialogValueCell(
                        this,
                        column.SeriesIndex,
                        categoryIndex,
                        column.Kind))
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

        foreach (var edit in valueEdits)
        {
            SetValue(
                edit.SeriesIndex,
                edit.CategoryIndex,
                ParseCellValue(edit.Value, culture),
                edit.Kind);
        }
    }

    public ChartDataDialogCommitPlan BuildCommitPlan(
        IEnumerable<ChartDataDialogCategoryEdit>? categoryEdits = null)
    {
        if (categoryEdits is not null)
        {
            ApplyCategoryEdits(categoryEdits);
        }

        EnsureCoordinateShape();
        return new ChartDataDialogCommitPlan(
            CategoriesForCommit(),
            SeriesNamesForCommit(),
            ValuesForCommit(),
            _chartType)
        {
            XValues = XValuesForCommit(),
            BubbleSizes = BubbleSizesForCommit(),
        };
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

    public IReadOnlyList<IReadOnlyList<double?>> XValuesForCommit() => SnapshotMatrix(_xValues);

    public IReadOnlyList<IReadOnlyList<double?>> BubbleSizesForCommit() => SnapshotMatrix(_bubbleSizes);

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

    private static string FormatChartTypeLabel(ChartType chartType)
    {
        return chartType switch
        {
            ChartType.ColumnClustered => "Clustered Column",
            ChartType.ColumnStacked => "Stacked Column",
            ChartType.ColumnStacked100 => "100% Stacked Column",
            ChartType.BarClustered => "Clustered Bar",
            ChartType.BarStacked => "Stacked Bar",
            ChartType.BarStacked100 => "100% Stacked Bar",
            ChartType.Line => "Line",
            ChartType.LineMarkers => "Line with Markers",
            ChartType.Pie => "Pie",
            ChartType.Area => "Area",
            ChartType.AreaStacked => "Stacked Area",
            ChartType.Scatter => "Scatter",
            ChartType.Doughnut => "Doughnut",
            ChartType.Radar => "Radar",
            ChartType.Bubble => "Bubble",
            ChartType.Stock => "Stock",
            ChartType.Surface => "Surface",
            ChartType.Surface3D => "3-D Surface",
            ChartType.Funnel => "Funnel",
            _ => chartType.ToString(),
        };
    }

    private static bool IsScatterLike(ChartType chartType) =>
        chartType is ChartType.Scatter or ChartType.Bubble;

    private void EnsureStockSeriesShape()
    {
        // A stockChart's OHLC roles are carried by four series. Keep existing
        // values when changing an ordinary chart to Stock, then add only the
        // missing roles so the result is immediately renderable and editable.
        string[] names = ["Open", "High", "Low", "Close"];
        while (_grid.SeriesCount < names.Length)
            _grid.AddSeries(names[_grid.SeriesCount]);

        if (_grid.SeriesCount > names.Length &&
            names.Skip(1).All(name => FindSeriesIndex(name) >= 0))
        {
            return;
        }

        for (var index = 0; index < names.Length; index++)
            _grid.SetSeriesName(index, names[index]);
    }

    private void EnsureFunnelSeriesShape()
    {
        if (_grid.SeriesCount > 0)
            return;

        _grid.AddSeries("Value");
        for (var categoryIndex = 0; categoryIndex < _grid.CategoryCount; categoryIndex++)
            _grid.SetValue(0, categoryIndex, 0);
    }

    private int FindSeriesIndex(string name)
    {
        for (var index = 0; index < _grid.SeriesCount; index++)
        {
            if (string.Equals(_grid.GetSeriesName(index), name, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private static IReadOnlyList<ChartDataDialogValueKind> ColumnKinds(ChartType chartType) =>
        chartType switch
        {
            ChartType.Scatter =>
                [ChartDataDialogValueKind.XValue, ChartDataDialogValueKind.Value],
            ChartType.Bubble =>
                [ChartDataDialogValueKind.XValue, ChartDataDialogValueKind.Value, ChartDataDialogValueKind.BubbleSize],
            _ => [ChartDataDialogValueKind.Value],
        };

    private static List<List<double?>> SnapshotCoordinates(
        ChartShape chart,
        Func<ChartSeries, List<double?>> selector)
    {
        return chart.Series
            .Select(series => NormalizeCoordinateRow(selector(series), chart.Categories.Count))
            .ToList();
    }

    private static List<double?> NormalizeCoordinateRow(List<double?> source, int count)
    {
        var result = source.Take(count).ToList();
        while (result.Count < count)
            result.Add(null);
        return result;
    }

    private void EnsureCoordinateShape()
    {
        while (_xValues.Count < _grid.SeriesCount)
            _xValues.Add(NewCoordinateRow());
        while (_bubbleSizes.Count < _grid.SeriesCount)
            _bubbleSizes.Add(NewCoordinateRow());
        while (_xValues.Count > _grid.SeriesCount)
            _xValues.RemoveAt(_xValues.Count - 1);
        while (_bubbleSizes.Count > _grid.SeriesCount)
            _bubbleSizes.RemoveAt(_bubbleSizes.Count - 1);

        foreach (var values in _xValues)
            NormalizeCoordinateRowInPlace(values);
        foreach (var values in _bubbleSizes)
            NormalizeCoordinateRowInPlace(values);
    }

    private void NormalizeCoordinateRowInPlace(List<double?> values)
    {
        while (values.Count < _grid.CategoryCount)
            values.Add(null);
        while (values.Count > _grid.CategoryCount)
            values.RemoveAt(values.Count - 1);
    }

    private void SeedMissingCoordinates()
    {
        if (!IsScatterLike(_chartType))
            return;

        EnsureCoordinateShape();
        for (var seriesIndex = 0; seriesIndex < _xValues.Count; seriesIndex++)
        {
            for (var categoryIndex = 0; categoryIndex < _grid.CategoryCount; categoryIndex++)
            {
                _xValues[seriesIndex][categoryIndex] ??= categoryIndex + 1.0;
                if (_chartType == ChartType.Bubble)
                    _bubbleSizes[seriesIndex][categoryIndex] ??= 1.0;
            }
        }
    }

    private List<double?> NewCoordinateRow() =>
        Enumerable.Repeat<double?>(null, _grid.CategoryCount).ToList();

    private static double? GetCoordinate(
        List<List<double?>> matrix,
        int seriesIndex,
        int categoryIndex) =>
        seriesIndex >= 0 && seriesIndex < matrix.Count &&
        categoryIndex >= 0 && categoryIndex < matrix[seriesIndex].Count
            ? matrix[seriesIndex][categoryIndex]
            : null;

    private static void SetCoordinate(
        List<List<double?>> matrix,
        int seriesIndex,
        int categoryIndex,
        double? value)
    {
        if (seriesIndex >= 0 && seriesIndex < matrix.Count &&
            categoryIndex >= 0 && categoryIndex < matrix[seriesIndex].Count)
        {
            matrix[seriesIndex][categoryIndex] = value;
        }
    }

    private static void RemoveCoordinateRow(List<List<double?>> matrix, int rowIndex)
    {
        if (rowIndex >= 0 && rowIndex < matrix.Count)
            matrix.RemoveAt(rowIndex);
    }

    private static void MoveCoordinateRow(List<List<double?>> matrix, int sourceIndex, int targetIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= matrix.Count ||
            targetIndex < 0 || targetIndex >= matrix.Count || sourceIndex == targetIndex)
            return;

        var row = matrix[sourceIndex];
        matrix.RemoveAt(sourceIndex);
        matrix.Insert(targetIndex, row);
    }

    private static void RemoveCoordinateValue(List<List<double?>> matrix, int categoryIndex)
    {
        foreach (var values in matrix)
        {
            if (categoryIndex >= 0 && categoryIndex < values.Count)
                values.RemoveAt(categoryIndex);
        }
    }

    private static void MoveCoordinateValue(
        List<List<double?>> matrix,
        int categoryIndex,
        int targetIndex)
    {
        foreach (var values in matrix)
        {
            if (categoryIndex < 0 || categoryIndex >= values.Count ||
                targetIndex < 0 || targetIndex >= values.Count ||
                categoryIndex == targetIndex)
                continue;

            var value = values[categoryIndex];
            values.RemoveAt(categoryIndex);
            values.Insert(targetIndex, value);
        }
    }

    private static List<List<double?>> CloneMatrix(IEnumerable<List<double?>> matrix) =>
        matrix.Select(values => values.ToList()).ToList();

    private static void ReplaceMatrix(List<List<double?>> target, IEnumerable<List<double?>> source)
    {
        target.Clear();
        target.AddRange(source.Select(values => values.ToList()));
    }

    private static List<List<double?>> Transpose(IReadOnlyList<List<double?>> matrix)
    {
        if (matrix.Count == 0)
            return [];

        var width = matrix.Max(values => values.Count);
        return Enumerable.Range(0, width)
            .Select(columnIndex => matrix
                .Select(values => columnIndex < values.Count ? values[columnIndex] : null)
                .ToList())
            .ToList();
    }

    private static IReadOnlyList<IReadOnlyList<double?>> SnapshotMatrix(
        IEnumerable<List<double?>> matrix) =>
        matrix.Select(values => (IReadOnlyList<double?>)values.ToList()).ToList();

}
