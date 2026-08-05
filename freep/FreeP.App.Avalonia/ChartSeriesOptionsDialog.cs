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

    internal ChartSeriesOptionsDialog(EditingSession editor, int? initialSeriesIndex = null)
    {
        _session = new ChartSeriesOptionsDialogSession(editor, initialSeriesIndex);
        var surface = _session.Surface;
        var state = _session.State;

        Title = surface.Title;
        Width = ChartSeriesOptionsPlanner.DefaultDialogWidth;
        Height = ChartSeriesOptionsPlanner.DefaultDialogHeight;
        MinWidth = 380;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _seriesCombo = new ComboBox
        {
            ItemsSource = state.SeriesOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = state.SeriesIndex,
            MinWidth = 200,
        };
        _seriesCombo.SelectionChanged += (_, _) =>
        {
            if (_seriesCombo.SelectedIndex >= 0)
                LoadControls(_session.SelectSeries(_seriesCombo.SelectedIndex));
        };
        _smoothLineCheck = new CheckBox { Content = surface.SmoothLineLabel };
        _secondaryAxisCheck = new CheckBox { Content = surface.SecondaryAxisLabel };
        _invertIfNegativeCheck = new CheckBox { Content = surface.InvertIfNegativeLabel };
        _seriesChartTypeCombo = new ComboBox
        {
            ItemsSource = state.SeriesChartTypeOptions.Select(option => option.Label).ToArray(),
            MinWidth = 200,
        };
        _lineWidthBox = new TextBox { MinWidth = 130 };
        _lineColorBox = new TextBox { MinWidth = 150 };
        _lineDashCombo = new ComboBox
        {
            ItemsSource = _session.DashOptions.Select(option => option.Label).ToArray(),
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
        _showLeaderLinesCheck = new CheckBox { Content = _session.LeaderLinesLabel, IsThreeState = true, Margin = new Thickness(20, 0, 0, 0) };
        _errorBarsCheck = new CheckBox { Content = _session.ErrorBarsLabel };
        _errorDirectionCombo = new ComboBox { ItemsSource = _session.ErrorDirectionOptions.Select(option => option.Label).ToArray(), MinWidth = 160 };
        _errorBarTypeCombo = new ComboBox { ItemsSource = _session.ErrorBarTypeOptions.Select(option => option.Label).ToArray(), MinWidth = 160 };
        _errorValueTypeCombo = new ComboBox { ItemsSource = _session.ErrorValueTypeOptions.Select(option => option.Label).ToArray(), MinWidth = 160 };
        _errorValueBox = new TextBox { MinWidth = 130 };
        _errorNoEndCapCheck = new CheckBox { Content = _session.ErrorNoEndCapLabel, Margin = new Thickness(20, 0, 0, 0) };
        _trendlineCheck = new CheckBox { Content = _session.TrendlineLabel };
        _trendlineTypeCombo = new ComboBox { ItemsSource = _session.TrendlineTypeOptions.Select(option => option.Label).ToArray(), MinWidth = 160 };
        _trendlineOrderBox = new TextBox { MinWidth = 130 };
        _trendlinePeriodBox = new TextBox { MinWidth = 130 };
        _trendlineForwardBox = new TextBox { MinWidth = 130 };
        _trendlineBackwardBox = new TextBox { MinWidth = 130 };
        _trendlineEquationCheck = new CheckBox { Content = _session.TrendlineEquationLabel, Margin = new Thickness(20, 0, 0, 0) };
        _trendlineRSquaredCheck = new CheckBox { Content = _session.TrendlineRSquaredLabel, Margin = new Thickness(20, 0, 0, 0) };
        _labelPositionCombo = new ComboBox
        {
            ItemsSource = _session.LabelPositionOptions.Select(option => option.Label).ToArray(),
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
            ItemsSource = _session.MarkerOptions.Select(option => option.Label).ToArray(),
            MinWidth = 160,
        };
        _markerSizeBox = new TextBox { MinWidth = 130 };
        LoadControls(state);

        var buttons = ChartOptionsDialogChrome.CreateActionRow(surface.OkLabel, OnOk, surface.CancelLabel, () => Close(false));

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
                MakeRow(_session.ErrorDirectionLabel, _errorDirectionCombo),
                MakeRow(_session.ErrorBarTypeLabel, _errorBarTypeCombo),
                MakeRow(_session.ErrorValueTypeLabel, _errorValueTypeCombo),
                MakeRow(_session.ErrorValueLabel, _errorValueBox),
                _errorNoEndCapCheck,
                _trendlineCheck,
                MakeRow(_session.TrendlineTypeLabel, _trendlineTypeCombo),
                MakeRow(_session.TrendlineOrderLabel, _trendlineOrderBox),
                MakeRow(_session.TrendlinePeriodLabel, _trendlinePeriodBox),
                MakeRow(_session.TrendlineForwardLabel, _trendlineForwardBox),
                MakeRow(_session.TrendlineBackwardLabel, _trendlineBackwardBox),
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
        if (_session.TryCommit(ReadInput()).Succeeded)
        {
            Close(true);
            return;
        }

        Close(false);
    }

    private void LoadControls(ChartSeriesOptionsDialogState state)
    {
        _seriesChartTypeCombo.ItemsSource = state.SeriesChartTypeOptions.Select(option => option.Label).ToArray();
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

    private static Control MakeRow(string label, Control control) =>
        ChartOptionsDialogChrome.CreateRow(label, control, 160);
}
