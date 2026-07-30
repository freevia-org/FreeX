namespace FreeP.Core.Model;

// ════════════════════════════════════════════════════════════════════════════════
// CHART DATA COMMANDS  (Wave 9B)
//
// All commands operate on a ChartShape identified by (slideIndex, shapeId).
// The value matrix is kept rectangular — every series has one value per category.
// Each command captures the prior state in Apply so that Revert is exact.
// ════════════════════════════════════════════════════════════════════════════════

internal static class ChartHelper
{
    internal static ChartShape? Find(Presentation p, int slideIndex, uint shapeId)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return null;
        var shape = ShapeHelper.Find(p, slideIndex, shapeId);
        return shape?.Chart;
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId) return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    internal static ChartShape? FindDataEditable(Presentation p, int slideIndex, uint shapeId)
    {
        var chart = Find(p, slideIndex, shapeId);
        return chart is null || !IsDataEditable(chart)
            ? null
            : chart;
    }

    internal static ChartShape? FindFormattingEditable(Presentation p, int slideIndex, uint shapeId)
    {
        var chart = Find(p, slideIndex, shapeId);
        return chart is null || !IsFormattingEditable(chart)
            ? null
            : chart;
    }

    internal static bool IsDataEditable(ChartShape chart) =>
        chart.ChartObjectProtected != true && chart.ChartDataProtected != true;

    internal static bool IsFormattingEditable(ChartShape chart) =>
        chart.ChartObjectProtected != true && chart.ChartFormattingProtected != true;

    internal static bool IsObjectEditable(SlideShape shape) =>
        shape.Chart is null || shape.Chart.ChartObjectProtected != true;

    internal static ChartSeries? FindFormattingSeries(
        Presentation p,
        int slideIndex,
        uint shapeId,
        int seriesIndex)
    {
        var chart = FindFormattingEditable(p, slideIndex, shapeId);
        return chart is not null && seriesIndex >= 0 && seriesIndex < chart.Series.Count
            ? chart.Series[seriesIndex]
            : null;
    }

    internal static ChartSeries? FindSeries(
        Presentation p,
        int slideIndex,
        uint shapeId,
        int seriesIndex)
    {
        var chart = FindDataEditable(p, slideIndex, shapeId);
        return chart is not null && seriesIndex >= 0 && seriesIndex < chart.Series.Count
            ? chart.Series[seriesIndex]
            : null;
    }

    internal static void MarkWorkbookDirty(ChartShape chart) =>
        chart.RegenerateWorkbookOnSave = true;
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
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_seriesIndex < 0 || _seriesIndex >= chart.Series.Count) return;
        var values = chart.Series[_seriesIndex].Values;
        if (_categoryIndex < 0 || _categoryIndex >= values.Count) return;
        _oldValue = values[_categoryIndex] ?? 0.0;
        values[_categoryIndex] = _newValue;
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_seriesIndex < 0 || _seriesIndex >= chart.Series.Count) return;
        var values = chart.Series[_seriesIndex].Values;
        if (_categoryIndex < 0 || _categoryIndex >= values.Count) return;
        values[_categoryIndex] = _oldValue;
        ChartHelper.MarkWorkbookDirty(chart);
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
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_categoryIndex < 0 || _categoryIndex >= chart.Categories.Count) return;
        _oldLabel                       = chart.Categories[_categoryIndex];
        chart.Categories[_categoryIndex] = _newLabel;
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_categoryIndex < 0 || _categoryIndex >= chart.Categories.Count) return;
        chart.Categories[_categoryIndex] = _oldLabel;
        ChartHelper.MarkWorkbookDirty(chart);
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
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_seriesIndex < 0 || _seriesIndex >= chart.Series.Count) return;
        _oldName                      = chart.Series[_seriesIndex].Name;
        chart.Series[_seriesIndex].Name = _newName;
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_seriesIndex < 0 || _seriesIndex >= chart.Series.Count) return;
        chart.Series[_seriesIndex].Name = _oldName;
        ChartHelper.MarkWorkbookDirty(chart);
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
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;

        _added = new ChartSeries { Name = _name };
        for (int i = 0; i < chart.Categories.Count; i++)
            _added.Values.Add(0.0);

        chart.Series.Add(_added);
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null || _added is null) return;
        chart.Series.Remove(_added);
        ChartHelper.MarkWorkbookDirty(chart);
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
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (_seriesIndex < 0 || _seriesIndex >= chart.Series.Count) return;
        _captured = chart.Series[_seriesIndex];
        chart.Series.RemoveAt(_seriesIndex);
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null || _captured is null) return;
        var idx = Math.Clamp(_seriesIndex, 0, chart.Series.Count);
        chart.Series.Insert(idx, _captured);
        ChartHelper.MarkWorkbookDirty(chart);
    }
}

