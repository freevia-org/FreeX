using System.Globalization;
using System.Windows;
using System.Windows.Automation;
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

    private readonly ChartDataDialogSession _session;
    private readonly List<IndexedTextBox> _seriesNameBoxes = [];

    // ── Controls ──────────────────────────────────────────────────────────────────

    private readonly DataGrid _grid;
    private readonly ComboBox _chartTypeCombo;
    private readonly TextBlock _validationText = new();

    // ── Construction ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the dialog for the chart currently selected in <paramref name="editor"/>.
    /// Throws <see cref="InvalidOperationException"/> if no chart is selected.
    /// </summary>
    public ChartDataDialog(EditingSession editor)
    {
        _session = new ChartDataDialogSession(editor);
        var plan = _session.BuildDialogPlan();

        // ── Window chrome ─────────────────────────────────────────────────────────
        Title          = plan.Title;
        Width          = plan.Width;
        Height         = plan.Height;
        MinWidth       = plan.MinimumWidth;
        MinHeight      = plan.MinimumHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode     = plan.IsResizable ? ResizeMode.CanResize : ResizeMode.NoResize;
        Background     = FreePBrushes.SheetSurface;

        // ── Toolbar ───────────────────────────────────────────────────────────────
        _chartTypeCombo = new ComboBox
        {
            ItemsSource = plan.ChartType.Choices,
            SelectedIndex = plan.ChartType.SelectedIndex,
            Width = 170,
            Margin = new Thickness(8, 0, 4, 0),
        };
        SetAutomation(_chartTypeCombo, plan.ChartType.AccessibleName, plan.ChartType.AutomationId);
        _chartTypeCombo.SelectionChanged += (_, _) =>
        {
            _session.SelectChartType(_chartTypeCombo.SelectedIndex);
        };

        var toolbar = new WrapPanel { Margin = new Thickness(4, 4, 4, 2) };
        var actionHandlers = BuildActionHandlers();
        for (var groupIndex = 0; groupIndex < plan.ToolbarGroups.Count; groupIndex++)
        {
            if (groupIndex > 0)
                toolbar.Children.Add(new Separator { Width = 12, Visibility = Visibility.Hidden });
            foreach (var action in plan.ToolbarGroups[groupIndex].Actions)
                toolbar.Children.Add(MakeToolbarButton(action, actionHandlers[action.Id]));
        }
        toolbar.Children.Add(new TextBlock
        {
            Text = plan.ChartType.Label,
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
        SetAutomation(_grid, plan.Table.AccessibleName, plan.Table.AutomationId);
        _validationText.Foreground = FreePBrushes.Accent;
        _validationText.Margin = new Thickness(8, 2, 8, 2);
        _validationText.TextWrapping = TextWrapping.Wrap;
        SetAutomation(_validationText, plan.Table.ValidationAccessibleName, plan.Table.ValidationAutomationId);

        // ── OK / Cancel ───────────────────────────────────────────────────────────
        var btnRow = DialogButtonRowFactory.Create(
            OnOk,
            buttonWidth: 80,
            rowMargin: new Thickness(4, 4, 8, 8),
            acceptContent: plan.AcceptAction.Label,
            cancelContent: plan.CancelAction.Label);
        SetAutomation((Button)btnRow.Children[0], plan.AcceptAction.AccessibleName, plan.AcceptAction.AutomationId);
        SetAutomation((Button)btnRow.Children[1], plan.CancelAction.AccessibleName, plan.CancelAction.AutomationId);

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
        if (!TryFlushPendingEdits())
            throw new InvalidOperationException("The chart data grid contains an invalid value.");
        return _session.BuildCommitPlan();
    }

    internal bool PrepareInvalidValueForTests()
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
        var committed = TryFlushPendingEdits();
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

        var table = _session.BuildTableProjection();

        _grid.Columns.Clear();
        _seriesNameBoxes.Clear();

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
        _seriesNameBoxes.Add(new IndexedTextBox(seriesColumn.SeriesIndex, tb));
        tb.LostFocus += (_, _) => _session.TryApplyEdits(
            new ChartDataDialogEdits(
                [new ChartDataDialogSeriesNameEdit(seriesColumn.SeriesIndex, tb.Text)],
                [],
                []),
            CultureInfo.CurrentCulture,
            out _);
        return tb;
    }

    // ── Toolbar handlers ──────────────────────────────────────────────────────────

    private void OnAddSeries()
    {
        _session.AddSeries();
        RebuildGrid();
    }

    private void OnRemoveSeries()
    {
        if (!TryFlushPendingEdits())
            return;

        var table = _session.BuildTableProjection();
        var displayIndex = _grid.CurrentColumn?.DisplayIndex ?? -1;
        var seriesIndex = displayIndex > 0 && displayIndex <= table.SeriesColumns.Count
            ? table.SeriesColumns[displayIndex - 1].SeriesIndex
            : -1;
        _session.SelectSeries(seriesIndex);
        if (_session.RemoveActiveSeries())
            RebuildGrid();
    }

    private void OnMoveSeriesUp() => MoveActiveSeries(-1);

    private void OnMoveSeriesDown() => MoveActiveSeries(1);

    private void MoveActiveSeries(int delta)
    {
        if (!TryFlushPendingEdits())
            return;

        var table = _session.BuildTableProjection();
        var displayIndex = _grid.CurrentColumn?.DisplayIndex ?? -1;
        var seriesIndex = displayIndex > 0 && displayIndex <= table.SeriesColumns.Count
            ? table.SeriesColumns[displayIndex - 1].SeriesIndex
            : -1;
        _session.SelectSeries(seriesIndex);
        if (_session.MoveActiveSeries(delta))
            RebuildGrid();
    }

    private void OnAddCategory()
    {
        _session.AddCategory();
        RebuildGrid();
    }

    private void OnRemoveCategory()
    {
        if (!TryFlushPendingEdits())
            return;

        var categoryIndex = _grid.SelectedIndex >= 0
            ? _grid.SelectedIndex
            : -1;
        _session.SelectCategory(categoryIndex);
        if (_session.RemoveActiveCategory())
            RebuildGrid();
    }

    private void OnMoveCategoryLeft() => MoveActiveCategory(-1);

    private void OnMoveCategoryRight() => MoveActiveCategory(1);

    private void MoveActiveCategory(int delta)
    {
        if (!TryFlushPendingEdits())
            return;

        var categoryIndex = _grid.SelectedIndex >= 0
            ? _grid.SelectedIndex
            : -1;
        _session.SelectCategory(categoryIndex);
        if (_session.MoveActiveCategory(delta))
            RebuildGrid();
    }

    private void OnSwitchRowsAndColumns()
    {
        if (!TryFlushPendingEdits())
            return;

        _session.SwitchRowsAndColumns();
        RebuildGrid();
    }

    // ── OK ────────────────────────────────────────────────────────────────────────

    private void OnOk()
    {
        if (!TryCommitNativeEdit())
            return;
        if (!_session.TryCommit(
                ReadEditsFromGrid(),
                CultureInfo.CurrentCulture,
                out var validation))
        {
            ShowValidation(validation);
            return;
        }
        _validationText.Text = string.Empty;

        DialogResult = true;
        Close();
    }

    private void ShowValidation(ChartDataDialogValidationDecision? validation = null) =>
        _validationText.Text = validation?.Message ?? ChartDataDialogPlanner.InvalidNumericValueMessage;

    private bool TryFlushPendingEdits()
    {
        if (!TryCommitNativeEdit())
            return false;
        if (_session.TryApplyEdits(
                ReadEditsFromGrid(),
                CultureInfo.CurrentCulture,
                out var validation))
        {
            return true;
        }

        ShowValidation(validation);
        return false;
    }

    private bool TryCommitNativeEdit()
    {
        var editor = FindVisualDescendants<TextBox>(_grid).FirstOrDefault(box => box.IsKeyboardFocusWithin);
        var isValueColumn = _grid.CurrentColumn?.DisplayIndex > 0;
        if (isValueColumn && editor is not null)
        {
            var validation = _session.ValidateValueEdit(editor.Text, CultureInfo.CurrentCulture);
            if (!validation.IsValid)
            {
                ShowValidation(validation);
                editor.Focus();
                return false;
            }
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

    private ChartDataDialogEdits ReadEditsFromGrid()
    {
        if (_grid.ItemsSource is IEnumerable<ChartRowViewModel> rows)
        {
            var rowList = rows.ToList();
            return new ChartDataDialogEdits(
                _seriesNameBoxes
                    .Select(box => new ChartDataDialogSeriesNameEdit(box.Index, box.TextBox.Text))
                    .ToArray(),
                rowList.Select(row => row.ToCategoryEdit()).ToArray(),
                rowList.SelectMany(row => row.ToValueEdits()).ToArray());
        }

        return ChartDataDialogEdits.Empty;
    }

    private IReadOnlyDictionary<ChartDataDialogActionId, Action> BuildActionHandlers() =>
        new Dictionary<ChartDataDialogActionId, Action>
        {
            [ChartDataDialogActionId.AddSeries] = OnAddSeries,
            [ChartDataDialogActionId.RemoveSeries] = OnRemoveSeries,
            [ChartDataDialogActionId.MoveSeriesUp] = OnMoveSeriesUp,
            [ChartDataDialogActionId.MoveSeriesDown] = OnMoveSeriesDown,
            [ChartDataDialogActionId.AddCategory] = OnAddCategory,
            [ChartDataDialogActionId.RemoveCategory] = OnRemoveCategory,
            [ChartDataDialogActionId.MoveCategoryLeft] = OnMoveCategoryLeft,
            [ChartDataDialogActionId.MoveCategoryRight] = OnMoveCategoryRight,
            [ChartDataDialogActionId.SwitchRowsAndColumns] = OnSwitchRowsAndColumns,
        };

    private static Button MakeToolbarButton(ChartDataDialogActionPlan action, Action onClick)
    {
        var btn = new Button
        {
            Content = action.Label,
            Padding = new Thickness(8, 3, 8, 3),
            Margin  = new Thickness(0, 0, 4, 0),
        };
        SetAutomation(btn, action.AccessibleName, action.AutomationId);
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static void SetAutomation(DependencyObject control, string name, string automationId)
    {
        AutomationProperties.SetName(control, name);
        AutomationProperties.SetAutomationId(control, automationId);
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

        public IEnumerable<ChartDataDialogValueEdit> ToValueEdits()
        {
            return _row.Values.Select(cell => new ChartDataDialogValueEdit(
                cell.SeriesIndex,
                cell.CategoryIndex,
                cell.Value,
                cell.Kind));
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

    private sealed record IndexedTextBox(int Index, TextBox TextBox);
}
