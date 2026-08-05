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

    internal ChartPointOptionsDialog(
        EditingSession editor,
        int? initialSeriesIndex = null,
        int? initialPointIndex = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartPointOptionsPlanner.FromChart(chart);
        if (initialSeriesIndex is { } seriesIndex)
            _planner.SetSeriesIndex(seriesIndex);
        if (initialPointIndex is { } pointIndex)
            _planner.SetPointIndex(pointIndex);
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
        _showBubbleSizeCheck = new CheckBox { Content = surface.BubbleSizeLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showLeaderLinesCheck = new CheckBox
        {
            Content = surface.LeaderLinesLabel,
            Margin = new Thickness(20, 0, 0, 0),
            IsThreeState = true,
        };
        _labelPositionCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.LabelPositionOptions.Select(option => option.Label).ToArray(),
            MinWidth = 160,
        };
        _labelNumberFormatBox = new TextBox { MinWidth = 150 };
        _labelSeparatorBox = new TextBox { MinWidth = 150 };
        _labelFontFamilyBox = new TextBox { MinWidth = 150 };
        _labelFontSizeBox = new TextBox { MinWidth = 120 };
        _labelBoldCheck = new CheckBox { Content = surface.BoldLabel, IsThreeState = true };
        _labelItalicCheck = new CheckBox { Content = surface.ItalicLabel, IsThreeState = true };
        _labelColorBox = new TextBox { MinWidth = 150 };
        _markerCombo = new ComboBox
        {
            ItemsSource = ChartPointOptionsPlanner.MarkerOptions.Select(option => option.Label).ToArray(),
            MinWidth = 160,
        };
        _markerSizeBox = new TextBox { MinWidth = 120 };
        _explosionBox = new TextBox { MinWidth = 120 };
        RefreshPoints();
        LoadControls();

        var buttons = ChartOptionsDialogChrome.CreateActionRow(surface.OkLabel, OnOk, surface.CancelLabel, () => Close(false));

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                ChartOptionsDialogChrome.CreateRow(surface.SeriesLabel, _seriesCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.PointLabel, _pointCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.FillColorLabel, _fillColorBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.StrokeColorLabel, _strokeColorBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.StrokeWidthLabel, _strokeWidthBox, 180),
                _usePointDataLabelsCheck,
                _showValueLabelsCheck,
                _showPercentLabelsCheck,
                _showCategoryLabelsCheck,
                _showSeriesLabelsCheck,
                _showLegendKeysCheck,
                _showBubbleSizeCheck,
                _showLeaderLinesCheck,
                ChartOptionsDialogChrome.CreateRow(surface.LabelPositionLabel, _labelPositionCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.NumberFormatLabel, _labelNumberFormatBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.SeparatorLabel, _labelSeparatorBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.FontFamilyLabel, _labelFontFamilyBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.FontSizeLabel, _labelFontSizeBox, 180),
                _labelBoldCheck,
                _labelItalicCheck,
                ChartOptionsDialogChrome.CreateRow(surface.LabelColorLabel, _labelColorBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.MarkerLabel, _markerCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.MarkerSizeLabel, _markerSizeBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.ExplosionLabel, _explosionBox, 180),
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
        _planner.SetLabelPosition(ChartDialogOptionProjection.ValueAtOrDefault(ChartDisplayOptionsPlanner.LabelPositionOptions, _labelPositionCombo.SelectedIndex, option => option.Value));
        _planner.SetLabelNumberFormat(_labelNumberFormatBox.Text);
        _planner.SetLabelSeparator(_labelSeparatorBox.Text);
        _planner.SetLabelFontFamily(_labelFontFamilyBox.Text);
        _planner.SetLabelFontSize(ParseOptional(_labelFontSizeBox.Text, "Label font size"));
        _planner.SetLabelBold(_labelBoldCheck.IsChecked);
        _planner.SetLabelItalic(_labelItalicCheck.IsChecked);
        _planner.SetLabelColor(_labelColorBox.Text);
        var marker = ChartDialogOptionProjection.ValueAtOrDefault(ChartPointOptionsPlanner.MarkerOptions, _markerCombo.SelectedIndex, option => option.Value, ChartMarkerSymbol.Auto);
        _planner.SetMarkerSymbol(marker == ChartMarkerSymbol.Auto ? null : marker);
        _planner.SetMarkerSize(ParseOptional(_markerSizeBox.Text, "Marker size"));
        _planner.SetExplosionPercent(ParseOptionalInt(_explosionBox.Text, "Explosion"));
    }

    private static double? ParseOptional(string? text, string label)
    {
        return ChartDialogOptionProjection.ParseOptionalDouble(text, CultureInfo.CurrentCulture, value => double.IsFinite(value) && value >= 0, $"{label} must be a non-negative finite number or blank.");
    }

    private static string Format(double? value) => ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static string Format(int? value) => ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static int? ParseOptionalInt(string? text, string label)
    {
        return ChartDialogOptionProjection.ParseOptionalInt(text, CultureInfo.CurrentCulture, value => value is >= 0 and <= 100, $"{label} must be an integer from 0 to 100 or blank.");
    }

    private static int FindMarkerIndex(ChartMarkerSymbol? symbol)
    {
        var value = symbol ?? ChartMarkerSymbol.Auto;
        return ChartDialogOptionProjection.FindIndex(ChartPointOptionsPlanner.MarkerOptions, value, option => option.Value);
    }

    private static int FindLabelPositionIndex(DataLabelPosition position) =>
        ChartDialogOptionProjection.FindIndex(ChartDisplayOptionsPlanner.LabelPositionOptions, position, option => option.Value);
}
