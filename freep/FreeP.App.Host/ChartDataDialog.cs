using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// Modal data-grid dialog for editing a chart's categories, series names, and numeric values.
///
/// Layout:
///   ┌────────────────────────────────────────────────────┐
///   │  [Add Series] [Remove Series]  [Add Cat] [Remove Cat] │
///   ├──────────┬─────────────┬─────────────────────────────┤
///   │ Category │  Series 1   │  Series 2  │  …             │
///   ├──────────┼─────────────┼────────────┤                │
///   │  Q1      │   4.3       │  2.4       │                │
///   │  Q2      │   2.5       │  4.4       │                │
///   ├──────────┴─────────────┴────────────┴────────────────┤
///   │                          [OK]  [Cancel]               │
///   └────────────────────────────────────────────────────────┘
///
/// The dialog edits a private in-memory copy of the chart data.  On OK it issues a single
/// <see cref="ReplaceChartDataCommand"/> through the <see cref="EditingSession"/> so all
/// changes become one undoable batch.
///
/// NOTE: The embedded workbook inside the .pptx is NOT updated by this dialog —
/// only the cached data model (which the PptxChartWriter emits on save) is written.
/// PowerPoint will read the embedded workbook and may warn about a mismatch; this is
/// acceptable for the current wave and is documented here.
/// </summary>
public sealed class ChartDataDialog : Window
{
    // ── State ─────────────────────────────────────────────────────────────────────

    private readonly EditingSession _editor;
    private readonly List<string>   _categories;  // mutable working copy
    private readonly List<string>   _seriesNames; // mutable working copy
    private readonly List<List<double>> _values;  // [seriesIndex][categoryIndex]

    // ── Controls ──────────────────────────────────────────────────────────────────

    private readonly DataGrid _grid;
    private readonly Button   _addSeriesBtn;
    private readonly Button   _removeSeriesBtn;
    private readonly Button   _addCatBtn;
    private readonly Button   _removeCatBtn;

    // ── Construction ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the dialog for the chart currently selected in <paramref name="editor"/>.
    /// Throws <see cref="InvalidOperationException"/> if no chart is selected.
    /// </summary>
    public ChartDataDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");

        // Deep-copy the data so we don't mutate the live model until OK is pressed.
        _categories  = chart.Categories.ToList();
        _seriesNames = chart.Series.Select(s => s.Name).ToList();
        _values      = chart.Series
            .Select(s => s.Values.Select(v => v ?? 0.0).ToList())
            .ToList();

        // Ensure matrix is rectangular.
        EnsureRectangular();

        // ── Window chrome ─────────────────────────────────────────────────────────
        Title          = "Edit Chart Data";
        Width          = 640;
        Height         = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode     = ResizeMode.CanResize;
        Background     = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        // ── Toolbar ───────────────────────────────────────────────────────────────
        _addSeriesBtn    = MakeToolbarButton("+ Series",    OnAddSeries);
        _removeSeriesBtn = MakeToolbarButton("- Series",    OnRemoveSeries);
        _addCatBtn       = MakeToolbarButton("+ Category",  OnAddCategory);
        _removeCatBtn    = MakeToolbarButton("- Category",  OnRemoveCategory);

        var toolbar = new WrapPanel { Margin = new Thickness(4, 4, 4, 2) };
        toolbar.Children.Add(_addSeriesBtn);
        toolbar.Children.Add(_removeSeriesBtn);
        toolbar.Children.Add(new Separator { Width = 12, Visibility = Visibility.Hidden });
        toolbar.Children.Add(_addCatBtn);
        toolbar.Children.Add(_removeCatBtn);

        // ── DataGrid ──────────────────────────────────────────────────────────────
        _grid = new DataGrid
        {
            AutoGenerateColumns       = false,
            CanUserAddRows            = false,
            CanUserDeleteRows         = false,
            CanUserReorderColumns     = false,
            SelectionMode             = DataGridSelectionMode.Single,
            SelectionUnit             = DataGridSelectionUnit.Cell,
            HeadersVisibility         = DataGridHeadersVisibility.Column,
            GridLinesVisibility       = DataGridGridLinesVisibility.All,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            Margin                    = new Thickness(4, 2, 4, 4),
        };