/// <summary>Moves one chart series while keeping its authored formatting and data together.</summary>
public sealed class MoveChartSeriesCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _sourceIndex;
    private readonly int _targetIndex;
    private bool _applied;

    public MoveChartSeriesCommand(int slideIndex, uint shapeId, int sourceIndex, int targetIndex)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _sourceIndex = sourceIndex;
        _targetIndex = targetIndex;
    }

    public string Label => "Move Series";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null || !IsValid(chart, _sourceIndex) || !IsValid(chart, _targetIndex) ||
            _sourceIndex == _targetIndex)
            return;

        var series = chart.Series[_sourceIndex];
        chart.Series.RemoveAt(_sourceIndex);
        chart.Series.Insert(_targetIndex, series);
        _applied = true;
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        if (!_applied)
            return;

        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null || !IsValid(chart, _targetIndex))
            return;

        var series = chart.Series[_targetIndex];
        chart.Series.RemoveAt(_targetIndex);
        chart.Series.Insert(Math.Clamp(_sourceIndex, 0, chart.Series.Count), series);
        ChartHelper.MarkWorkbookDirty(chart);
    }

    private static bool IsValid(ChartShape chart, int index) =>
        index >= 0 && index < chart.Series.Count;
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
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;
        chart.Categories.Add(_label);
        foreach (var series in chart.Series)
            series.Values.Add(0.0);
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;
        if (chart.Categories.Count == 0) return;
        chart.Categories.RemoveAt(chart.Categories.Count - 1);
        foreach (var series in chart.Series)
            if (series.Values.Count > 0)
                series.Values.RemoveAt(series.Values.Count - 1);
        ChartHelper.MarkWorkbookDirty(chart);
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
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
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
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;

        var idx = Math.Clamp(_categoryIndex, 0, chart.Categories.Count);
        chart.Categories.Insert(idx, _capturedLabel);

        for (int si = 0; si < chart.Series.Count; si++)
        {
            var v = si < _capturedValues.Count ? _capturedValues[si] : 0.0;
            var vi = Math.Clamp(idx, 0, chart.Series[si].Values.Count);
            chart.Series[si].Values.Insert(vi, v);
        }
        ChartHelper.MarkWorkbookDirty(chart);
    }
}

// ── Batch replace (used by the dialog OK button for one coherent undo entry) ──

/// <summary>
/// Atomically replaces the entire data payload of a chart — categories, series names,
/// and the full values matrix — in one undoable command.  Used by <c>ChartDataDialog</c>
/// so that all edits made in the dialog become a single undo step.
///
/// Gap (null) values are preserved throughout: the command accepts nullable values in its
/// new-data payload, captures the existing nullable values for undo, and restores the
/// original <see cref="ChartSeries"/> instances (including <see cref="ChartSeries.FillColor"/>
/// and <see cref="ChartSeries.PointColors"/>) on Revert so that per-series styling survives
/// a remove-then-undo cycle.
/// </summary>
public sealed class ReplaceChartDataCommand : IPresentationCommand
{
    private readonly int               _slideIndex;
    private readonly uint              _shapeId;
    private readonly List<string>      _newCategories;
    private readonly List<string>      _newSeriesNames;
    private readonly List<List<double?>> _newValues;   // [seriesIndex][categoryIndex], nulls = gaps
    private readonly List<List<double?>>? _newXValues;
    private readonly List<List<double?>>? _newBubbleSizes;
    private readonly ChartType?        _newChartType;

    // Captured prior state (W6: nullable to preserve gaps; W8: full series list for styling).
    private List<string>        _oldCategories  = new();
    private List<string>        _oldSeriesNames = new();
    private List<List<double?>> _oldValues      = new();
    private List<List<double?>> _oldXValues     = new();
    private List<List<double?>> _oldBubbleSizes = new();
    private List<ChartSeries>   _oldSeries      = new();  // W8: snapshot for FillColor / PointColors
    private bool                _oldRegenerateWorkbookOnSave;
    private ChartType            _oldChartType;
    private ScatterStyle         _oldScatterStyle;

