namespace FreeP.Core.Model;

// ════════════════════════════════════════════════════════════════════════════════
// CHART DATA COMMANDS  (Wave 9B)
//
// All commands operate on a ChartShape identified by (slideIndex, shapeId).
// The value matrix is kept rectangular — every series has one value per category.
// Each command captures the prior state in Apply so that Revert is exact.
// ════════════════════════════════════════════════════════════════════════════════

file static class ChartHelper
{
    internal static ChartShape? Find(Presentation p, int slideIndex, uint shapeId)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return null;
        var shape = p.Slides[slideIndex].Shapes.FirstOrDefault(s => s.Id == shapeId);
        return shape?.Chart;
    }
}

// ── Cell value ────────────────────────────────────────────────────────────────

/// <summary>
/// Sets the numeric value at [<paramref name="seriesIndex"/>][<paramref name="categoryIndex"/>]
/// in the chart data matrix.  Out-of-range indices are silently ignored.
/// </summary>
public sealed class SetChartCellValueCommand : IPresentationCommand
{
    private readonly int    _slideIndex;
    private readonly uint   _shapeId;
    private readonly int    _seriesIndex;
    private readonly int    _categoryIndex;
    private readonly double _newValue;
    private double          _oldValue;

    public SetChartCellValueCommand(
        int slideIndex, uint shapeId,
        int seriesIndex, int categoryIndex,
        double value)
    {
        _slideIndex    = slideIndex;
        _shapeId       = shapeId;
        _seriesIndex   = seriesIndex;
        _categoryIndex = categoryIndex;
        _newValue      = value;
    }

    public string Label => "Edit Chart Value";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_seriesIndex < 0 || _seriesIndex >= chart.Series.Count) return;
        var values = chart.Series[_seriesIndex].Values;
        if (_categoryIndex < 0 || _categoryIndex >= values.Count) return;
        _oldValue = values[_categoryIndex] ?? 0.0;
        values[_categoryIndex] = _newValue;
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_seriesIndex < 0 || _seriesIndex >= chart.Series.Count) return;
        var values = chart.Series[_seriesIndex].Values;
        if (_categoryIndex < 0 || _categoryIndex >= values.Count) return;
        values[_categoryIndex] = _oldValue;
    }
}

// ── Category label ────────────────────────────────────────────────────────────

/// <summary>
/// Renames the category at <paramref name="categoryIndex"/>.
/// Out-of-range indices are silently ignored.
/// </summary>
public sealed class SetChartCategoryLabelCommand : IPresentationCommand
{
    private readonly int    _slideIndex;
    private readonly uint   _shapeId;
    private readonly int    _categoryIndex;
    private readonly string _newLabel;
    private string          _oldLabel = string.Empty;

    public SetChartCategoryLabelCommand(
        int slideIndex, uint shapeId,
        int categoryIndex, string label)
    {
        _slideIndex    = slideIndex;
        _shapeId       = shapeId;
        _categoryIndex = categoryIndex;
        _newLabel      = label;
    }

    public string Label => "Rename Category";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_categoryIndex < 0 || _categoryIndex >= chart.Categories.Count) return;
        _oldLabel                       = chart.Categories[_categoryIndex];
        chart.Categories[_categoryIndex] = _newLabel;
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_categoryIndex < 0 || _categoryIndex >= chart.Categories.Count) return;
        chart.Categories[_categoryIndex] = _oldLabel;
    }
}

// ── Series name ───────────────────────────────────────────────────────────────

/// <summary>
/// Renames the series at <paramref name="seriesIndex"/>.
/// Out-of-range indices are silently ignored.
/// </summary>
public sealed class SetChartSeriesNameCommand : IPresentationCommand
{
    private readonly int    _slideIndex;
    private readonly uint   _shapeId;
    private readonly int    _seriesIndex;
    private readonly string _newName;
    private string          _oldName = string.Empty;

