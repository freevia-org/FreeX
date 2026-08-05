using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartDataDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly ChartDataDialogSession _session;
    private readonly ChartDataDialogSurfacePlan _surface;
    private readonly CultureInfo _culture;
    private readonly Grid _tableGrid = new();
    private readonly List<IndexedTextBox> _seriesNameBoxes = new();
    private readonly List<IndexedTextBox> _categoryBoxes = new();
    private readonly List<ValueTextBox> _valueBoxes = new();
    private readonly ComboBox _chartTypeCombo;
    private readonly TextBlock _validationText = new();

    public ChartDataDialog(EditingSession editor)
        : this(editor, CultureInfo.CurrentCulture)
    {
    }

    internal ChartDataDialog(EditingSession editor, CultureInfo culture)
    {
        _culture = culture ?? throw new ArgumentNullException(nameof(culture));
        _session = new ChartDataDialogSession(editor);
        _surface = ChartDataDialogPlanner.BuildSurfacePlan();
        var chartTypeOptions = ChartDataDialogPlanner.ChartTypeOptions;
        var selectedChartTypeIndex = chartTypeOptions
            .ToList()
            .FindIndex(option => option.Value == _session.SelectedChartType);
        _chartTypeCombo = new ComboBox
        {
            ItemsSource = chartTypeOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = selectedChartTypeIndex >= 0 ? selectedChartTypeIndex : 0,
            MinWidth = 170,
        };
        _chartTypeCombo.SelectionChanged += (_, _) =>
        {
            if (_chartTypeCombo.SelectedIndex >= 0 &&
                _chartTypeCombo.SelectedIndex < chartTypeOptions.Count)
            {
                _session.SetChartType(chartTypeOptions[_chartTypeCombo.SelectedIndex].Value);
            }
        };

        Title = _surface.Title;
        Width = 625.3333333333334;
        Height = 402.6666666666667;
        MinWidth = 520;
        MinHeight = 320;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        _tableGrid.MinWidth = 616;
        _tableGrid.HorizontalAlignment = HorizontalAlignment.Stretch;

        Content = BuildContent();
        RebuildTable();
    }

    internal int RenderedSeriesColumnCount => _seriesNameBoxes.Count;

    internal int RenderedCategoryRowCount => _categoryBoxes.Count;

    internal int RenderedValueCellCount => _valueBoxes.Count;

    internal ChartDataDialogCommitPlan BuildCommitPlanForTests()
    {
        FlushTextBoxEdits();
        return _session.BuildCommitPlan();
    }

    internal void SwitchRowsAndColumnsForTests()
    {
        FlushTextBoxEdits();
        _session.SwitchRowsAndColumns();
        RebuildTable();
    }

    internal void SetChartTypeForTests(ChartType chartType)
    {
        _session.SetChartType(chartType);
        var options = ChartDataDialogPlanner.ChartTypeOptions;
        var index = options.ToList().FindIndex(option => option.Value == chartType);
        if (index >= 0)
            _chartTypeCombo.SelectedIndex = index;
    }

    internal void MoveSeriesForTests(int seriesIndex, bool down)
    {
        FlushTextBoxEdits();
        _session.SelectSeries(seriesIndex);
        MoveActiveSeries(down ? 1 : -1);
    }

    internal void RemoveSeriesForTests(int seriesIndex)
    {
        FlushTextBoxEdits();
        _session.SelectSeries(seriesIndex);
        OnRemoveSeries();
    }

    internal void RemoveCategoryForTests(int categoryIndex)
    {
        FlushTextBoxEdits();
        _session.SelectCategory(categoryIndex);
        OnRemoveCategory();
    }

    internal void MoveCategoryForTests(int categoryIndex, bool right)
    {
        FlushTextBoxEdits();
        _session.SelectCategory(categoryIndex);
        MoveActiveCategory(right ? 1 : -1);
    }

    private Control BuildContent()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 4, 4, 2),
            Spacing = 4,
            Children =
            {
                MakeToolbarButton(_surface.AddSeriesLabel, OnAddSeries),
                MakeToolbarButton(_surface.RemoveSeriesLabel, OnRemoveSeries),
                MakeToolbarButton(_surface.MoveSeriesUpLabel, OnMoveSeriesUp),
                MakeToolbarButton(_surface.MoveSeriesDownLabel, OnMoveSeriesDown),
                new Border { Width = 12 },
                MakeToolbarButton(_surface.AddCategoryLabel, OnAddCategory),
                MakeToolbarButton(_surface.RemoveCategoryLabel, OnRemoveCategory),
                MakeToolbarButton(_surface.MoveCategoryLeftLabel, OnMoveCategoryLeft),
                MakeToolbarButton(_surface.MoveCategoryRightLabel, OnMoveCategoryRight),
                MakeToolbarButton(_surface.SwitchRowsAndColumnsLabel, OnSwitchRowsAndColumns),
                new TextBlock
                {
                    Text = _surface.ChartTypeLabel,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                },
                _chartTypeCombo,
            },
        };

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _tableGrid,
        };
        var tableBorder = new Border
        {
            Margin = new Thickness(4, 2, 4, 4),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
            Child = scroller,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(4, 4, 8, 8),
            Spacing = 8,
            Children =
            {
                MakeDialogButton(_surface.OkLabel, isDefault: true, OnOk),
                MakeDialogButton(_surface.CancelLabel, isDefault: false, () => Close(false)),
            },
        };
        _validationText.Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A));
        _validationText.Margin = new Thickness(12, 2, 12, 2);
        _validationText.TextWrapping = TextWrapping.Wrap;

        Grid.SetRow(toolbar, 0);
        Grid.SetRow(tableBorder, 1);
        Grid.SetRow(_validationText, 2);
        Grid.SetRow(buttons, 3);
        root.Children.Add(toolbar);
        root.Children.Add(tableBorder);
        root.Children.Add(_validationText);
        root.Children.Add(buttons);
        return root;
    }

    internal string ValidationText => _validationText.Text ?? string.Empty;

    internal bool PrepareValidationForVisualEvidence()
    {
        var first = _valueBoxes.FirstOrDefault();
        if (first is null)
            return false;
        first.TextBox.Text = "not-a-number";
        first.TextBox.Focus();
        return !TryFlushTextBoxEdits();
    }

    private void RebuildTable()
    {
        _tableGrid.Children.Clear();
        _tableGrid.RowDefinitions.Clear();
        _tableGrid.ColumnDefinitions.Clear();
        _seriesNameBoxes.Clear();
        _categoryBoxes.Clear();
        _valueBoxes.Clear();

        var table = _session.BuildTableProjection();
        _tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

        AddCell(MakeHeader(table.CategoryColumnHeader), row: 0, column: 0);

        for (var seriesColumnIndex = 0; seriesColumnIndex < table.SeriesColumns.Count; seriesColumnIndex++)
        {
            var seriesColumn = table.SeriesColumns[seriesColumnIndex];
            _tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (seriesColumn.IsSeriesNameColumn)
            {
                var headerBox = MakeTextBox(seriesColumn.Name, minWidth: 100);
                TrackSeriesFocus(headerBox, seriesColumn.SeriesIndex);
                _seriesNameBoxes.Add(new IndexedTextBox(seriesColumn.SeriesIndex, headerBox));
                AddCell(headerBox, row: 0, column: seriesColumnIndex + 1);
            }
            else
            {
                AddCell(MakeHeader(seriesColumn.Header), row: 0, column: seriesColumnIndex + 1);
            }
        }

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var gridRow = rowIndex + 1;
            _tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var categoryBox = MakeTextBox(row.Category, minWidth: 130);
            _categoryBoxes.Add(new IndexedTextBox(row.CategoryIndex, categoryBox));
            TrackCategoryFocus(categoryBox, row.CategoryIndex);
            AddCell(categoryBox, gridRow, column: 0);

            for (var valueIndex = 0; valueIndex < row.Values.Count; valueIndex++)
            {
                var cell = row.Values[valueIndex];
                var valueBox = MakeTextBox(
                    ChartDataDialogPlanner.FormatCellValue(cell.Value, _culture),
                    minWidth: 90);
                TrackSeriesFocus(valueBox, cell.SeriesIndex);
                TrackCategoryFocus(valueBox, cell.CategoryIndex);
                _valueBoxes.Add(new ValueTextBox(
                    cell.SeriesIndex,
                    cell.CategoryIndex,
                    cell.Kind,
                    valueBox));
                AddCell(valueBox, gridRow, valueIndex + 1);
            }
        }
    }

    private void AddCell(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        var cell = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = control,
        };
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        _tableGrid.Children.Add(cell);
    }

    private void OnAddSeries()
    {
        FlushTextBoxEdits();
        _session.AddSeries();
        RebuildTable();
    }

    private void OnRemoveSeries()
    {
        FlushTextBoxEdits();
        if (_session.RemoveActiveSeries())
            RebuildTable();
    }

    private void OnMoveSeriesUp() => MoveActiveSeries(-1);

    private void OnMoveSeriesDown() => MoveActiveSeries(1);

    private void MoveActiveSeries(int delta)
    {
        if (!TryFlushTextBoxEdits())
            return;

        if (_session.MoveActiveSeries(delta))
            RebuildTable();
    }

    private void OnAddCategory()
    {
        FlushTextBoxEdits();
        _session.AddCategory();
        RebuildTable();
    }

    private void OnRemoveCategory()
    {
        FlushTextBoxEdits();
        if (_session.RemoveActiveCategory())
            RebuildTable();
    }

    private void OnMoveCategoryLeft() => MoveActiveCategory(-1);

    private void OnMoveCategoryRight() => MoveActiveCategory(1);

    private void MoveActiveCategory(int delta)
    {
        if (!TryFlushTextBoxEdits())
            return;

        if (_session.MoveActiveCategory(delta))
            RebuildTable();
    }

    private void OnSwitchRowsAndColumns()
    {
        if (!TryFlushTextBoxEdits())
            return;

        _session.SwitchRowsAndColumns();
        RebuildTable();
    }

    private void OnOk()
    {
        if (!_session.TryCommit(ReadEdits(), _culture, out var validation))
        {
            ShowValidation(validation);
            return;
        }
        Close(true);
    }

    private void FlushTextBoxEdits()
    {
        TryFlushTextBoxEdits();
    }

    private bool TryFlushTextBoxEdits()
    {
        if (!_session.TryApplyEdits(ReadEdits(), _culture, out var validation))
        {
            ShowValidation(validation);
            return false;
        }
        _validationText.Text = string.Empty;
        return true;
    }

    private ChartDataDialogEdits ReadEdits() =>
        new(
            _seriesNameBoxes.Select(box =>
                new ChartDataDialogSeriesNameEdit(box.Index, box.TextBox.Text)).ToArray(),
            _categoryBoxes.Select(box =>
                new ChartDataDialogCategoryEdit(box.Index, box.TextBox.Text)).ToArray(),
            _valueBoxes.Select(box =>
                new ChartDataDialogValueEdit(
                    box.SeriesIndex,
                    box.CategoryIndex,
                    box.TextBox.Text,
                    box.Kind)).ToArray());

    private void ShowValidation(ChartDataDialogValidationDecision validation)
    {
        _validationText.Text = validation.Message;
        if (validation.InvalidValueEditIndex >= 0 &&
            validation.InvalidValueEditIndex < _valueBoxes.Count)
        {
            _valueBoxes[validation.InvalidValueEditIndex].TextBox.Focus();
        }
    }

    private static TextBlock MakeHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Height = 22,
            Padding = new Thickness(3, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static TextBox MakeTextBox(string text, double minWidth)
    {
        return new TextBox
        {
            Text = text,
            MinWidth = minWidth,
            Height = 20,
            MinHeight = 20,
            MaxHeight = 20,
            Padding = new Thickness(3, 0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
        };
    }

    private void TrackSeriesFocus(TextBox textBox, int seriesIndex) =>
        textBox.GotFocus += (_, _) => _session.SelectSeries(seriesIndex);

    private void TrackCategoryFocus(TextBox textBox, int categoryIndex) =>
        textBox.GotFocus += (_, _) => _session.SelectCategory(categoryIndex);

    private static Button MakeToolbarButton(string label, Action onClick)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(8, 3),
        };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 0);
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Button MakeDialogButton(string label, bool isDefault, Action onClick)
    {
        var button = new Button
        {
            Content = label,
            IsDefault = isDefault,
            IsCancel = !isDefault,
        };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => onClick();
        return button;
    }

    private sealed record IndexedTextBox(int Index, TextBox TextBox);

    private sealed record ValueTextBox(
        int SeriesIndex,
        int CategoryIndex,
        ChartDataDialogValueKind Kind,
        TextBox TextBox);
}
