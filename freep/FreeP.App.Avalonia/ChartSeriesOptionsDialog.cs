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

internal sealed class ChartSeriesOptionsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly EditingSession _editor;
    private readonly ChartSeriesOptionsPlanner _planner;
    private readonly ComboBox _seriesCombo;
    private readonly ComboBox _seriesChartTypeCombo;
    private readonly CheckBox _smoothLineCheck;
    private readonly CheckBox _secondaryAxisCheck;
    private readonly CheckBox _invertIfNegativeCheck;
    private readonly TextBox _lineWidthBox;
    private readonly TextBox _lineColorBox;
    private readonly ComboBox _lineDashCombo;
    private readonly CheckBox _noLineCheck;
    private readonly TextBox _fillColorBox;
    private readonly CheckBox _useSeriesDataLabelsCheck;
    private readonly CheckBox _showValueLabelsCheck;
    private readonly CheckBox _showPercentLabelsCheck;
    private readonly CheckBox _showCategoryLabelsCheck;
    private readonly CheckBox _showSeriesLabelsCheck;
    private readonly CheckBox _showLegendKeysCheck;
    private readonly CheckBox _showBubbleSizeCheck;
    private readonly CheckBox _showLeaderLinesCheck;
    private readonly CheckBox _errorBarsCheck;
    private readonly ComboBox _errorDirectionCombo;
    private readonly ComboBox _errorBarTypeCombo;
    private readonly ComboBox _errorValueTypeCombo;
    private readonly TextBox _errorValueBox;
    private readonly CheckBox _errorNoEndCapCheck;
    private readonly CheckBox _trendlineCheck;
    private readonly ComboBox _trendlineTypeCombo;
    private readonly TextBox _trendlineOrderBox;
    private readonly TextBox _trendlinePeriodBox;
    private readonly TextBox _trendlineForwardBox;
    private readonly TextBox _trendlineBackwardBox;
    private readonly CheckBox _trendlineEquationCheck;
    private readonly CheckBox _trendlineRSquaredCheck;
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

    internal ChartSeriesOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartSeriesOptionsPlanner.FromChart(chart);
        var surface = ChartSeriesOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartSeriesOptionsPlanner.DefaultDialogWidth;
        Height = ChartSeriesOptionsPlanner.DefaultDialogHeight;
        MinWidth = 380;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _seriesCombo = new ComboBox
        {
            ItemsSource = _planner.SeriesOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = _planner.SeriesIndex,
            MinWidth = 200,
        };
        _seriesCombo.SelectionChanged += (_, _) =>
        {
            if (_seriesCombo.SelectedIndex >= 0)
            {
                _planner.SetSeriesIndex(_seriesCombo.SelectedIndex);
                LoadControls();
            }
        };
        _smoothLineCheck = new CheckBox { Content = surface.SmoothLineLabel };
        _secondaryAxisCheck = new CheckBox { Content = surface.SecondaryAxisLabel };
        _invertIfNegativeCheck = new CheckBox { Content = surface.InvertIfNegativeLabel };
        _seriesChartTypeCombo = new ComboBox
        {
            ItemsSource = ChartSeriesOptionsPlanner.SeriesChartTypeOptions.Select(option => option.Label).ToArray(),
            MinWidth = 200,
        };
        _lineWidthBox = new TextBox { MinWidth = 130 };
        _lineColorBox = new TextBox { MinWidth = 150 };
        _lineDashCombo = new ComboBox
        {
            ItemsSource = ChartSeriesOptionsPlanner.DashOptions.Select(option => option.Label).ToArray(),
            MinWidth = 160,
        };
        _noLineCheck = new CheckBox { Content = surface.NoLineLabel };
        _fillColorBox = new TextBox { MinWidth = 150 };
        _useSeriesDataLabelsCheck = new CheckBox { Content = surface.SeriesDataLabelsLabel };
        _showValueLabelsCheck = new CheckBox { Content = surface.ValueLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showPercentLabelsCheck = new CheckBox { Content = surface.PercentLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showCategoryLabelsCheck = new CheckBox { Content = surface.CategoryLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showSeriesLabelsCheck = new CheckBox { Content = surface.SeriesLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showLegendKeysCheck = new CheckBox { Content = surface.LegendKeysLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showBubbleSizeCheck = new CheckBox { Content = surface.BubbleSizeLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showLeaderLinesCheck = new CheckBox { Content = ChartSeriesOptionsPlanner.LeaderLinesLabel, IsThreeState = true, Margin = new Thickness(20, 0, 0, 0) };
        _errorBarsCheck = new CheckBox { Content = ChartSeriesOptionsPlanner.ErrorBarsLabel };
        _errorDirectionCombo = new ComboBox { ItemsSource = ChartSeriesOptionsPlanner.ErrorDirectionOptions.Select(option => option.Label).ToArray(), MinWidth = 160 };
        _errorBarTypeCombo = new ComboBox { ItemsSource = ChartSeriesOptionsPlanner.ErrorBarTypeOptions.Select(option => option.Label).ToArray(), MinWidth = 160 };
        _errorValueTypeCombo = new ComboBox { ItemsSource = ChartSeriesOptionsPlanner.ErrorValueTypeOptions.Select(option => option.Label).ToArray(), MinWidth = 160 };
        _errorValueBox = new TextBox { MinWidth = 130 };
        _errorNoEndCapCheck = new CheckBox { Content = ChartSeriesOptionsPlanner.ErrorNoEndCapLabel, Margin = new Thickness(20, 0, 0, 0) };
        _trendlineCheck = new CheckBox { Content = ChartSeriesOptionsPlanner.TrendlineLabel };
        _trendlineTypeCombo = new ComboBox { ItemsSource = ChartSeriesOptionsPlanner.TrendlineTypeOptions.Select(option => option.Label).ToArray(), MinWidth = 160 };
        _trendlineOrderBox = new TextBox { MinWidth = 130 };
        _trendlinePeriodBox = new TextBox { MinWidth = 130 };
        _trendlineForwardBox = new TextBox { MinWidth = 130 };
        _trendlineBackwardBox = new TextBox { MinWidth = 130 };
        _trendlineEquationCheck = new CheckBox { Content = ChartSeriesOptionsPlanner.TrendlineEquationLabel, Margin = new Thickness(20, 0, 0, 0) };
        _trendlineRSquaredCheck = new CheckBox { Content = ChartSeriesOptionsPlanner.TrendlineRSquaredLabel, Margin = new Thickness(20, 0, 0, 0) };
        _labelPositionCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.LabelPositionOptions.Select(option => option.Label).ToArray(),
            MinWidth = 160,
        };
        _labelNumberFormatBox = new TextBox { MinWidth = 150 };
        _labelSeparatorBox = new TextBox { MinWidth = 150 };
        _labelFontFamilyBox = new TextBox { MinWidth = 150 };
        _labelFontSizeBox = new TextBox { MinWidth = 130 };
        _labelBoldCheck = new CheckBox { Content = surface.BoldLabel, IsThreeState = true, Margin = new Thickness(20, 0, 0, 0) };
        _labelItalicCheck = new CheckBox { Content = surface.ItalicLabel, IsThreeState = true, Margin = new Thickness(20, 0, 0, 0) };
        _labelColorBox = new TextBox { MinWidth = 150 };
        _markerCombo = new ComboBox
        {
            ItemsSource = ChartSeriesOptionsPlanner.MarkerOptions.Select(option => option.Label).ToArray(),
            MinWidth = 160,
        };
        _markerSizeBox = new TextBox { MinWidth = 130 };
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

        var content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                MakeRow(surface.SeriesLabel, _seriesCombo),
                MakeRow(surface.SeriesChartTypeLabel, _seriesChartTypeCombo),
                _smoothLineCheck,
                _secondaryAxisCheck,
                _invertIfNegativeCheck,
                MakeRow(surface.LineWidthLabel, _lineWidthBox),
                MakeRow(surface.LineColorLabel, _lineColorBox),
                MakeRow(surface.LineDashLabel, _lineDashCombo),
                _noLineCheck,
                MakeRow(surface.FillColorLabel, _fillColorBox),
                _useSeriesDataLabelsCheck,
                _showValueLabelsCheck,
                _showPercentLabelsCheck,
                _showCategoryLabelsCheck,
                _showSeriesLabelsCheck,
                _showLegendKeysCheck,
                _showBubbleSizeCheck,
                _showLeaderLinesCheck,
                _errorBarsCheck,
                MakeRow(ChartSeriesOptionsPlanner.ErrorDirectionLabel, _errorDirectionCombo),
                MakeRow(ChartSeriesOptionsPlanner.ErrorBarTypeLabel, _errorBarTypeCombo),
                MakeRow(ChartSeriesOptionsPlanner.ErrorValueTypeLabel, _errorValueTypeCombo),
                MakeRow(ChartSeriesOptionsPlanner.ErrorValueLabel, _errorValueBox),
                _errorNoEndCapCheck,
                _trendlineCheck,
                MakeRow(ChartSeriesOptionsPlanner.TrendlineTypeLabel, _trendlineTypeCombo),
                MakeRow(ChartSeriesOptionsPlanner.TrendlineOrderLabel, _trendlineOrderBox),
                MakeRow(ChartSeriesOptionsPlanner.TrendlinePeriodLabel, _trendlinePeriodBox),
                MakeRow(ChartSeriesOptionsPlanner.TrendlineForwardLabel, _trendlineForwardBox),
                MakeRow(ChartSeriesOptionsPlanner.TrendlineBackwardLabel, _trendlineBackwardBox),
                _trendlineEquationCheck,
                _trendlineRSquaredCheck,
                MakeRow(surface.LabelPositionLabel, _labelPositionCombo),
                MakeRow(surface.NumberFormatLabel, _labelNumberFormatBox),
                MakeRow(surface.SeparatorLabel, _labelSeparatorBox),
                MakeRow(surface.FontFamilyLabel, _labelFontFamilyBox),
                MakeRow(surface.FontSizeLabel, _labelFontSizeBox),
                _labelBoldCheck,
                _labelItalicCheck,
                MakeRow(surface.LabelColorLabel, _labelColorBox),
                MakeRow(surface.MarkerLabel, _markerCombo),
                MakeRow(surface.MarkerSizeLabel, _markerSizeBox),
                new TextBlock { Text = surface.AutoHint, Opacity = 0.7 },
            },
        };

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(scrollViewer, 0);
        Grid.SetRow(buttons, 1);
        layout.Children.Add(scrollViewer);
        layout.Children.Add(buttons);
        Content = layout;
    }

    internal ChartSeriesOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(
        int seriesIndex,
        bool smoothLine,
        bool onSecondaryAxis,
        double? lineWidthPt,
        ChartMarkerSymbol markerSymbol,
        double? markerSizePt,
        string? fillColor = null,
        string? lineColor = null,
        OutlineDash lineDash = OutlineDash.Solid,
        bool noLine = false,
        bool useSeriesDataLabels = false,
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
        bool? showLeaderLines = null,
        bool errorBars = false,
        bool trendline = false,
        ChartTrendlineType trendlineType = ChartTrendlineType.Linear,
        int? trendlineOrder = null,
        int? trendlinePeriod = null,
        double? trendlineForward = null,
        double? trendlineBackward = null,
        bool trendlineEquation = false,
        bool trendlineRSquared = false,
        ChartType? overrideChartType = null,
        bool? invertIfNegative = null)
    {
        _seriesCombo.SelectedIndex = seriesIndex;
        _smoothLineCheck.IsChecked = smoothLine;
        _secondaryAxisCheck.IsChecked = onSecondaryAxis;
        if (invertIfNegative.HasValue)
            _invertIfNegativeCheck.IsChecked = invertIfNegative.Value;
        _seriesChartTypeCombo.SelectedIndex = FindSeriesChartTypeIndex(overrideChartType);
        _lineWidthBox.Text = Format(lineWidthPt);
        _lineColorBox.Text = lineColor ?? string.Empty;
        _lineDashCombo.SelectedIndex = FindDashIndex(lineDash);
        _noLineCheck.IsChecked = noLine;
        _markerCombo.SelectedIndex = FindMarkerIndex(markerSymbol);
        _markerSizeBox.Text = Format(markerSizePt);
        _fillColorBox.Text = fillColor ?? string.Empty;
        _useSeriesDataLabelsCheck.IsChecked = useSeriesDataLabels;
        _showValueLabelsCheck.IsChecked = showValueLabels;
        _showPercentLabelsCheck.IsChecked = showPercentLabels;
        _showCategoryLabelsCheck.IsChecked = showCategoryLabels;
        _showSeriesLabelsCheck.IsChecked = showSeriesLabels;
        _showLegendKeysCheck.IsChecked = showLegendKeys;
        _showBubbleSizeCheck.IsChecked = showBubbleSize;
        _showLeaderLinesCheck.IsChecked = showLeaderLines;
        _errorBarsCheck.IsChecked = errorBars;
        _trendlineCheck.IsChecked = trendline;
        _trendlineTypeCombo.SelectedIndex = FindTrendlineTypeIndex(trendlineType);
        _trendlineOrderBox.Text = Format(trendlineOrder);
        _trendlinePeriodBox.Text = Format(trendlinePeriod);
        _trendlineForwardBox.Text = Format(trendlineForward);
        _trendlineBackwardBox.Text = Format(trendlineBackward);
        _trendlineEquationCheck.IsChecked = trendlineEquation;
        _trendlineRSquaredCheck.IsChecked = trendlineRSquared;
        _labelPositionCombo.SelectedIndex = FindLabelPositionIndex(labelPosition);
        _labelNumberFormatBox.Text = labelNumberFormat ?? string.Empty;
        _labelSeparatorBox.Text = labelSeparator ?? string.Empty;
        _labelFontFamilyBox.Text = labelFontFamily ?? string.Empty;
        _labelFontSizeBox.Text = Format(labelFontSizePt);
        _labelBoldCheck.IsChecked = labelBold;
        _labelItalicCheck.IsChecked = labelItalic;
        _labelColorBox.Text = labelColor ?? string.Empty;
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartSeriesOptions(BuildCommitPlanForTests());
            Close(true);
        }
        catch (FormatException)
        {
            Close(false);
        }
    }

    private void LoadControls()
    {
        _smoothLineCheck.IsChecked = _planner.SmoothLine;
        _secondaryAxisCheck.IsChecked = _planner.OnSecondaryAxis;
        _invertIfNegativeCheck.IsChecked = _planner.InvertIfNegative == true;
        _seriesChartTypeCombo.SelectedIndex = FindSeriesChartTypeIndex(_planner.OverrideChartType);
        _lineWidthBox.Text = Format(_planner.LineWidthPt);
        _lineColorBox.Text = _planner.LineColorText;
        _lineDashCombo.SelectedIndex = FindDashIndex(_planner.LineDash);
        _noLineCheck.IsChecked = _planner.NoLine;
        _fillColorBox.Text = _planner.FillColorText;
        _useSeriesDataLabelsCheck.IsChecked = _planner.UseSeriesDataLabels;
        _showValueLabelsCheck.IsChecked = _planner.ShowValueLabels;
        _showPercentLabelsCheck.IsChecked = _planner.ShowPercentLabels;
        _showCategoryLabelsCheck.IsChecked = _planner.ShowCategoryLabels;
        _showSeriesLabelsCheck.IsChecked = _planner.ShowSeriesLabels;
        _showLegendKeysCheck.IsChecked = _planner.ShowLegendKeys;
        _showBubbleSizeCheck.IsChecked = _planner.ShowBubbleSize;
        _showLeaderLinesCheck.IsChecked = _planner.ShowLeaderLines;
        _errorBarsCheck.IsChecked = _planner.ErrorBarsEnabled;
        _errorDirectionCombo.SelectedIndex = FindErrorDirectionIndex(_planner.ErrorDirection);
        _errorBarTypeCombo.SelectedIndex = FindErrorBarTypeIndex(_planner.ErrorBarType);
        _errorValueTypeCombo.SelectedIndex = FindErrorValueTypeIndex(_planner.ErrorValueType);
        _errorValueBox.Text = Format(_planner.ErrorValue);
        _errorNoEndCapCheck.IsChecked = _planner.ErrorNoEndCap;
        _trendlineCheck.IsChecked = _planner.TrendlineEnabled;
        _trendlineTypeCombo.SelectedIndex = FindTrendlineTypeIndex(_planner.TrendlineType);
        _trendlineOrderBox.Text = Format(_planner.TrendlineOrder);
        _trendlinePeriodBox.Text = Format(_planner.TrendlinePeriod);
        _trendlineForwardBox.Text = Format(_planner.TrendlineForward);
        _trendlineBackwardBox.Text = Format(_planner.TrendlineBackward);
        _trendlineEquationCheck.IsChecked = _planner.TrendlineEquation;
        _trendlineRSquaredCheck.IsChecked = _planner.TrendlineRSquared;
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
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetSmoothLine(_smoothLineCheck.IsChecked == true);
        _planner.SetOnSecondaryAxis(_secondaryAxisCheck.IsChecked == true);
        _planner.SetInvertIfNegative(_invertIfNegativeCheck.IsChecked == true);
        if (_seriesChartTypeCombo.SelectedIndex >= 0 &&
            _seriesChartTypeCombo.SelectedIndex < ChartSeriesOptionsPlanner.SeriesChartTypeOptions.Count)
            _planner.SetOverrideChartType(ChartSeriesOptionsPlanner.SeriesChartTypeOptions[_seriesChartTypeCombo.SelectedIndex].Value);
        _planner.SetLineWidth(ParseOptional(_lineWidthBox.Text, "Line width"));
        _planner.SetLineColor(_lineColorBox.Text);
        if (_lineDashCombo.SelectedIndex >= 0 && _lineDashCombo.SelectedIndex < ChartSeriesOptionsPlanner.DashOptions.Count)
            _planner.SetLineDash(ChartSeriesOptionsPlanner.DashOptions[_lineDashCombo.SelectedIndex].Value);
        _planner.SetNoLine(_noLineCheck.IsChecked == true);
        _planner.SetFillColor(_fillColorBox.Text);
        _planner.SetUseSeriesDataLabels(_useSeriesDataLabelsCheck.IsChecked == true);
        _planner.SetShowValueLabels(_showValueLabelsCheck.IsChecked == true);
        _planner.SetShowPercentLabels(_showPercentLabelsCheck.IsChecked == true);
        _planner.SetShowCategoryLabels(_showCategoryLabelsCheck.IsChecked == true);
        _planner.SetShowSeriesLabels(_showSeriesLabelsCheck.IsChecked == true);
        _planner.SetShowLegendKeys(_showLegendKeysCheck.IsChecked == true);
        _planner.SetShowBubbleSize(_showBubbleSizeCheck.IsChecked == true);
        _planner.SetShowLeaderLines(_showLeaderLinesCheck.IsChecked);
        _planner.SetErrorBarsEnabled(_errorBarsCheck.IsChecked == true);
        if (_errorDirectionCombo.SelectedIndex >= 0 && _errorDirectionCombo.SelectedIndex < ChartSeriesOptionsPlanner.ErrorDirectionOptions.Count)
            _planner.SetErrorDirection(ChartSeriesOptionsPlanner.ErrorDirectionOptions[_errorDirectionCombo.SelectedIndex].Value);
        if (_errorBarTypeCombo.SelectedIndex >= 0 && _errorBarTypeCombo.SelectedIndex < ChartSeriesOptionsPlanner.ErrorBarTypeOptions.Count)
            _planner.SetErrorBarType(ChartSeriesOptionsPlanner.ErrorBarTypeOptions[_errorBarTypeCombo.SelectedIndex].Value);
        if (_errorValueTypeCombo.SelectedIndex >= 0 && _errorValueTypeCombo.SelectedIndex < ChartSeriesOptionsPlanner.ErrorValueTypeOptions.Count)
            _planner.SetErrorValueType(ChartSeriesOptionsPlanner.ErrorValueTypeOptions[_errorValueTypeCombo.SelectedIndex].Value);
        _planner.SetErrorValue(ParseOptional(_errorValueBox.Text, "Error bar value") ?? 0);
        _planner.SetErrorNoEndCap(_errorNoEndCapCheck.IsChecked == true);
        _planner.SetTrendlineEnabled(_trendlineCheck.IsChecked == true);
        if (_trendlineTypeCombo.SelectedIndex >= 0 && _trendlineTypeCombo.SelectedIndex < ChartSeriesOptionsPlanner.TrendlineTypeOptions.Count)
            _planner.SetTrendlineType(ChartSeriesOptionsPlanner.TrendlineTypeOptions[_trendlineTypeCombo.SelectedIndex].Value);
        _planner.SetTrendlineOrder(ParseOptionalInt(_trendlineOrderBox.Text, ChartSeriesOptionsPlanner.TrendlineOrderLabel));
        _planner.SetTrendlinePeriod(ParseOptionalInt(_trendlinePeriodBox.Text, ChartSeriesOptionsPlanner.TrendlinePeriodLabel));
        _planner.SetTrendlineForward(ParseOptional(_trendlineForwardBox.Text, ChartSeriesOptionsPlanner.TrendlineForwardLabel));
        _planner.SetTrendlineBackward(ParseOptional(_trendlineBackwardBox.Text, ChartSeriesOptionsPlanner.TrendlineBackwardLabel));
        _planner.SetTrendlineEquation(_trendlineEquationCheck.IsChecked == true);
        _planner.SetTrendlineRSquared(_trendlineRSquaredCheck.IsChecked == true);
        if (_labelPositionCombo.SelectedIndex >= 0 &&
            _labelPositionCombo.SelectedIndex < ChartDisplayOptionsPlanner.LabelPositionOptions.Count)
            _planner.SetLabelPosition(ChartDisplayOptionsPlanner.LabelPositionOptions[_labelPositionCombo.SelectedIndex].Value);
        _planner.SetLabelNumberFormat(_labelNumberFormatBox.Text);
        _planner.SetLabelSeparator(_labelSeparatorBox.Text);
        _planner.SetLabelFontFamily(_labelFontFamilyBox.Text);
        _planner.SetLabelFontSize(ParseOptional(_labelFontSizeBox.Text, "Label font size"));
        _planner.SetLabelBold(_labelBoldCheck.IsChecked);
        _planner.SetLabelItalic(_labelItalicCheck.IsChecked);
        _planner.SetLabelColor(_labelColorBox.Text);
        if (_markerCombo.SelectedIndex >= 0 && _markerCombo.SelectedIndex < ChartSeriesOptionsPlanner.MarkerOptions.Count)
            _planner.SetMarkerSymbol(ChartSeriesOptionsPlanner.MarkerOptions[_markerCombo.SelectedIndex].Value);
        _planner.SetMarkerSize(ParseOptional(_markerSizeBox.Text, "Marker size"));
    }

    private static double? ParseOptional(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
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
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value >= 0)
            return value;
        throw new FormatException($"{label} must be a non-negative integer or blank.");
    }

    private static int FindMarkerIndex(ChartMarkerSymbol symbol) =>
        Math.Max(0, ChartSeriesOptionsPlanner.MarkerOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == symbol).index);

    private static int FindSeriesChartTypeIndex(ChartType? value) =>
        Math.Max(0, ChartSeriesOptionsPlanner.SeriesChartTypeOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);

    private static int FindDashIndex(OutlineDash dash) =>
        Math.Max(0, ChartSeriesOptionsPlanner.DashOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == dash).index);

    private static int FindErrorDirectionIndex(ChartErrorDirection value) =>
        Math.Max(0, ChartSeriesOptionsPlanner.ErrorDirectionOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);

    private static int FindErrorBarTypeIndex(ChartErrorBarType value) =>
        Math.Max(0, ChartSeriesOptionsPlanner.ErrorBarTypeOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);

    private static int FindErrorValueTypeIndex(ChartErrorValueType value) =>
        Math.Max(0, ChartSeriesOptionsPlanner.ErrorValueTypeOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);

    private static int FindTrendlineTypeIndex(ChartTrendlineType value) =>
        Math.Max(0, ChartSeriesOptionsPlanner.TrendlineTypeOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);

    private static int FindLabelPositionIndex(DataLabelPosition position) =>
        Math.Max(0, ChartDisplayOptionsPlanner.LabelPositionOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == position).index);

    private static Control MakeRow(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("160, *") };
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
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