    public SetChartSeriesNameCommand(
        int slideIndex, uint shapeId,
        int seriesIndex, string name)
    {
        _slideIndex  = slideIndex;
        _shapeId     = shapeId;
        _seriesIndex = seriesIndex;
        _newName     = name;
    }

    public string Label => "Rename Series";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_seriesIndex < 0 || _seriesIndex >= chart.Series.Count) return;
        _oldName                      = chart.Series[_seriesIndex].Name;
        chart.Series[_seriesIndex].Name = _newName;
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_seriesIndex < 0 || _seriesIndex >= chart.Series.Count) return;
        chart.Series[_seriesIndex].Name = _oldName;
    }
}

// ── Add / remove series ───────────────────────────────────────────────────────

/// <summary>
/// Appends a new series to the chart.  The new series gets one <c>0.0</c> value per
/// existing category so the value matrix stays rectangular.
/// Revert removes the series by reference.
/// </summary>
public sealed class AddChartSeriesCommand : IPresentationCommand
{
    private readonly int         _slideIndex;
    private readonly uint        _shapeId;
    private readonly string      _name;
    private ChartSeries?         _added;

    public AddChartSeriesCommand(int slideIndex, uint shapeId, string name = "New Series")
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _name       = name;
    }

    public string Label => "Add Series";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;

        _added = new ChartSeries { Name = _name };
        for (int i = 0; i < chart.Categories.Count; i++)
            _added.Values.Add(0.0);

        chart.Series.Add(_added);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null || _added is null) return;
        chart.Series.Remove(_added);
    }
}

/// <summary>
/// Removes the series at <paramref name="seriesIndex"/> from the chart.
/// Captures the series instance + its index for undo.
/// </summary>
public sealed class RemoveChartSeriesCommand : IPresentationCommand
{
    private readonly int    _slideIndex;
    private readonly uint   _shapeId;
    private readonly int    _seriesIndex;
    private ChartSeries?    _captured;

    public RemoveChartSeriesCommand(int slideIndex, uint shapeId, int seriesIndex)
    {
        _slideIndex  = slideIndex;
        _shapeId     = shapeId;
        _seriesIndex = seriesIndex;
    }

    public string Label => "Remove Series";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_seriesIndex < 0 || _seriesIndex >= chart.Series.Count) return;
        _captured = chart.Series[_seriesIndex];
        chart.Series.RemoveAt(_seriesIndex);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null || _captured is null) return;
        var idx = Math.Clamp(_seriesIndex, 0, chart.Series.Count);
        chart.Series.Insert(idx, _captured);
    }
}

// ── Add / remove category ─────────────────────────────────────────────────────

/// <summary>
/// Appends a new category to the chart.  Each existing series gets one new <c>0.0</c> value
/// so the value matrix stays rectangular.
/// Revert removes the category (and the corresponding tail value from every series).
/// </summary>
public sealed class AddChartCategoryCommand : IPresentationCommand
{
    private readonly int    _slideIndex;
    private readonly uint   _shapeId;
    private readonly string _label;

    public AddChartCategoryCommand(int slideIndex, uint shapeId, string label = "New Category")
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _label      = label;
    }

    public string Label => "Add Category";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;
        chart.Categories.Add(_label);
        foreach (var series in chart.Series)
            series.Values.Add(0.0);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (chart.Categories.Count == 0) return;
        chart.Categories.RemoveAt(chart.Categories.Count - 1);
        foreach (var series in chart.Series)
            if (series.Values.Count > 0)
                series.Values.RemoveAt(series.Values.Count - 1);
    }
}

/// <summary>
/// Removes the category at <paramref name="categoryIndex"/> together with the corresponding
/// value slot from every series.  Captures removed data for undo.
/// </summary>
public sealed class RemoveChartCategoryCommand : IPresentationCommand
{
    private readonly int             _slideIndex;
    private readonly uint            _shapeId;
    private readonly int             _categoryIndex;
    private string                   _capturedLabel   = string.Empty;
    private List<double?>            _capturedValues  = new();

