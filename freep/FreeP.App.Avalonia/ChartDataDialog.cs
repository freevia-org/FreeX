using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class ChartDataDialog : Window
{
    private readonly EditingSession _editor;
    private readonly ChartDataDialogPlanner _planner;
    private readonly ChartDataDialogSurfacePlan _surface;
    private readonly CultureInfo _culture;
    private readonly Grid _tableGrid = new();
    private readonly List<IndexedTextBox> _seriesNameBoxes = new();
    private readonly List<IndexedTextBox> _categoryBoxes = new();
    private readonly List<ValueTextBox> _valueBoxes = new();
    private readonly TextBlock _validationText = new();

    public ChartDataDialog(EditingSession editor)
        : this(editor, CultureInfo.CurrentCulture)
    {
    }

    internal ChartDataDialog(EditingSession editor, CultureInfo culture)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _culture = culture ?? throw new ArgumentNullException(nameof(culture));

        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");

        _planner = ChartDataDialogPlanner.FromChart(chart);
        _surface = ChartDataDialogPlanner.BuildSurfacePlan();

        Title = _surface.Title;
        Width = 625.3333333333334;
        Height = 402.6666666666667;
        MinWidth = 520;
        MinHeight = 320;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        Content = BuildContent();
        RebuildTable();
    }

    internal int RenderedSeriesColumnCount => _seriesNameBoxes.Count;

    internal int RenderedCategoryRowCount => _categoryBoxes.Count;

    internal int RenderedValueCellCount => _valueBoxes.Count;

    internal ChartDataDialogCommitPlan BuildCommitPlanForTests()
    {
        FlushTextBoxEdits();
        return _planner.BuildCommitPlan();
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
            Margin = new Thickness(8, 8, 8, 4),
            Spacing = 6,
            Children =
            {
                MakeToolbarButton(_surface.AddSeriesLabel, OnAddSeries),
                MakeToolbarButton(_surface.RemoveSeriesLabel, OnRemoveSeries),
                MakeToolbarButton(_surface.AddCategoryLabel, OnAddCategory),
                MakeToolbarButton(_surface.RemoveCategoryLabel, OnRemoveCategory),
            },
        };

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(8, 4),
            Content = _tableGrid,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 4, 12, 12),
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
        Grid.SetRow(scroller, 1);
        Grid.SetRow(_validationText, 2);
        Grid.SetRow(buttons, 3);
        root.Children.Add(toolbar);
        root.Children.Add(scroller);
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

        var table = _planner.BuildTableProjection();
        _tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddCell(MakeHeader(table.CategoryColumnHeader), row: 0, column: 0);

        for (var seriesColumnIndex = 0; seriesColumnIndex < table.SeriesColumns.Count; seriesColumnIndex++)
        {
            var seriesColumn = table.SeriesColumns[seriesColumnIndex];
            _tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var headerBox = MakeTextBox(seriesColumn.Name, minWidth: 100);
            _seriesNameBoxes.Add(new IndexedTextBox(seriesColumn.SeriesIndex, headerBox));
            AddCell(headerBox, row: 0, column: seriesColumnIndex + 1);
        }

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var gridRow = rowIndex + 1;
            _tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var categoryBox = MakeTextBox(row.Category, minWidth: 130);
            _categoryBoxes.Add(new IndexedTextBox(row.CategoryIndex, categoryBox));
            AddCell(categoryBox, gridRow, column: 0);

            for (var valueIndex = 0; valueIndex < row.Values.Count; valueIndex++)
            {
                var cell = row.Values[valueIndex];
                var valueBox = MakeTextBox(
                    ChartDataDialogPlanner.FormatCellValue(cell.Value, _culture),
                    minWidth: 90);
                _valueBoxes.Add(new ValueTextBox(cell.SeriesIndex, cell.CategoryIndex, valueBox));
                AddCell(valueBox, gridRow, valueIndex + 1);
            }
        }
    }

    private void AddCell(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        _tableGrid.Children.Add(control);
    }

    private void OnAddSeries()
    {
        FlushTextBoxEdits();
        _planner.AddSeries();
        RebuildTable();
    }

    private void OnRemoveSeries()
    {
        FlushTextBoxEdits();
        _planner.RemoveLastSeries();
        RebuildTable();
    }

    private void OnAddCategory()
    {
        FlushTextBoxEdits();
        _planner.AddCategory();
        RebuildTable();
    }

    private void OnRemoveCategory()
    {
        FlushTextBoxEdits();
        _planner.RemoveLastCategory();
        RebuildTable();
    }

    private void OnOk()
    {
        if (!TryFlushTextBoxEdits())
            return;
        var commit = _planner.BuildCommitPlan();
        _editor.ReplaceChartData(
            commit.Categories,
            commit.SeriesNames,
            commit.ValuesForCommand());
        Close(true);
    }

    private void FlushTextBoxEdits()
    {
        TryFlushTextBoxEdits();
    }

    private bool TryFlushTextBoxEdits()
    {
        var invalid = _valueBoxes.FirstOrDefault(box =>
            !string.IsNullOrWhiteSpace(box.TextBox.Text) &&
            !double.TryParse(box.TextBox.Text, NumberStyles.Float | NumberStyles.AllowThousands, _culture, out _));
        if (invalid is not null)
        {
            _validationText.Text = ChartDataDialogPlanner.InvalidNumericValueMessage;
            invalid.TextBox.Focus();
            return false;
        }
        _validationText.Text = string.Empty;
        _planner.ApplySeriesNameEdits(_seriesNameBoxes.Select(box =>
            new ChartDataDialogSeriesNameEdit(box.Index, box.TextBox.Text)));
        _planner.ApplyCategoryEdits(_categoryBoxes.Select(box =>
            new ChartDataDialogCategoryEdit(box.Index, box.TextBox.Text)));
        _planner.ApplyValueEdits(_valueBoxes.Select(box =>
            new ChartDataDialogValueEdit(box.SeriesIndex, box.CategoryIndex, box.TextBox.Text)), _culture);
        return true;
    }

    private static TextBlock MakeHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(3),
            Padding = new Thickness(6, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static TextBox MakeTextBox(string text, double minWidth)
    {
        return new TextBox
        {
            Text = text,
            MinWidth = minWidth,
            Margin = new Thickness(3),
            Padding = new Thickness(6, 3),
        };
    }

    private static Button MakeToolbarButton(string label, Action onClick)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(9, 4),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Button MakeDialogButton(string label, bool isDefault, Action onClick)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 84,
            Padding = new Thickness(10, 4),
            IsDefault = isDefault,
            IsCancel = !isDefault,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private sealed record IndexedTextBox(int Index, TextBox TextBox);

    private sealed record ValueTextBox(int SeriesIndex, int CategoryIndex, TextBox TextBox);
}
