using System.Globalization;
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
///   │  [Add Series] [Remove Series]  [Add Cat] [Remove Cat] [Switch Row/Column] │
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
/// The commit marks the chart workbook for regeneration; the package writer emits the
/// updated embedded workbook and cached chart data together on save.
/// </summary>
public sealed class ChartDataDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    // ── State ─────────────────────────────────────────────────────────────────────

    private readonly EditingSession          _editor;
    private readonly ChartDataDialogPlanner _planner;

    // ── Controls ──────────────────────────────────────────────────────────────────

    private readonly DataGrid _grid;
    private readonly Button   _addSeriesBtn;
    private readonly Button   _removeSeriesBtn;
    private readonly Button   _moveSeriesUpBtn;
    private readonly Button   _moveSeriesDownBtn;
    private readonly Button   _addCatBtn;
    private readonly Button   _removeCatBtn;
    private readonly Button   _moveCatLeftBtn;
    private readonly Button   _moveCatRightBtn;
    private readonly Button   _switchRowsAndColumnsBtn;
    private readonly ComboBox _chartTypeCombo;
    private readonly TextBlock _validationText = new();

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
        // W7: preserve gap (null) entries — do NOT coerce to 0.0 here.
        _planner = ChartDataDialogPlanner.FromChart(chart);

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
        _moveSeriesUpBtn = MakeToolbarButton("Move Series Up", OnMoveSeriesUp);
        _moveSeriesDownBtn = MakeToolbarButton("Move Series Down", OnMoveSeriesDown);
        _addCatBtn       = MakeToolbarButton("+ Category",  OnAddCategory);
        _removeCatBtn    = MakeToolbarButton("- Category",  OnRemoveCategory);
        _moveCatLeftBtn  = MakeToolbarButton("Move Category Left", OnMoveCategoryLeft);
        _moveCatRightBtn = MakeToolbarButton("Move Category Right", OnMoveCategoryRight);
        _switchRowsAndColumnsBtn = MakeToolbarButton("Switch Row/Column", OnSwitchRowsAndColumns);
        _chartTypeCombo = new ComboBox
        {
            ItemsSource = ChartDataDialogPlanner.ChartTypeOptions,
            DisplayMemberPath = nameof(ChartDataDialogChartTypeOption.Label),
            SelectedValuePath = nameof(ChartDataDialogChartTypeOption.Value),
            SelectedValue = _planner.SelectedChartType,
            Width = 170,
            Margin = new Thickness(8, 0, 4, 0),
        };
        _chartTypeCombo.SelectionChanged += (_, _) =>
        {
            if (_chartTypeCombo.SelectedValue is ChartType chartType)
                _planner.SetChartType(chartType);
        };

        var toolbar = new WrapPanel { Margin = new Thickness(4, 4, 4, 2) };
        toolbar.Children.Add(_addSeriesBtn);
        toolbar.Children.Add(_removeSeriesBtn);
        toolbar.Children.Add(_moveSeriesUpBtn);
        toolbar.Children.Add(_moveSeriesDownBtn);
        toolbar.Children.Add(new Separator { Width = 12, Visibility = Visibility.Hidden });
        toolbar.Children.Add(_addCatBtn);
        toolbar.Children.Add(_removeCatBtn);
        toolbar.Children.Add(_moveCatLeftBtn);
        toolbar.Children.Add(_moveCatRightBtn);
        toolbar.Children.Add(_switchRowsAndColumnsBtn);
        toolbar.Children.Add(new TextBlock
        {
            Text = ChartDataDialogPlanner.ChartTypeLabel,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 2, 0),
        });
        toolbar.Children.Add(_chartTypeCombo);

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
        _validationText.Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A));
        _validationText.Margin = new Thickness(8, 2, 8, 2);
        _validationText.TextWrapping = TextWrapping.Wrap;

        // ── OK / Cancel ───────────────────────────────────────────────────────────
        var btnRow = DialogButtonRowFactory.Create(
            OnOk,
            buttonWidth: 80,
            rowMargin: new Thickness(4, 4, 8, 8));

        // ── Layout ────────────────────────────────────────────────────────────────
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // toolbar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // grid
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // validation
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // buttons
        Grid.SetRow(toolbar,  0);
        Grid.SetRow(_grid,    1);
        Grid.SetRow(_validationText, 2);
        Grid.SetRow(btnRow,   3);
        root.Children.Add(toolbar);
        root.Children.Add(_grid);
        root.Children.Add(_validationText);
        root.Children.Add(btnRow);

        Content = root;

        RebuildGrid();
    }

    internal string ValidationText => _validationText.Text;

    internal ChartDataDialogCommitPlan BuildCommitPlanForTests()
    {
        if (!TryCommitPendingEdit())
            throw new InvalidOperationException("The chart data grid contains an invalid value.");
        return _planner.BuildCommitPlan(ReadCategoryEditsFromGrid());
    }

    internal bool PrepareValidationForVisualEvidence()
    {
        if (_grid.Items.Count == 0 || _grid.Columns.Count < 2)
            return false;
        _grid.CurrentCell = new DataGridCellInfo(_grid.Items[0], _grid.Columns[1]);
        _grid.ScrollIntoView(_grid.Items[0], _grid.Columns[1]);
        _grid.Focus();
        if (!_grid.BeginEdit())
            return false;
        UpdateLayout();
        var editor = FindVisualDescendants<TextBox>(_grid).FirstOrDefault(box => box.IsKeyboardFocusWithin)
            ?? FindVisualDescendants<TextBox>(_grid).FirstOrDefault();
        if (editor is null)
            return false;
        editor.Text = "not-a-number";
        editor.Focus();
        var committed = TryCommitPendingEdit();
        return !committed && !string.IsNullOrWhiteSpace(_validationText.Text);
    }

    // ── Grid rebuild ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reconstructs the DataGrid columns and rows from the presentation planner.
    /// Called after any structural change.
    /// </summary>
    private void RebuildGrid()
    {
        // Flush any pending edits before rebuilding.
        _grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var table = _planner.BuildTableProjection();

        _grid.Columns.Clear();

        // Column 0: Category label (editable text).
        var catCol = new DataGridTextColumn
        {
            Header  = table.CategoryColumnHeader,
            Width   = new DataGridLength(130),
            Binding = new Binding("Category") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }
        };
        _grid.Columns.Add(catCol);

        // One column per series; header = series name (editable via cell template).
        foreach (var seriesColumn in table.SeriesColumns)
        {
            var col = new DataGridTextColumn
            {
                Header  = seriesColumn.IsSeriesNameColumn
                    ? MakeEditableHeader(seriesColumn)
                    : seriesColumn.Header,
                Width   = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding($"Values[{seriesColumn.ValueIndex}]")
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                    Converter = new NullableDoubleConverter()
                }
            };
            _grid.Columns.Add(col);
        }

        // Build row items.
        var rows = table.Rows
            .Select(row => new ChartRowViewModel(row))
            .ToList();
        _grid.ItemsSource = rows;
    }

    /// <summary>Creates a TextBox header element that updates the series name on leave.</summary>
    private FrameworkElement MakeEditableHeader(ChartDataDialogSeriesColumn seriesColumn)
    {
        var tb = new TextBox
        {
            Text        = seriesColumn.Name,
            Background  = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontWeight  = FontWeights.SemiBold,
            MinWidth    = 60,
            Padding     = new Thickness(2),
        };
        tb.LostFocus += (_, _) => seriesColumn.Name = tb.Text;
        return tb;
    }

    // ── Toolbar handlers ──────────────────────────────────────────────────────────

    private void OnAddSeries()
    {
        _planner.AddSeries();
        RebuildGrid();
    }

    private void OnRemoveSeries()
    {
        if (!TryCommitPendingEdit())
            return;

        var table = _planner.BuildTableProjection();
        var displayIndex = _grid.CurrentColumn?.DisplayIndex ?? -1;
        var seriesIndex = displayIndex > 0 && displayIndex <= table.SeriesColumns.Count
            ? table.SeriesColumns[displayIndex - 1].SeriesIndex
            : _planner.SeriesCount - 1;
        if (_planner.RemoveSeriesAt(seriesIndex))
            RebuildGrid();
    }

    private void OnMoveSeriesUp() => MoveActiveSeries(-1);

    private void OnMoveSeriesDown() => MoveActiveSeries(1);

    private void MoveActiveSeries(int delta)
    {
        if (!TryCommitPendingEdit())
            return;

        var table = _planner.BuildTableProjection();
        var displayIndex = _grid.CurrentColumn?.DisplayIndex ?? -1;
        if (displayIndex <= 0 || displayIndex > table.SeriesColumns.Count)
            return;

        var sourceIndex = table.SeriesColumns[displayIndex - 1].SeriesIndex;
        if (_planner.MoveSeries(sourceIndex, sourceIndex + delta))
            RebuildGrid();
    }

    private void OnAddCategory()
    {
        _planner.AddCategory();
        RebuildGrid();
    }

    private void OnRemoveCategory()
    {
        if (!TryCommitPendingEdit())
            return;

        var categoryIndex = _grid.SelectedIndex >= 0
            ? _grid.SelectedIndex
            : _planner.CategoryCount - 1;
        if (_planner.RemoveCategoryAt(categoryIndex))
            RebuildGrid();
    }

    private void OnMoveCategoryLeft() => MoveActiveCategory(-1);

    private void OnMoveCategoryRight() => MoveActiveCategory(1);

    private void MoveActiveCategory(int delta)
    {
        if (!TryCommitPendingEdit())
            return;

        var categoryIndex = _grid.SelectedIndex >= 0
            ? _grid.SelectedIndex
            : _planner.CategoryCount - 1;
        if (_planner.MoveCategory(categoryIndex, categoryIndex + delta))
            RebuildGrid();
    }

    private void OnSwitchRowsAndColumns()
    {
        if (!TryCommitPendingEdit())
            return;

        _planner.SwitchRowsAndColumns();
        RebuildGrid();
    }

    // ── OK ────────────────────────────────────────────────────────────────────────

    private void OnOk()
    {
        // Flush any cell being edited.
        if (!TryCommitPendingEdit())
            return;
        _validationText.Text = string.Empty;

        // Flush row-bound category labels that have not yet lost focus.
        var commit = _planner.BuildCommitPlan(ReadCategoryEditsFromGrid());

        // W7: pass nullable values so gaps stay null in the committed model.
        _editor.ReplaceChartData(
            commit.Categories,
            commit.SeriesNames,
            commit.ValuesForCommand(),
            commit.ChartType,
            commit.XValuesForCommand(),
            commit.BubbleSizesForCommand());

        DialogResult = true;
        Close();
    }

    private void ShowValidation() => _validationText.Text = ChartDataDialogPlanner.InvalidNumericValueMessage;

    private bool TryCommitPendingEdit()
    {
        var editor = FindVisualDescendants<TextBox>(_grid).FirstOrDefault(box => box.IsKeyboardFocusWithin);
        var isValueColumn = _grid.CurrentColumn?.DisplayIndex > 0;
        if (isValueColumn && editor is not null &&
            !string.IsNullOrWhiteSpace(editor.Text) &&
            ChartDataDialogPlanner.ParseCellValue(editor.Text, CultureInfo.CurrentCulture) is null)
        {
            ShowValidation();
            editor.Focus();
            return false;
        }

        if (!_grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true))
        {
            ShowValidation();
            return false;
        }
        return true;
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindVisualDescendants<T>(child))
                yield return descendant;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private IEnumerable<ChartDataDialogCategoryEdit> ReadCategoryEditsFromGrid()
    {
        if (_grid.ItemsSource is IEnumerable<ChartRowViewModel> rows)
        {
            return rows.Select(row => row.ToCategoryEdit()).ToList();
        }

        return Array.Empty<ChartDataDialogCategoryEdit>();
    }

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

    // ── Inner view-model types ────────────────────────────────────────────────────

    /// <summary>One row in the DataGrid: a category label + one value per series.</summary>
    internal sealed class ChartRowViewModel
    {
        private readonly ChartDataDialogTableRow _row;

        public ChartRowViewModel(ChartDataDialogTableRow row)
        {
            _row = row;
            Values = new ObservableDoubleNullableArray(row.Values);
        }

        public int CategoryIndex => _row.CategoryIndex;

        public string Category
        {
            get => _row.Category;
            set => _row.Category = value;
        }

        /// <summary>Indexed nullable-value array — bound by DataGridTextColumn Binding("Values[si]").</summary>
        public ObservableDoubleNullableArray Values { get; }

        public ChartDataDialogCategoryEdit ToCategoryEdit()
        {
            return new ChartDataDialogCategoryEdit(CategoryIndex, Category);
        }
    }

    /// <summary>
    /// A simple indexable adapter that lets DataGrid bindings mutate planner values.
    ///
    /// W7: Stores <see cref="double?"/> so that gap (null) values survive the dialog
    /// round-trip.  The WPF DataGrid binding pairs this with <see cref="NullableDoubleConverter"/>
    /// which renders null as an empty cell and parses an empty/blank cell back to null.
    /// </summary>
    internal sealed class ObservableDoubleNullableArray
    {
        private readonly IReadOnlyList<ChartDataDialogValueCell> _values;

        public ObservableDoubleNullableArray(IReadOnlyList<ChartDataDialogValueCell> values)
        {
            _values = values;
        }

        public double? this[int index]
        {
            get => index >= 0 && index < _values.Count ? _values[index].Value : null;
            set
            {
                if (index >= 0 && index < _values.Count)
                {
                    _values[index].Value = value;
                }
            }
        }
    }

    /// <summary>
    /// Converts between <see cref="double?"/> and string for the DataGrid binding.
    ///
    /// W7 display boundary:
    ///  - null  → empty string  (gap cell appears blank)
    ///  - value → G6 string
    ///  - empty/whitespace ← null  (user clearing a cell creates a gap)
    ///  - numeric string  ← parsed double
    /// </summary>
    private sealed class NullableDoubleConverter : System.Windows.Data.IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return ChartDataDialogPlanner.FormatCellValue(value as double?, culture);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return ChartDataDialogPlanner.ParseCellValue(value, culture);
        }
    }
}
