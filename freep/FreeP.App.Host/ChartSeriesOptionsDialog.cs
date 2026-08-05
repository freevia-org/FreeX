using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Small PowerPoint-style per-series chart formatting dialog.</summary>
public sealed class ChartSeriesOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartSeriesOptionsDialogSession _session;
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
        _session = new ChartSeriesOptionsDialogSession(editor, initialSeriesIndex);
        var surface = _session.Surface;
        var state = _session.State;

        Title = surface.Title;
        Width = ChartSeriesOptionsPlanner.DefaultDialogWidth;
        Height = ChartSeriesOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _seriesCombo = new ComboBox
        {
            ItemsSource = state.SeriesOptions,
            DisplayMemberPath = nameof(ChartSeriesOption.Label),
            SelectedIndex = state.SeriesIndex,
            MinWidth = 200,
        };
        _seriesCombo.SelectionChanged += (_, _) =>
        {
            if (_seriesCombo.SelectedItem is ChartSeriesOption option)
                LoadControls(_session.SelectSeries(option.Index));
        };
        _smoothLineCheck = new CheckBox { Content = surface.SmoothLineLabel };
        _secondaryAxisCheck = new CheckBox { Content = surface.SecondaryAxisLabel };
        _invertIfNegativeCheck = new CheckBox { Content = surface.InvertIfNegativeLabel };
        _seriesChartTypeCombo = new ComboBox
        {
            ItemsSource = state.SeriesChartTypeOptions,
            DisplayMemberPath = nameof(ChartSeriesChartTypeOption.Label),
            MinWidth = 200,
        };
        _lineWidthBox = new TextBox { MinWidth = 120 };
        _lineColorBox = new TextBox { MinWidth = 140 };
        _lineDashCombo = new ComboBox
        {
            ItemsSource = _session.DashOptions,
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
        _showLeaderLinesCheck = new CheckBox { Content = _session.LeaderLinesLabel, IsThreeState = true, Margin = new Thickness(20, 0, 0, 0) };
        _errorBarsCheck = new CheckBox { Content = _session.ErrorBarsLabel };
        _errorDirectionCombo = new ComboBox { ItemsSource = _session.ErrorDirectionOptions, DisplayMemberPath = nameof(ChartErrorDirectionOption.Label), MinWidth = 150 };
        _errorBarTypeCombo = new ComboBox { ItemsSource = _session.ErrorBarTypeOptions, DisplayMemberPath = nameof(ChartErrorBarTypeOption.Label), MinWidth = 150 };
        _errorValueTypeCombo = new ComboBox { ItemsSource = _session.ErrorValueTypeOptions, DisplayMemberPath = nameof(ChartErrorValueTypeOption.Label), MinWidth = 150 };
        _errorValueBox = new TextBox { MinWidth = 120 };
        _errorNoEndCapCheck = new CheckBox { Content = _session.ErrorNoEndCapLabel, Margin = new Thickness(20, 0, 0, 0) };
        _trendlineCheck = new CheckBox { Content = _session.TrendlineLabel };
        _trendlineTypeCombo = new ComboBox { ItemsSource = _session.TrendlineTypeOptions, DisplayMemberPath = nameof(ChartTrendlineTypeOption.Label), MinWidth = 150 };
        _trendlineOrderBox = new TextBox { MinWidth = 120 };
        _trendlinePeriodBox = new TextBox { MinWidth = 120 };
        _trendlineForwardBox = new TextBox { MinWidth = 120 };
        _trendlineBackwardBox = new TextBox { MinWidth = 120 };
        _trendlineEquationCheck = new CheckBox { Content = _session.TrendlineEquationLabel, Margin = new Thickness(20, 0, 0, 0) };
        _trendlineRSquaredCheck = new CheckBox { Content = _session.TrendlineRSquaredLabel, Margin = new Thickness(20, 0, 0, 0) };
        _labelPositionCombo = new ComboBox
        {
            ItemsSource = _session.LabelPositionOptions,
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
            ItemsSource = _session.MarkerOptions,
            DisplayMemberPath = nameof(ChartMarkerSymbolOption.Label),
            MinWidth = 150,
        };
        _markerSizeBox = new TextBox { MinWidth = 120 };
        LoadControls(state);

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
        content.Children.Add(MakeRow(_session.ErrorDirectionLabel, _errorDirectionCombo));
        content.Children.Add(MakeRow(_session.ErrorBarTypeLabel, _errorBarTypeCombo));
        content.Children.Add(MakeRow(_session.ErrorValueTypeLabel, _errorValueTypeCombo));
        content.Children.Add(MakeRow(_session.ErrorValueLabel, _errorValueBox));
        content.Children.Add(_errorNoEndCapCheck);
        content.Children.Add(_trendlineCheck);
        content.Children.Add(MakeRow(_session.TrendlineTypeLabel, _trendlineTypeCombo));
        content.Children.Add(MakeRow(_session.TrendlineOrderLabel, _trendlineOrderBox));
        content.Children.Add(MakeRow(_session.TrendlinePeriodLabel, _trendlinePeriodBox));
        content.Children.Add(MakeRow(_session.TrendlineForwardLabel, _trendlineForwardBox));
        content.Children.Add(MakeRow(_session.TrendlineBackwardLabel, _trendlineBackwardBox));
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
        return _session.BuildCommitPlan(ReadInput());
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
        _seriesChartTypeCombo.SelectedIndex = _session.FindSeriesChartTypeIndex(overrideChartType);
        _lineWidthBox.Text = _session.Format(lineWidthPt);
        _lineColorBox.Text = lineColor ?? string.Empty;
        _lineDashCombo.SelectedIndex = _session.FindDashIndex(lineDash);
        _noLineCheck.IsChecked = noLine;
        _markerCombo.SelectedIndex = _session.FindMarkerIndex(markerSymbol);
        _markerSizeBox.Text = _session.Format(markerSizePt);
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
        _trendlineTypeCombo.SelectedIndex = _session.FindTrendlineTypeIndex(trendlineType);
        _trendlineOrderBox.Text = _session.Format(trendlineOrder);
        _trendlinePeriodBox.Text = _session.Format(trendlinePeriod);
        _trendlineForwardBox.Text = _session.Format(trendlineForward);
        _trendlineBackwardBox.Text = _session.Format(trendlineBackward);
        _trendlineEquationCheck.IsChecked = trendlineEquation;
        _trendlineRSquaredCheck.IsChecked = trendlineRSquared;
        _labelPositionCombo.SelectedIndex = _session.FindLabelPositionIndex(labelPosition);
        _labelNumberFormatBox.Text = labelNumberFormat ?? string.Empty;
        _labelSeparatorBox.Text = labelSeparator ?? string.Empty;
        _labelFontFamilyBox.Text = labelFontFamily ?? string.Empty;
        _labelFontSizeBox.Text = _session.Format(labelFontSizePt);
        _labelBoldCheck.IsChecked = labelBold;
        _labelItalicCheck.IsChecked = labelItalic;
        _labelColorBox.Text = labelColor ?? string.Empty;
    }

    private void OnOk()
    {
        var result = _session.TryCommit(ReadInput());
        if (result.Succeeded)
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, result.Error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void LoadControls(ChartSeriesOptionsDialogState state)
    {
        _seriesChartTypeCombo.ItemsSource = state.SeriesChartTypeOptions;
        _seriesChartTypeCombo.SelectedIndex = state.SeriesChartTypeIndex;
        _smoothLineCheck.IsChecked = state.SmoothLine;
        _secondaryAxisCheck.IsChecked = state.OnSecondaryAxis;
        _invertIfNegativeCheck.IsChecked = state.InvertIfNegative;
        _lineWidthBox.Text = state.LineWidthText;
        _lineColorBox.Text = state.LineColorText;
        _lineDashCombo.SelectedIndex = state.LineDashIndex;
        _noLineCheck.IsChecked = state.NoLine;
        _fillColorBox.Text = state.FillColorText;
        _useSeriesDataLabelsCheck.IsChecked = state.UseSeriesDataLabels;
        _showValueLabelsCheck.IsChecked = state.ShowValueLabels;
        _showPercentLabelsCheck.IsChecked = state.ShowPercentLabels;
        _showCategoryLabelsCheck.IsChecked = state.ShowCategoryLabels;
        _showSeriesLabelsCheck.IsChecked = state.ShowSeriesLabels;
        _showLegendKeysCheck.IsChecked = state.ShowLegendKeys;
        _showBubbleSizeCheck.IsChecked = state.ShowBubbleSize;
        _showLeaderLinesCheck.IsChecked = state.ShowLeaderLines;
        _errorBarsCheck.IsChecked = state.ErrorBarsEnabled;
        _errorDirectionCombo.SelectedIndex = state.ErrorDirectionIndex;
        _errorBarTypeCombo.SelectedIndex = state.ErrorBarTypeIndex;
        _errorValueTypeCombo.SelectedIndex = state.ErrorValueTypeIndex;
        _errorValueBox.Text = state.ErrorValueText;
        _errorNoEndCapCheck.IsChecked = state.ErrorNoEndCap;
        _trendlineCheck.IsChecked = state.TrendlineEnabled;
        _trendlineTypeCombo.SelectedIndex = state.TrendlineTypeIndex;
        _trendlineOrderBox.Text = state.TrendlineOrderText;
        _trendlinePeriodBox.Text = state.TrendlinePeriodText;
        _trendlineForwardBox.Text = state.TrendlineForwardText;
        _trendlineBackwardBox.Text = state.TrendlineBackwardText;
        _trendlineEquationCheck.IsChecked = state.TrendlineEquation;
        _trendlineRSquaredCheck.IsChecked = state.TrendlineRSquared;
        _labelPositionCombo.SelectedIndex = state.LabelPositionIndex;
        _labelNumberFormatBox.Text = state.LabelNumberFormat;
        _labelSeparatorBox.Text = state.LabelSeparator;
        _labelFontFamilyBox.Text = state.LabelFontFamily;
        _labelFontSizeBox.Text = state.LabelFontSizeText;
        _labelBoldCheck.IsChecked = state.LabelBold;
        _labelItalicCheck.IsChecked = state.LabelItalic;
        _labelColorBox.Text = state.LabelColorText;
        _markerCombo.SelectedIndex = state.MarkerIndex;
        _markerSizeBox.Text = state.MarkerSizeText;
    }

    private ChartSeriesOptionsDialogInput ReadInput() => new(
        _seriesCombo.SelectedIndex,
        _seriesChartTypeCombo.SelectedIndex,
        _smoothLineCheck.IsChecked == true,
        _secondaryAxisCheck.IsChecked == true,
        _invertIfNegativeCheck.IsChecked,
        _lineWidthBox.Text,
        _lineColorBox.Text,
        _lineDashCombo.SelectedIndex,
        _noLineCheck.IsChecked == true,
        _fillColorBox.Text,
        _useSeriesDataLabelsCheck.IsChecked == true,
        _showValueLabelsCheck.IsChecked == true,
        _showPercentLabelsCheck.IsChecked == true,
        _showCategoryLabelsCheck.IsChecked == true,
        _showSeriesLabelsCheck.IsChecked == true,
        _showLegendKeysCheck.IsChecked == true,
        _showBubbleSizeCheck.IsChecked == true,
        _showLeaderLinesCheck.IsChecked,
        _errorBarsCheck.IsChecked == true,
        _errorDirectionCombo.SelectedIndex,
        _errorBarTypeCombo.SelectedIndex,
        _errorValueTypeCombo.SelectedIndex,
        _errorValueBox.Text,
        _errorNoEndCapCheck.IsChecked == true,
        _trendlineCheck.IsChecked == true,
        _trendlineTypeCombo.SelectedIndex,
        _trendlineOrderBox.Text,
        _trendlinePeriodBox.Text,
        _trendlineForwardBox.Text,
        _trendlineBackwardBox.Text,
        _trendlineEquationCheck.IsChecked == true,
        _trendlineRSquaredCheck.IsChecked == true,
        _labelPositionCombo.SelectedIndex,
        _labelNumberFormatBox.Text,
        _labelSeparatorBox.Text,
        _labelFontFamilyBox.Text,
        _labelFontSizeBox.Text,
        _labelBoldCheck.IsChecked,
        _labelItalicCheck.IsChecked,
        _labelColorBox.Text,
        _markerCombo.SelectedIndex,
        _markerSizeBox.Text);

    private static StackPanel MakeRow(string label, Control control) =>
        ChartOptionsDialogChrome.CreateRow(label, control, 160);
}