    /// <summary>
    /// Nullable-aware constructor — gaps (null entries) in <paramref name="values"/> are
    /// preserved as null in the model and round-trip through OOXML as missing &lt;c:pt&gt; nodes.
    /// </summary>
    public ReplaceChartDataCommand(
        int slideIndex,
        uint shapeId,
        IEnumerable<string>             categories,
        IEnumerable<string>             seriesNames,
        IEnumerable<IEnumerable<double?>> values,
        ChartType?                       chartType = null)
    {
        _slideIndex     = slideIndex;
        _shapeId        = shapeId;
        _newCategories  = categories.ToList();
        _newSeriesNames = seriesNames.ToList();
        _newValues      = values.Select(row => row.ToList()).ToList();
        _newChartType   = chartType;
    }

    /// <summary>Scatter/Bubble-aware batch constructor with editable coordinate payloads.</summary>
    public ReplaceChartDataCommand(
        int slideIndex,
        uint shapeId,
        IEnumerable<string> categories,
        IEnumerable<string> seriesNames,
        IEnumerable<IEnumerable<double?>> values,
        ChartType? chartType,
        IEnumerable<IEnumerable<double?>>? xValues,
        IEnumerable<IEnumerable<double?>>? bubbleSizes)
        : this(slideIndex, shapeId, categories, seriesNames, values, chartType)
    {
        _newXValues = xValues?.Select(row => row.ToList()).ToList();
        _newBubbleSizes = bubbleSizes?.Select(row => row.ToList()).ToList();
    }

    /// <summary>
    /// Non-nullable overload for callers that already have <c>double</c> sequences (no gaps).
    /// Delegates to the nullable constructor.
    /// </summary>
    public ReplaceChartDataCommand(
        int slideIndex,
        uint shapeId,
        IEnumerable<string>              categories,
        IEnumerable<string>              seriesNames,
        IEnumerable<IEnumerable<double>> values,
        ChartType?                        chartType = null)
        : this(slideIndex, shapeId, categories, seriesNames,
               values.Select(row => row.Select(v => (double?)v)), chartType)
    {
    }

    public string Label => "Edit Chart Data";
    public int EstimatedBytes => 1024;

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;

        // W6: Capture old values as nullable — preserves gap (null) entries for undo.
        // W8: Capture the actual ChartSeries instances so FillColor/PointColors survive undo.
        _oldCategories  = chart.Categories.ToList();
        _oldSeriesNames = chart.Series.Select(s => s.Name).ToList();
        _oldValues      = chart.Series
            .Select(s => s.Values.ToList())   // ToList() of List<double?> — nulls preserved
            .ToList();
        _oldXValues = chart.Series
            .Select(s => s.XValues.ToList())
            .ToList();
        _oldBubbleSizes = chart.Series
            .Select(s => s.BubbleSizes.ToList())
            .ToList();
        _oldSeries      = chart.Series.ToList();  // snapshot series references
        _oldRegenerateWorkbookOnSave = chart.RegenerateWorkbookOnSave;
        _oldChartType = chart.ChartType;
        _oldScatterStyle = chart.ScatterStyle;

        // Apply new state.
        ApplyForward(chart, _newCategories, _newSeriesNames, _newValues);
        if (_newChartType.HasValue)
        {
            ApplyChartTypeTransition(chart, _oldChartType, _newChartType.Value);
            chart.ChartType = _newChartType.Value;
        }
        ApplyCoordinatePayload(
            chart,
            _newXValues,
            _newBubbleSizes,
            _newChartType ?? _oldChartType);
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindDataEditable(p, _slideIndex, _shapeId);
        if (chart is null) return;