        // ── OK / Cancel ───────────────────────────────────────────────────────────
        var okBtn = new Button
        {
            Content = "OK",
            Width   = 80,
            Margin  = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        okBtn.Click += (_, _) => OnOk();

        var cancelBtn = new Button
        {
            Content   = "Cancel",
            Width     = 80,
            IsCancel  = true,
        };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(4, 4, 8, 8),
        };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);

        // ── Layout ────────────────────────────────────────────────────────────────
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // toolbar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // grid
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // buttons
        Grid.SetRow(toolbar,  0);
        Grid.SetRow(_grid,    1);
        Grid.SetRow(btnRow,   2);
        root.Children.Add(toolbar);
        root.Children.Add(_grid);
        root.Children.Add(btnRow);

        Content = root;

        RebuildGrid();
    }

    // ── Grid rebuild ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reconstructs the DataGrid columns from current _seriesNames and repopulates
    /// rows from _categories / _values.  Called after any structural change.
    /// </summary>
    private void RebuildGrid()
    {
        // Flush any pending edits before rebuilding.
        _grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        _grid.Columns.Clear();

        // Column 0: Category label (editable text).
        var catCol = new DataGridTextColumn
        {
            Header  = "Category",
            Width   = new DataGridLength(130),
            Binding = new Binding("Category") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }
        };
        _grid.Columns.Add(catCol);

        // One column per series; header = series name (editable via cell template).
        for (int si = 0; si < _seriesNames.Count; si++)
        {
            int capturedSi = si; // closure capture

            var col = new DataGridTextColumn
            {
                Header  = MakeEditableHeader(si),
                Width   = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding($"Values[{capturedSi}]")
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                    Converter = new DoubleConverter()
                }
            };
            _grid.Columns.Add(col);
        }

        // Build row items.
        var rows = new List<ChartRowViewModel>();
        for (int ci = 0; ci < _categories.Count; ci++)
        {
            int capturedCi = ci;
            var rowValues = new ObservableDoubleArray(
                _values.Select(sv => capturedCi < sv.Count ? sv[capturedCi] : 0.0).ToArray(),
                (si, v) =>
                {
                    while (_values.Count <= si) _values.Add(new List<double>());
                    while (_values[si].Count <= capturedCi) _values[si].Add(0.0);
                    _values[si][capturedCi] = v;
                });

            rows.Add(new ChartRowViewModel(
                category: _categories[capturedCi],
                values: rowValues,
                onCategoryChanged: label => _categories[capturedCi] = label));
        }

        _grid.ItemsSource = rows;
    }

    /// <summary>Creates a TextBox header element that updates the series name on leave.</summary>
    private FrameworkElement MakeEditableHeader(int seriesIndex)
    {
        var tb = new TextBox
        {
            Text        = _seriesNames[seriesIndex],
            Background  = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontWeight  = FontWeights.SemiBold,
            MinWidth    = 60,
            Padding     = new Thickness(2),
        };
        tb.LostFocus += (_, _) => _seriesNames[seriesIndex] = tb.Text;
        return tb;
    }

    // ── Toolbar handlers ──────────────────────────────────────────────────────────

    private void OnAddSeries()
    {
        _seriesNames.Add($"Series {_seriesNames.Count + 1}");
        _values.Add(Enumerable.Repeat(0.0, _categories.Count).ToList());
        EnsureRectangular();
        RebuildGrid();
    }

    private void OnRemoveSeries()
    {
        if (_seriesNames.Count == 0) return;
        _seriesNames.RemoveAt(_seriesNames.Count - 1);
        _values.RemoveAt(_values.Count - 1);
        RebuildGrid();
    }

    private void OnAddCategory()
    {
        _categories.Add($"Cat {_categories.Count + 1}");
        foreach (var sv in _values) sv.Add(0.0);
        RebuildGrid();
    }

    private void OnRemoveCategory()
    {
        if (_categories.Count == 0) return;
        _categories.RemoveAt(_categories.Count - 1);
        foreach (var sv in _values)
            if (sv.Count > 0) sv.RemoveAt(sv.Count - 1);
        RebuildGrid();
    }

    // ── OK ────────────────────────────────────────────────────────────────────────

    private void OnOk()
    {
        // Flush any cell being edited.
        _grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        // Collect category labels from the row view-models (they are updated via the callback
        // in ChartRowViewModel, but the TextBox binding updates on LostFocus — flush here).
        if (_grid.ItemsSource is List<ChartRowViewModel> rows)
        {
            for (int ci = 0; ci < rows.Count; ci++)
                _categories[ci] = rows[ci].Category;
        }

        // Issue the batch replace command through the session → one undo entry.
        _editor.ReplaceChartData(
            _categories,
            _seriesNames,
            _values.Select(sv => (IEnumerable<double>)sv));

        DialogResult = true;
        Close();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static Button MakeToolbarButton(string label, Action onClick)
    {
        var btn = new Button
        {
            Content = label,
            Padding = new Thickness(8, 3, 8, 3),
            Margin  = new Thickness(0, 0, 4, 0),
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    /// <summary>Pads every series' Values list to have exactly Categories.Count entries.</summary>
    private void EnsureRectangular()
    {
        int catCount = _categories.Count;
        foreach (var sv in _values)
        {
            while (sv.Count < catCount) sv.Add(0.0);
            while (sv.Count > catCount) sv.RemoveAt(sv.Count - 1);
        }
    }

    // ── Inner view-model types ────────────────────────────────────────────────────

    /// <summary>One row in the DataGrid: a category label + one value per series.</summary>
    internal sealed class ChartRowViewModel
    {
        private string _category;
        private readonly Action<string> _onCategoryChanged;

        public ChartRowViewModel(
            string               category,
            ObservableDoubleArray values,
            Action<string>       onCategoryChanged)
        {
            _category          = category;
            Values             = values;
            _onCategoryChanged = onCategoryChanged;
        }

        public string Category
        {
            get => _category;
            set { _category = value; _onCategoryChanged(value); }
        }

        /// <summary>Indexed value array — bound by DataGridTextColumn Binding("Values[si]").</summary>
        public ObservableDoubleArray Values { get; }
    }

    /// <summary>
    /// A simple indexable array that calls back into the _values matrix on set,
    /// allowing DataGrid bindings to mutate the working copy directly.
    /// </summary>
    internal sealed class ObservableDoubleArray
    {
        private readonly double[]              _data;
        private readonly Action<int, double>   _onSet;

        public ObservableDoubleArray(double[] data, Action<int, double> onSet)
        {
            _data  = data;
            _onSet = onSet;
        }

        public double this[int index]
        {
            get => index >= 0 && index < _data.Length ? _data[index] : 0.0;
            set
            {
                if (index >= 0 && index < _data.Length)
                {
                    _data[index] = value;
                    _onSet(index, value);
                }
            }
        }
    }

    /// <summary>Converts between <see cref="double"/> and string for the DataGrid binding.</summary>
    private sealed class DoubleConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is double d ? d.ToString("G6", culture) : "0";

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string s && double.TryParse(s, System.Globalization.NumberStyles.Any, culture, out double d))
                return d;
            return 0.0;
        }
    }
}
