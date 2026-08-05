using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Small PowerPoint-style per-series chart formatting dialog.</summary>
public sealed class ChartSeriesOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
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

    public ChartSeriesOptionsDialog(EditingSession editor, int? initialSeriesIndex = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartSeriesOptionsPlanner.FromChart(chart);
        var surface = ChartSeriesOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartSeriesOptionsPlanner.DefaultDialogWidth;
        Height = ChartSeriesOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var selectedSeriesIndex = initialSeriesIndex ?? _planner.SeriesIndex;
        if (_planner.SeriesOptions.Count > 0)
            selectedSeriesIndex = Math.Clamp(selectedSeriesIndex, 0, _planner.SeriesOptions.Count - 1);
        else
            selectedSeriesIndex = 0;

        _seriesCombo = new ComboBox
        {
            ItemsSource = _planner.SeriesOptions,
            DisplayMemberPath = nameof(ChartSeriesOption.Label),
            SelectedIndex = selectedSeriesIndex,
            MinWidth = 200,
        };
        _seriesCombo.SelectionChanged += (_, _) =>
        {
            if (_seriesCombo.SelectedItem is ChartSeriesOption option)
            {
                _planner.SetSeriesIndex(option.Index);
                LoadControls();
            }
        };
        _planner.SetSeriesIndex(_seriesCombo.SelectedIndex);
        _smoothLineCheck = new CheckBox { Content = surface.SmoothLineLabel };
        _secondaryAxisCheck = new CheckBox { Content = surface.SecondaryAxisLabel };
        _invertIfNegativeCheck = new CheckBox { Content = surface.InvertIfNegativeLabel };
        _seriesChartTypeCombo = new ComboBox
        {
            ItemsSource = ChartSeriesOptionsPlanner.SeriesChartTypeOptions,
            DisplayMemberPath = nameof(ChartSeriesChartTypeOption.Label),
            MinWidth = 200,
        };
        _lineWidthBox = new TextBox { MinWidth = 120 };
        _lineColorBox = new TextBox { MinWidth = 140 };
        _lineDashCombo = new ComboBox
        {
            ItemsSource = ChartSeriesOptionsPlanner.DashOptions,
            DisplayMemberPath = nameof(ChartDashOption.Label),
            MinWidth = 150,
        };
        _noLineCheck = new CheckBox { Content = surface.NoLineLabel };
        _fillColorBox = new TextBox { MinWidth = 140 };
        _useSeriesDataLabelsCheck = new CheckBox { Content = surface.SeriesDataLabelsLabel };
        _showValueLabelsCheck = new CheckBox { Content = surface.ValueLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showPercentLabelsCheck = new CheckBox { Content = surface.PercentLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showCategoryLabelsCheck = new CheckBox { Content = surface.CategoryLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showSeriesLabelsCheck = new CheckBox { Content = surface.SeriesLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showLegendKeysCheck = new CheckBox { Content = surface.LegendKeysLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showBubbleSizeCheck = new CheckBox { Content = surface.BubbleSizeLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showLeaderLinesCheck = new CheckBox { Content = ChartSeriesOptionsPlanner.LeaderLinesLabel, IsThreeState = true, Margin = new Thickness(20, 0, 0, 0) };
        _errorBarsCheck = new CheckBox { Content = ChartSeriesOptionsPlanner.ErrorBarsLabel };
        _errorDirectionCombo = new ComboBox { ItemsSource = ChartSeriesOptionsPlanner.ErrorDirectionOptions, DisplayMemberPath = nameof(ChartErrorDirectionOption.Label), MinWidth = 150 };
        _errorBarTypeCombo = new ComboBox { ItemsSource = ChartSeriesOptionsPlanner.ErrorBarTypeOptions, DisplayMemberPath = nameof(ChartErrorBarTypeOption.Label), MinWidth = 150 };
        _errorValueTypeCombo = new ComboBox { ItemsSource = ChartSeriesOptionsPlanner.ErrorValueTypeOptions, DisplayMemberPath = nameof(ChartErrorValueTypeOption.Label), MinWidth = 150 };
        _errorValueBox = new TextBox { MinWidth = 120 };
        _errorNoEndCapCheck = new CheckBox { Content = ChartSeriesOptionsPlanner.ErrorNoEndCapLabel, Margin = new Thickness(20, 0, 0, 0) };
        _trendlineCheck = new CheckBox { Content = ChartSeriesOptionsPlanner.TrendlineLabel };
        _trendlineTypeCombo = new ComboBox { ItemsSource = ChartSeriesOptionsPlanner.TrendlineTypeOptions, DisplayMemberPath = nameof(ChartTrendlineTypeOption.Label), MinWidth = 150 };
        _trendlineOrderBox = new TextBox { MinWidth = 120 };
        _trendlinePeriodBox = new TextBox { MinWidth = 120 };
        _trendlineForwardBox = new TextBox { MinWidth = 120 };
        _trendlineBackwardBox = new TextBox { MinWidth = 120 };
        _trendlineEquationCheck = new CheckBox { Content = ChartSeriesOptionsPlanner.TrendlineEquationLabel, Margin = new Thickness(20, 0, 0, 0) };
        _trendlineRSquaredCheck = new CheckBox { Content = ChartSeriesOptionsPlanner.TrendlineRSquaredLabel, Margin = new Thickness(20, 0, 0, 0) };
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
        _labelBoldCheck = new CheckBox { Content = surface.BoldLabel, IsThreeState = true, Margin = new Thickness(20, 0, 0, 0) };
        _labelItalicCheck = new CheckBox { Content = surface.ItalicLabel, IsThreeState = true, Margin = new Thickness(20, 0, 0, 0) };
        _labelColorBox = new TextBox { MinWidth = 140 };
        _markerCombo = new ComboBox
        {
            ItemsSource = ChartSeriesOptionsPlanner.MarkerOptions,
            DisplayMemberPath = nameof(ChartMarkerSymbolOption.Label),
            MinWidth = 150,
        };
        _markerSizeBox = new TextBox { MinWidth = 120 };
        LoadControls();

        var buttons = ChartOptionsDialogChrome.CreateActionRow(surface.OkLabel, OnOk, surface.CancelLabel, Close, new Thickness(8, 14, 8, 8));

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(MakeRow(surface.SeriesLabel, _seriesCombo));
        content.Children.Add(MakeRow(surface.SeriesChartTypeLabel, _seriesChartTypeCombo));
        content.Children.Add(_smoothLineCheck);
        content.Children.Add(_secondaryAxisCheck);
        content.Children.Add(_invertIfNegativeCheck);
        content.Children.Add(MakeRow(surface.LineWidthLabel, _lineWidthBox));
        content.Children.Add(MakeRow(surface.LineColorLabel, _lineColorBox));
        content.Children.Add(MakeRow(surface.LineDashLabel, _lineDashCombo));
        content.Children.Add(_noLineCheck);
        content.Children.Add(MakeRow(surface.FillColorLabel, _fillColorBox));
        content.Children.Add(_useSeriesDataLabelsCheck);
        content.Children.Add(_showValueLabelsCheck);
        content.Children.Add(_showPercentLabelsCheck);
        content.Children.Add(_showCategoryLabelsCheck);
        content.Children.Add(_showSeriesLabelsCheck);
        content.Children.Add(_showLegendKeysCheck);
        content.Children.Add(_showBubbleSizeCheck);
        content.Children.Add(_showLeaderLinesCheck);
        content.Children.Add(_errorBarsCheck);
        content.Children.Add(MakeRow(ChartSeriesOptionsPlanner.ErrorDirectionLabel, _errorDirectionCombo));
        content.Children.Add(MakeRow(ChartSeriesOptionsPlanner.ErrorBarTypeLabel, _errorBarTypeCombo));
        content.Children.Add(MakeRow(ChartSeriesOptionsPlanner.ErrorValueTypeLabel, _errorValueTypeCombo));
        content.Children.Add(MakeRow(ChartSeriesOptionsPlanner.ErrorValueLabel, _errorValueBox));
        content.Children.Add(_errorNoEndCapCheck);
        content.Children.Add(_trendlineCheck);
        content.Children.Add(MakeRow(ChartSeriesOptionsPlanner.TrendlineTypeLabel, _trendlineTypeCombo));
        content.Children.Add(MakeRow(ChartSeriesOptionsPlanner.TrendlineOrderLabel, _trendlineOrderBox));
        content.Children.Add(MakeRow(ChartSeriesOptionsPlanner.TrendlinePeriodLabel, _trendlinePeriodBox));
        content.Children.Add(MakeRow(ChartSeriesOptionsPlanner.TrendlineForwardLabel, _trendlineForwardBox));
        content.Children.Add(MakeRow(ChartSeriesOptionsPlanner.TrendlineBackwardLabel, _trendlineBackwardBox));
        content.Children.Add(_trendlineEquationCheck);
        content.Children.Add(_trendlineRSquaredCheck);
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
        content.Children.Add(new TextBlock { Text = surface.AutoHint, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });

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
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
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
        _planner.SetOverrideChartType(ChartDialogOptionProjection.ValueAtOrDefault(ChartSeriesOptionsPlanner.SeriesChartTypeOptions, _seriesChartTypeCombo.SelectedIndex, option => option.Value, default(ChartType?)));
        _planner.SetLineWidth(ParseOptional(_lineWidthBox.Text, "Line width"));
        _planner.SetLineColor(_lineColorBox.Text);
        _planner.SetLineDash(ChartDialogOptionProjection.ValueAtOrDefault(ChartSeriesOptionsPlanner.DashOptions, _lineDashCombo.SelectedIndex, option => option.Value, _planner.LineDash));
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
        _planner.SetErrorDirection(ChartDialogOptionProjection.ValueAtOrDefault(ChartSeriesOptionsPlanner.ErrorDirectionOptions, _errorDirectionCombo.SelectedIndex, option => option.Value, _planner.ErrorDirection));
        _planner.SetErrorBarType(ChartDialogOptionProjection.ValueAtOrDefault(ChartSeriesOptionsPlanner.ErrorBarTypeOptions, _errorBarTypeCombo.SelectedIndex, option => option.Value, _planner.ErrorBarType));
        _planner.SetErrorValueType(ChartDialogOptionProjection.ValueAtOrDefault(ChartSeriesOptionsPlanner.ErrorValueTypeOptions, _errorValueTypeCombo.SelectedIndex, option => option.Value, _planner.ErrorValueType));
        _planner.SetErrorValue(ParseOptional(_errorValueBox.Text, "Error bar value") ?? 0);
        _planner.SetErrorNoEndCap(_errorNoEndCapCheck.IsChecked == true);
        _planner.SetTrendlineEnabled(_trendlineCheck.IsChecked == true);
        _planner.SetTrendlineType(ChartDialogOptionProjection.ValueAtOrDefault(ChartSeriesOptionsPlanner.TrendlineTypeOptions, _trendlineTypeCombo.SelectedIndex, option => option.Value, _planner.TrendlineType));
        _planner.SetTrendlineOrder(ParseOptionalInt(_trendlineOrderBox.Text, ChartSeriesOptionsPlanner.TrendlineOrderLabel));
        _planner.SetTrendlinePeriod(ParseOptionalInt(_trendlinePeriodBox.Text, ChartSeriesOptionsPlanner.TrendlinePeriodLabel));
        _planner.SetTrendlineForward(ParseOptional(_trendlineForwardBox.Text, ChartSeriesOptionsPlanner.TrendlineForwardLabel));
        _planner.SetTrendlineBackward(ParseOptional(_trendlineBackwardBox.Text, ChartSeriesOptionsPlanner.TrendlineBackwardLabel));
        _planner.SetTrendlineEquation(_trendlineEquationCheck.IsChecked == true);
        _planner.SetTrendlineRSquared(_trendlineRSquaredCheck.IsChecked == true);
        _planner.SetLabelPosition(ChartDialogOptionProjection.ValueAtOrDefault(ChartDisplayOptionsPlanner.LabelPositionOptions, _labelPositionCombo.SelectedIndex, option => option.Value, _planner.LabelPosition));
        _planner.SetLabelNumberFormat(_labelNumberFormatBox.Text);
        _planner.SetLabelSeparator(_labelSeparatorBox.Text);
        _planner.SetLabelFontFamily(_labelFontFamilyBox.Text);
        _planner.SetLabelFontSize(ParseOptional(_labelFontSizeBox.Text, "Label font size"));
        _planner.SetLabelBold(_labelBoldCheck.IsChecked);
        _planner.SetLabelItalic(_labelItalicCheck.IsChecked);
        _planner.SetLabelColor(_labelColorBox.Text);
        _planner.SetMarkerSymbol(ChartDialogOptionProjection.ValueAtOrDefault(ChartSeriesOptionsPlanner.MarkerOptions, _markerCombo.SelectedIndex, option => option.Value, _planner.MarkerSymbol));
        _planner.SetMarkerSize(ParseOptional(_markerSizeBox.Text, "Marker size"));
    }

    private static double? ParseOptional(string? text, string label)
    {
        return ChartDialogOptionProjection.ParseOptionalDouble(text, CultureInfo.CurrentCulture, value => double.IsFinite(value) && value >= 0, $"{label} must be a non-negative finite number or blank.");
    }

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static string Format(int? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static int? ParseOptionalInt(string? text, string label)
    {
        return ChartDialogOptionProjection.ParseOptionalInt(text, CultureInfo.CurrentCulture, value => value >= 0, $"{label} must be a non-negative integer or blank.");
    }

    private static int FindMarkerIndex(ChartMarkerSymbol symbol) =>
        ChartDialogOptionProjection.FindIndex(ChartSeriesOptionsPlanner.MarkerOptions, symbol, option => option.Value);

    private static int FindSeriesChartTypeIndex(ChartType? value) =>
        ChartDialogOptionProjection.FindIndex(ChartSeriesOptionsPlanner.SeriesChartTypeOptions, value, option => option.Value);

    private static int FindDashIndex(OutlineDash dash) =>
        ChartDialogOptionProjection.FindIndex(ChartSeriesOptionsPlanner.DashOptions, dash, option => option.Value);

    private static int FindErrorDirectionIndex(ChartErrorDirection value) =>
        ChartDialogOptionProjection.FindIndex(ChartSeriesOptionsPlanner.ErrorDirectionOptions, value, option => option.Value);

    private static int FindErrorBarTypeIndex(ChartErrorBarType value) =>
        ChartDialogOptionProjection.FindIndex(ChartSeriesOptionsPlanner.ErrorBarTypeOptions, value, option => option.Value);

    private static int FindErrorValueTypeIndex(ChartErrorValueType value) =>
        ChartDialogOptionProjection.FindIndex(ChartSeriesOptionsPlanner.ErrorValueTypeOptions, value, option => option.Value);

    private static int FindTrendlineTypeIndex(ChartTrendlineType value) =>
        ChartDialogOptionProjection.FindIndex(ChartSeriesOptionsPlanner.TrendlineTypeOptions, value, option => option.Value);

    private static int FindLabelPositionIndex(DataLabelPosition position) =>
        ChartDialogOptionProjection.FindIndex(ChartDisplayOptionsPlanner.LabelPositionOptions, position, option => option.Value);

    private static StackPanel MakeRow(string label, Control control) =>
        ChartOptionsDialogChrome.CreateRow(label, control, 160);
}