    public RemoveChartCategoryCommand(int slideIndex, uint shapeId, int categoryIndex)
    {
        _slideIndex    = slideIndex;
        _shapeId       = shapeId;
        _categoryIndex = categoryIndex;
    }

    public string Label => "Remove Category";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_categoryIndex < 0 || _categoryIndex >= chart.Categories.Count) return;

        _capturedLabel  = chart.Categories[_categoryIndex];
        _capturedValues = chart.Series
            .Select(s => _categoryIndex < s.Values.Count ? s.Values[_categoryIndex] : (double?)null)
            .ToList();

        chart.Categories.RemoveAt(_categoryIndex);
        foreach (var series in chart.Series)
            if (_categoryIndex < series.Values.Count)
                series.Values.RemoveAt(_categoryIndex);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;

        var idx = Math.Clamp(_categoryIndex, 0, chart.Categories.Count);
        chart.Categories.Insert(idx, _capturedLabel);

        for (int si = 0; si < chart.Series.Count; si++)
        {
            var v = si < _capturedValues.Count ? _capturedValues[si] : 0.0;
            var vi = Math.Clamp(idx, 0, chart.Series[si].Values.Count);
            chart.Series[si].Values.Insert(vi, v);
        }
    }
}

// ── Batch replace (used by the dialog OK button for one coherent undo entry) ──

/// <summary>
/// Atomically replaces the entire data payload of a chart — categories, series names,
/// and the full values matrix — in one undoable command.  Used by <c>ChartDataDialog</c>
/// so that all edits made in the dialog become a single undo step.
/// </summary>
public sealed class ReplaceChartDataCommand : IPresentationCommand
{
    private readonly int              _slideIndex;
    private readonly uint             _shapeId;
    private readonly List<string>     _newCategories;
    private readonly List<string>     _newSeriesNames;
    private readonly List<List<double>> _newValues;   // [seriesIndex][categoryIndex]

    // Captured prior state:
    private List<string>       _oldCategories   = new();
    private List<string>       _oldSeriesNames  = new();
    private List<List<double>> _oldValues       = new();

    public ReplaceChartDataCommand(
        int slideIndex,
        uint shapeId,
        IEnumerable<string>           categories,
        IEnumerable<string>           seriesNames,
        IEnumerable<IEnumerable<double>> values)
    {
        _slideIndex     = slideIndex;
        _shapeId        = shapeId;
        _newCategories  = categories.ToList();
        _newSeriesNames = seriesNames.ToList();
        _newValues      = values.Select(row => row.ToList()).ToList();
    }

    public string Label => "Edit Chart Data";
    public int EstimatedBytes => 1024;

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;

        // Capture old state.
        _oldCategories  = chart.Categories.ToList();
        _oldSeriesNames = chart.Series.Select(s => s.Name).ToList();
        _oldValues      = chart.Series
            .Select(s => s.Values.Select(v => v ?? 0.0).ToList())
            .ToList();

        // Apply new state.
        ApplyData(chart, _newCategories, _newSeriesNames, _newValues);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null) return;
        ApplyData(chart, _oldCategories, _oldSeriesNames, _oldValues);
    }

    private static void ApplyData(
        ChartShape          chart,
        List<string>        categories,
        List<string>        seriesNames,
        List<List<double>>  values)
    {
        // Replace categories.
        chart.Categories.Clear();
        foreach (var c in categories) chart.Categories.Add(c);

        int catCount = categories.Count;

        // Add/remove series to match desired count.
        while (chart.Series.Count > seriesNames.Count)
            chart.Series.RemoveAt(chart.Series.Count - 1);
        while (chart.Series.Count < seriesNames.Count)
            chart.Series.Add(new ChartSeries());

        // Set names and values.
        for (int si = 0; si < chart.Series.Count; si++)
        {
            chart.Series[si].Name = seriesNames[si];
            var vs = si < values.Count ? values[si] : new List<double>();
            chart.Series[si].Values.Clear();
            for (int ci = 0; ci < catCount; ci++)
                chart.Series[si].Values.Add(ci < vs.Count ? vs[ci] : 0.0);
        }
    }
}
