using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style per-point chart formatting dialog.</summary>
public sealed class ChartPointOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
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
    private readonly CheckBox _showBubbleSizeCheck;
    private readonly CheckBox _showLeaderLinesCheck;
    private readonly ComboBox _labelPositionCombo;
    private readonly TextBox _labelNumberFormatBox;
    private readonly TextBox _labelSeparatorBox;
    private readonly TextBox _labelFontFamilyBox;
    private readonly TextBox _labelFontSizeBox;
    private readonly CheckBox _labelBoldCheck;
    private readonly CheckBox _labelItalicCheck;
    private readonly TextBox _labelColorBox;
    private readonly ComboBox _markerCombo;
    private readonly TextBox _markerSizeBox;
    private readonly TextBox _explosionBox;

    public ChartPointOptionsDialog(
        EditingSession editor,
        int? initialSeriesIndex = null,
        int? initialPointIndex = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartPointOptionsPlanner.FromChart(chart);
        if (initialSeriesIndex is { } seriesIndex)
            _planner.SetSeriesIndex(seriesIndex);
        if (initialPointIndex is { } pointIndex)
            _planner.SetPointIndex(pointIndex);
        var surface = ChartPointOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartPointOptionsPlanner.DefaultDialogWidth;
        Height = ChartPointOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _seriesCombo = new ComboBox
        {
            ItemsSource = _planner.SeriesOptions,
            DisplayMemberPath = nameof(ChartSeriesOption.Label),
            SelectedIndex = _planner.SeriesIndex,
            MinWidth = 220,
        };
        _seriesCombo.SelectionChanged += (_, _) =>
        {
            if (_seriesCombo.SelectedItem is ChartSeriesOption option)
            {
                _planner.SetSeriesIndex(option.Index);
                RefreshPoints();
                LoadControls();
            }
        };

        _pointCombo = new ComboBox { DisplayMemberPath = nameof(ChartPointOption.Label), MinWidth = 220 };
        _pointCombo.SelectionChanged += (_, _) =>
        {
            if (_pointCombo.SelectedItem is ChartPointOption option)
            {
                _planner.SetPointIndex(option.Index);
                LoadControls();
            }
        };
        _fillColorBox = new TextBox { MinWidth = 140 };
        _strokeColorBox = new TextBox { MinWidth = 140 };
        _strokeWidthBox = new TextBox { MinWidth = 120 };
        _usePointDataLabelsCheck = new CheckBox { Content = surface.PointDataLabelsLabel };
        _showValueLabelsCheck = new CheckBox { Content = surface.ValueLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showPercentLabelsCheck = new CheckBox { Content = surface.PercentLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showCategoryLabelsCheck = new CheckBox { Content = surface.CategoryLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showSeriesLabelsCheck = new CheckBox { Content = surface.SeriesLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showLegendKeysCheck = new CheckBox { Content = surface.LegendKeysLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showBubbleSizeCheck = new CheckBox { Content = surface.BubbleSizeLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showLeaderLinesCheck = new CheckBox
        {
            Content = surface.LeaderLinesLabel,
            Margin = new Thickness(20, 0, 0, 0),
            IsThreeState = true,
        };
        _labelPositionCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.LabelPositionOptions,
            DisplayMemberPath = nameof(ChartDisplayLabelPositionOption.Label),
            MinWidth = 150,
        };
        _labelNumberFormatBox = new TextBox { MinWidth = 140 };
        _labelSeparatorBox = new TextBox { MinWidth = 140 };
        _labelFontFamilyBox = new TextBox { MinWidth = 140 };
        _labelFontSizeBox = new TextBox { MinWidth = 120 };
        _labelBoldCheck = new CheckBox { Content = surface.BoldLabel, IsThreeState = true };
        _labelItalicCheck = new CheckBox { Content = surface.ItalicLabel, IsThreeState = true };
        _labelColorBox = new TextBox { MinWidth = 140 };
        _markerCombo = new ComboBox
        {
            ItemsSource = ChartPointOptionsPlanner.MarkerOptions,
            DisplayMemberPath = nameof(ChartMarkerSymbolOption.Label),
            MinWidth = 160,
        };
        _markerSizeBox = new TextBox { MinWidth = 120 };
        _explosionBox = new TextBox { MinWidth = 120 };
        RefreshPoints();
        LoadControls();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 14, 8, 8),
        };
        var ok = new Button { Content = surface.OkLabel, IsDefault = true, MinWidth = 80, Margin = new Thickness(4) };
        var cancel = new Button { Content = surface.CancelLabel, IsCancel = true, MinWidth = 80, Margin = new Thickness(4) };
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(MakeRow(surface.SeriesLabel, _seriesCombo));
        content.Children.Add(MakeRow(surface.PointLabel, _pointCombo));
        content.Children.Add(MakeRow(surface.FillColorLabel, _fillColorBox));
        content.Children.Add(MakeRow(surface.StrokeColorLabel, _strokeColorBox));
        content.Children.Add(MakeRow(surface.StrokeWidthLabel, _strokeWidthBox));
        content.Children.Add(_usePointDataLabelsCheck);
        content.Children.Add(_showValueLabelsCheck);
        content.Children.Add(_showPercentLabelsCheck);
        content.Children.Add(_showCategoryLabelsCheck);
        content.Children.Add(_showSeriesLabelsCheck);
        content.Children.Add(_showLegendKeysCheck);
        content.Children.Add(_showBubbleSizeCheck);
        content.Children.Add(_showLeaderLinesCheck);
        content.Children.Add(MakeRow(surface.LabelPositionLabel, _labelPositionCombo));
        content.Children.Add(MakeRow(surface.NumberFormatLabel, _labelNumberFormatBox));
        content.Children.Add(MakeRow(surface.SeparatorLabel, _labelSeparatorBox));
        content.Children.Add(MakeRow(surface.FontFamilyLabel, _labelFontFamilyBox));
        content.Children.Add(MakeRow(surface.FontSizeLabel, _labelFontSizeBox));
        content.Children.Add(_labelBoldCheck);
        content.Children.Add(_labelItalicCheck);
        content.Children.Add(MakeRow(surface.LabelColorLabel, _labelColorBox));
        content.Children.Add(MakeRow(surface.MarkerLabel, _markerCombo));
        content.Children.Add(MakeRow(surface.MarkerSizeLabel, _markerSizeBox));
        content.Children.Add(MakeRow(surface.ExplosionLabel, _explosionBox));
        content.Children.Add(new TextBlock { Text = surface.AutoHint, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
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
        string? labelSeparator = null,
        string? labelFontFamily = null,
        double? labelFontSizePt = null,
        bool? labelBold = null,
        bool? labelItalic = null,
        string? labelColor = null,
        bool showBubbleSize = false,
        int? explosionPercent = null,
        bool? showLeaderLines = null)
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
        _showBubbleSizeCheck.IsChecked = showBubbleSize;
        _showLeaderLinesCheck.IsChecked = showLeaderLines;
        _labelPositionCombo.SelectedIndex = FindLabelPositionIndex(labelPosition);
        _labelNumberFormatBox.Text = labelNumberFormat ?? string.Empty;
        _labelSeparatorBox.Text = labelSeparator ?? string.Empty;
        _labelFontFamilyBox.Text = labelFontFamily ?? string.Empty;
        _labelFontSizeBox.Text = Format(labelFontSizePt);
        _labelBoldCheck.IsChecked = labelBold;
        _labelItalicCheck.IsChecked = labelItalic;
        _labelColorBox.Text = labelColor ?? string.Empty;
        _markerCombo.SelectedIndex = FindMarkerIndex(markerSymbol);
        _markerSizeBox.Text = Format(markerSizePt);
        _explosionBox.Text = Format(explosionPercent);
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartPointOptions(BuildCommitPlanForTests());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshPoints()
    {
        _pointCombo.ItemsSource = _planner.PointOptions;
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
        _showBubbleSizeCheck.IsChecked = _planner.ShowBubbleSize;
        _showLeaderLinesCheck.IsChecked = _planner.ShowLeaderLines;
        _labelPositionCombo.SelectedIndex = FindLabelPositionIndex(_planner.LabelPosition);
        _labelNumberFormatBox.Text = _planner.LabelNumberFormat;
        _labelSeparatorBox.Text = _planner.LabelSeparator;
        _labelFontFamilyBox.Text = _planner.LabelFontFamily;
        _labelFontSizeBox.Text = Format(_planner.LabelFontSizePt);
        _labelBoldCheck.IsChecked = _planner.LabelBold;
        _labelItalicCheck.IsChecked = _planner.LabelItalic;
        _labelColorBox.Text = _planner.LabelColorText;
        _markerCombo.SelectedIndex = FindMarkerIndex(_planner.MarkerSymbol);
        _markerSizeBox.Text = Format(_planner.MarkerSizePt);
        _explosionBox.Text = Format(_planner.ExplosionPercent);
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
        _planner.SetShowBubbleSize(_showBubbleSizeCheck.IsChecked == true);
        _planner.SetShowLeaderLines(_showLeaderLinesCheck.IsChecked);
        if (_labelPositionCombo.SelectedItem is ChartDisplayLabelPositionOption position)
            _planner.SetLabelPosition(position.Value);
        _planner.SetLabelNumberFormat(_labelNumberFormatBox.Text);
        _planner.SetLabelSeparator(_labelSeparatorBox.Text);
        _planner.SetLabelFontFamily(_labelFontFamilyBox.Text);
        _planner.SetLabelFontSize(ParseOptional(_labelFontSizeBox.Text, "Label font size"));
        _planner.SetLabelBold(_labelBoldCheck.IsChecked);
        _planner.SetLabelItalic(_labelItalicCheck.IsChecked);
        _planner.SetLabelColor(_labelColorBox.Text);
        var marker = _markerCombo.SelectedItem as ChartMarkerSymbolOption;
        _planner.SetMarkerSymbol(marker is null || marker.Value == ChartMarkerSymbol.Auto ? null : marker.Value);
        _planner.SetMarkerSize(ParseOptional(_markerSizeBox.Text, "Marker size"));
        _planner.SetExplosionPercent(ParseOptionalInt(_explosionBox.Text, "Explosion"));
    }

    private static double? ParseOptional(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            double.IsFinite(value) && value >= 0)
            return value;
        throw new FormatException($"{label} must be a non-negative finite number or blank.");
    }

    private static string Format(double? value) =>
        value?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;

    private static string Format(int? value) =>
        value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

    private static int? ParseOptionalInt(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) &&
            value is >= 0 and <= 100)
            return value;
        throw new FormatException($"{label} must be an integer from 0 to 100 or blank.");
    }

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

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        row.Children.Add(new Label { Content = label, Width = 180, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }
}
