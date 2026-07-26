using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartPointOptionsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly EditingSession _editor;
    private readonly ChartPointOptionsPlanner _planner;
    private readonly ComboBox _seriesCombo;
    private readonly ComboBox _pointCombo;
    private readonly TextBox _fillColorBox;
    private readonly TextBox _strokeColorBox;
    private readonly TextBox _strokeWidthBox;
    private readonly CheckBox _usePointDataLabelsCheck;
    private readonly CheckBox _showValueLabelsCheck;
    private readonly CheckBox _showPercentLabelsCheck;
    private readonly CheckBox _showCategoryLabelsCheck;
    private readonly CheckBox _showSeriesLabelsCheck;
    private readonly CheckBox _showLegendKeysCheck;
    private readonly ComboBox _labelPositionCombo;
    private readonly TextBox _labelNumberFormatBox;
    private readonly TextBox _labelSeparatorBox;
    private readonly ComboBox _markerCombo;
    private readonly TextBox _markerSizeBox;

    internal ChartPointOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartPointOptionsPlanner.FromChart(chart);
        var surface = ChartPointOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartPointOptionsPlanner.DefaultDialogWidth;
        Height = ChartPointOptionsPlanner.DefaultDialogHeight;
        MinWidth = 400;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _seriesCombo = new ComboBox
        {
            ItemsSource = _planner.SeriesOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = _planner.SeriesIndex,
            MinWidth = 220,
        };
        _seriesCombo.SelectionChanged += (_, _) =>
        {
            _planner.SetSeriesIndex(_seriesCombo.SelectedIndex);
            RefreshPoints();
            LoadControls();
        };
        _pointCombo = new ComboBox { MinWidth = 220 };
        _pointCombo.SelectionChanged += (_, _) =>
        {
            _planner.SetPointIndex(_pointCombo.SelectedIndex);
            LoadControls();
        };
        _fillColorBox = new TextBox { MinWidth = 150 };
        _strokeColorBox = new TextBox { MinWidth = 150 };
        _strokeWidthBox = new TextBox { MinWidth = 120 };
        _usePointDataLabelsCheck = new CheckBox { Content = surface.PointDataLabelsLabel };
        _showValueLabelsCheck = new CheckBox { Content = surface.ValueLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showPercentLabelsCheck = new CheckBox { Content = surface.PercentLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showCategoryLabelsCheck = new CheckBox { Content = surface.CategoryLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showSeriesLabelsCheck = new CheckBox { Content = surface.SeriesLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showLegendKeysCheck = new CheckBox { Content = surface.LegendKeysLabel, Margin = new Thickness(20, 0, 0, 0) };
        _labelPositionCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.LabelPositionOptions.Select(option => option.Label).ToArray(),
            MinWidth = 160,
        };
        _labelNumberFormatBox = new TextBox { MinWidth = 150 };
        _labelSeparatorBox = new TextBox { MinWidth = 150 };
        _markerCombo = new ComboBox
        {
            ItemsSource = ChartPointOptionsPlanner.MarkerOptions.Select(option => option.Label).ToArray(),
            MinWidth = 160,
        };
        _markerSizeBox = new TextBox { MinWidth = 120 };
        RefreshPoints();
        LoadControls();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                MakeButton(surface.OkLabel, true, OnOk),
                MakeButton(surface.CancelLabel, false, () => Close(false)),
            },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                MakeRow(surface.SeriesLabel, _seriesCombo),
                MakeRow(surface.PointLabel, _pointCombo),
                MakeRow(surface.FillColorLabel, _fillColorBox),
                MakeRow(surface.StrokeColorLabel, _strokeColorBox),
                MakeRow(surface.StrokeWidthLabel, _strokeWidthBox),
                _usePointDataLabelsCheck,
                _showValueLabelsCheck,
                _showPercentLabelsCheck,
                _showCategoryLabelsCheck,
                _showSeriesLabelsCheck,
                _showLegendKeysCheck,
                MakeRow(surface.LabelPositionLabel, _labelPositionCombo),
                MakeRow(surface.NumberFormatLabel, _labelNumberFormatBox),
                MakeRow(surface.SeparatorLabel, _labelSeparatorBox),
                MakeRow(surface.MarkerLabel, _markerCombo),
                MakeRow(surface.MarkerSizeLabel, _markerSizeBox),
                new TextBlock { Text = surface.AutoHint, Opacity = 0.7 },
                buttons,
            },
        };
    }

    internal ChartPointOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(
        int seriesIndex,
        int pointIndex,
        string? fillColor,
        string? strokeColor,
        double? strokeWidthPt,
        ChartMarkerSymbol? markerSymbol,
        double? markerSizePt,
        bool usePointDataLabels = false,
        bool showValueLabels = false,
        bool showPercentLabels = false,
        bool showCategoryLabels = false,
        bool showSeriesLabels = false,
        bool showLegendKeys = false,
        DataLabelPosition labelPosition = DataLabelPosition.OutsideEnd,
        string? labelNumberFormat = null,
        string? labelSeparator = null)
    {
        _seriesCombo.SelectedIndex = seriesIndex;
        RefreshPoints();
        _pointCombo.SelectedIndex = pointIndex;
        _fillColorBox.Text = fillColor ?? string.Empty;
        _strokeColorBox.Text = strokeColor ?? string.Empty;
        _strokeWidthBox.Text = Format(strokeWidthPt);
        _usePointDataLabelsCheck.IsChecked = usePointDataLabels;
        _showValueLabelsCheck.IsChecked = showValueLabels;
        _showPercentLabelsCheck.IsChecked = showPercentLabels;
        _showCategoryLabelsCheck.IsChecked = showCategoryLabels;
        _showSeriesLabelsCheck.IsChecked = showSeriesLabels;
        _showLegendKeysCheck.IsChecked = showLegendKeys;
        _labelPositionCombo.SelectedIndex = FindLabelPositionIndex(labelPosition);
        _labelNumberFormatBox.Text = labelNumberFormat ?? string.Empty;
        _labelSeparatorBox.Text = labelSeparator ?? string.Empty;
        _markerCombo.SelectedIndex = FindMarkerIndex(markerSymbol);
        _markerSizeBox.Text = Format(markerSizePt);
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartPointOptions(BuildCommitPlanForTests());
            Close(true);
        }
        catch (FormatException)
        {
            Close(false);
        }
    }

    private void RefreshPoints()
    {
        _pointCombo.ItemsSource = _planner.PointOptions.Select(option => option.Label).ToArray();
        _pointCombo.SelectedIndex = Math.Min(_planner.PointIndex, Math.Max(0, _planner.PointOptions.Count - 1));
    }

    private void LoadControls()
    {
        _fillColorBox.Text = _planner.FillColorText;
        _strokeColorBox.Text = _planner.StrokeColorText;
        _strokeWidthBox.Text = Format(_planner.StrokeWidthPt);
        _usePointDataLabelsCheck.IsChecked = _planner.UsePointDataLabels;
        _showValueLabelsCheck.IsChecked = _planner.ShowValueLabels;
        _showPercentLabelsCheck.IsChecked = _planner.ShowPercentLabels;
        _showCategoryLabelsCheck.IsChecked = _planner.ShowCategoryLabels;
        _showSeriesLabelsCheck.IsChecked = _planner.ShowSeriesLabels;
        _showLegendKeysCheck.IsChecked = _planner.ShowLegendKeys;
        _labelPositionCombo.SelectedIndex = FindLabelPositionIndex(_planner.LabelPosition);
        _labelNumberFormatBox.Text = _planner.LabelNumberFormat;
        _labelSeparatorBox.Text = _planner.LabelSeparator;
        _markerCombo.SelectedIndex = FindMarkerIndex(_planner.MarkerSymbol);
        _markerSizeBox.Text = Format(_planner.MarkerSizePt);
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetFillColor(_fillColorBox.Text);
        _planner.SetStrokeColor(_strokeColorBox.Text);
        _planner.SetStrokeWidth(ParseOptional(_strokeWidthBox.Text, "Outline width"));
        _planner.SetUsePointDataLabels(_usePointDataLabelsCheck.IsChecked == true);
        _planner.SetShowValueLabels(_showValueLabelsCheck.IsChecked == true);
        _planner.SetShowPercentLabels(_showPercentLabelsCheck.IsChecked == true);
        _planner.SetShowCategoryLabels(_showCategoryLabelsCheck.IsChecked == true);
        _planner.SetShowSeriesLabels(_showSeriesLabelsCheck.IsChecked == true);
        _planner.SetShowLegendKeys(_showLegendKeysCheck.IsChecked == true);
        if (_labelPositionCombo.SelectedIndex >= 0 &&
            _labelPositionCombo.SelectedIndex < ChartDisplayOptionsPlanner.LabelPositionOptions.Count)
            _planner.SetLabelPosition(ChartDisplayOptionsPlanner.LabelPositionOptions[_labelPositionCombo.SelectedIndex].Value);
        _planner.SetLabelNumberFormat(_labelNumberFormatBox.Text);
        _planner.SetLabelSeparator(_labelSeparatorBox.Text);
        var index = _markerCombo.SelectedIndex;
        var marker = index >= 0 && index < ChartPointOptionsPlanner.MarkerOptions.Count
            ? ChartPointOptionsPlanner.MarkerOptions[index].Value
            : ChartMarkerSymbol.Auto;
        _planner.SetMarkerSymbol(marker == ChartMarkerSymbol.Auto ? null : marker);
        _planner.SetMarkerSize(ParseOptional(_markerSizeBox.Text, "Marker size"));
    }

    private static double? ParseOptional(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            double.IsFinite(value) && value >= 0)
            return value;
        throw new FormatException($"{label} must be a non-negative finite number or blank.");
    }

    private static string Format(double? value) => value?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;

    private static int FindMarkerIndex(ChartMarkerSymbol? symbol)
    {
        var value = symbol ?? ChartMarkerSymbol.Auto;
        return Math.Max(0, ChartPointOptionsPlanner.MarkerOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);
    }

    private static int FindLabelPositionIndex(DataLabelPosition position) =>
        Math.Max(0, ChartDisplayOptionsPlanner.LabelPositionOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == position).index);

    private static Control MakeRow(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("180, *") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