        // W8: Restore original series instances (restores FillColor, PointColors, etc.).
        // W6: Restore values with their original nullable shape (gaps stay null).
        // Also restore original Names — ApplyForward mutates Name in place.
        RestoreOriginal(chart, _oldCategories, _oldSeries, _oldSeriesNames, _oldValues);
        RestoreSeriesCoordinates(chart, _oldXValues, _oldBubbleSizes);
        chart.RegenerateWorkbookOnSave = _oldRegenerateWorkbookOnSave;
        chart.ChartType = _oldChartType;
        chart.ScatterStyle = _oldScatterStyle;
    }

    // ── Forward apply: produce the new data, keeping existing series when possible ─

    private static void ApplyForward(
        ChartShape            chart,
        List<string>          categories,
        List<string>          seriesNames,
        List<List<double?>>   values)
    {
        // Replace categories.
        chart.Categories.Clear();
        foreach (var c in categories) chart.Categories.Add(c);

        int catCount = categories.Count;

        // Shrink: remove surplus series from the tail.
        while (chart.Series.Count > seriesNames.Count)
            chart.Series.RemoveAt(chart.Series.Count - 1);

        // Grow: add new blank series (styling will be default — only needed for net-new series
        // that didn't exist before the edit, so there's nothing to restore).
        while (chart.Series.Count < seriesNames.Count)
            chart.Series.Add(new ChartSeries());

        // Set names and values.
        for (int si = 0; si < chart.Series.Count; si++)
        {
            chart.Series[si].Name = seriesNames[si];
            var vs = si < values.Count ? values[si] : new List<double?>();
            chart.Series[si].Values.Clear();
            for (int ci = 0; ci < catCount; ci++)
                chart.Series[si].Values.Add(ci < vs.Count ? vs[ci] : null);
        }
    }

    private static void ApplyChartTypeTransition(
        ChartShape chart,
        ChartType oldChartType,
        ChartType newChartType)
    {
        bool oldScatterLike = oldChartType is ChartType.Scatter or ChartType.Bubble;
        bool newScatterLike = newChartType is ChartType.Scatter or ChartType.Bubble;

        if (newScatterLike)
        {
            foreach (var series in chart.Series)
            {
                int pointCount = Math.Max(series.Values.Count, chart.Categories.Count);
                if (series.XValues.Count == 0)
                {
                    for (var index = 0; index < pointCount; index++)
                        series.XValues.Add(index + 1.0);
                }

                if (newChartType == ChartType.Bubble && series.BubbleSizes.Count == 0)
                {
                    for (var index = 0; index < pointCount; index++)
                        series.BubbleSizes.Add(1.0);
                }
            }

            if (!oldScatterLike)
                chart.ScatterStyle = ScatterStyle.LineMarker;
            return;
        }

        if (oldScatterLike)
        {
            foreach (var series in chart.Series)
            {
                series.XValues.Clear();
                series.BubbleSizes.Clear();
            }
        }
    }

    private static void ApplyCoordinatePayload(
        ChartShape chart,
        List<List<double?>>? xValues,
        List<List<double?>>? bubbleSizes,
        ChartType targetChartType)
    {
        if (targetChartType is not (ChartType.Scatter or ChartType.Bubble))
            return;

        for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            if (xValues is not null && seriesIndex < xValues.Count)
            {
                series.XValues.Clear();
                series.XValues.AddRange(
                    NormalizeCoordinates(xValues[seriesIndex], chart.Categories.Count));
            }

            if (targetChartType == ChartType.Bubble)
            {
                if (bubbleSizes is not null && seriesIndex < bubbleSizes.Count)
                {
                    series.BubbleSizes.Clear();
                    series.BubbleSizes.AddRange(
                        NormalizeCoordinates(bubbleSizes[seriesIndex], chart.Categories.Count));
                }
                else if (series.BubbleSizes.Count == 0)
                {
                    for (var index = 0; index < chart.Categories.Count; index++)
                        series.BubbleSizes.Add(1.0);
                }
            }
            else
            {
                series.BubbleSizes.Clear();
            }
        }
    }

    private static IEnumerable<double?> NormalizeCoordinates(
        IEnumerable<double?> values,
        int count)
    {
        var normalized = values.Take(count).ToList();
        while (normalized.Count < count)
            normalized.Add(null);
        return normalized;
    }

    private static void RestoreSeriesCoordinates(
        ChartShape chart,
        List<List<double?>> oldXValues,
        List<List<double?>> oldBubbleSizes)
    {
        for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            series.XValues.Clear();
            if (seriesIndex < oldXValues.Count)
                series.XValues.AddRange(oldXValues[seriesIndex]);

            series.BubbleSizes.Clear();
            if (seriesIndex < oldBubbleSizes.Count)
                series.BubbleSizes.AddRange(oldBubbleSizes[seriesIndex]);
        }
    }

    // ── Undo restore: put back original series instances verbatim ────────────────

    private static void RestoreOriginal(
        ChartShape            chart,
        List<string>          categories,
        List<ChartSeries>     originalSeries,
        List<string>          originalSeriesNames,
        List<List<double?>>   originalValues)
    {
        // Restore categories.
        chart.Categories.Clear();
        foreach (var c in categories) chart.Categories.Add(c);

        // Replace the entire series list with the original instances
        // (this restores FillColor, PointColors, etc. exactly).
        chart.Series.Clear();
        foreach (var s in originalSeries)
            chart.Series.Add(s);

        // Restore Name and Values on each series — both may have been mutated by ApplyForward.
        for (int si = 0; si < chart.Series.Count; si++)
        {
            if (si < originalSeriesNames.Count)
                chart.Series[si].Name = originalSeriesNames[si];

            chart.Series[si].Values.Clear();
            var vs = si < originalValues.Count ? originalValues[si] : new List<double?>();
            foreach (var v in vs)
                chart.Series[si].Values.Add(v);
        }
    }
}
