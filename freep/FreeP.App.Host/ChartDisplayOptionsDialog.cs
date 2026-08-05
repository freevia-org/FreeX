using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Small PowerPoint-style chart display/options dialog.</summary>
public sealed class ChartDisplayOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartDisplayOptionsPlanner _planner;
    private readonly TextBox _titleBox;
    private readonly CheckBox _titleOverlayCheck;
    private readonly CheckBox _plotVisibleOnlyCheck;
    private readonly CheckBox _roundedCornersCheck;
    private readonly ComboBox _styleCombo;
    private readonly ComboBox _legendCombo;
    private readonly CheckBox _valueLabelsCheck;
    private readonly CheckBox _percentLabelsCheck;
    private readonly CheckBox _categoryLabelsCheck;
    private readonly CheckBox _seriesLabelsCheck;
    private readonly CheckBox _legendKeysCheck;
    private readonly CheckBox _bubbleSizeLabelsCheck;
    private readonly CheckBox _showLeaderLinesCheck;
    private readonly TextBox _numberFormatBox;
    private readonly TextBox _separatorBox;
    private readonly TextBox _labelFontFamilyBox;
    private readonly TextBox _labelFontSizeBox;
    private readonly CheckBox _labelBoldCheck;
    private readonly CheckBox _labelItalicCheck;
    private readonly TextBox _labelColorBox;
    private readonly ComboBox _labelPositionCombo;
    private readonly CheckBox _categoryGridlinesCheck;
    private readonly CheckBox _valueGridlinesCheck;
    private readonly TextBox _barGapWidthBox;
    private readonly TextBox _barOverlapBox;
    private readonly ComboBox _displayBlanksCombo;
    private readonly CheckBox _showDataLabelsOverMaximumCheck;
    private readonly CheckBox _varyColorsCheck;
    private readonly CheckBox _legendOverlayCheck;
    private readonly CheckBox _highLowLinesCheck;
    private readonly CheckBox _waterfallConnectorLinesCheck;
    private readonly CheckBox _dropLinesCheck;
    private readonly CheckBox _upDownBarsCheck;
    private readonly CheckBox _seriesLinesCheck;

    public ChartDisplayOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");

        _planner = ChartDisplayOptionsPlanner.FromChart(chart);
        var surface = ChartDisplayOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartDisplayOptionsPlanner.DefaultDialogWidth;
        Height = ChartDisplayOptionsPlanner.DefaultDialogHeight + 40;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _titleBox = new TextBox { Text = _planner.Title, MinWidth = 240 };
        _titleOverlayCheck = new CheckBox
        {
            Content = surface.TitleOverlayLabel,
            IsChecked = _planner.TitleOverlay,
        };
        _plotVisibleOnlyCheck = new CheckBox
        {
            Content = surface.PlotVisibleOnlyLabel,
            IsChecked = _planner.PlotVisibleOnly,
        };
        _roundedCornersCheck = new CheckBox
        {
            Content = surface.RoundedCornersLabel,
            IsChecked = _planner.RoundedCorners,
        };
        _styleCombo = new ComboBox
        {
            ItemsSource = _planner.AvailableStyleOptions,
            DisplayMemberPath = nameof(ChartDisplayStyleOption.Label),
            MinWidth = 160,
            SelectedIndex = FindStyleIndex(_planner.StyleId),
        };
        _legendCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.LegendOptions,
            DisplayMemberPath = nameof(ChartDisplayLegendOption.Label),
            MinWidth = 160,
            SelectedIndex = FindLegendIndex(_planner.Legend),
        };
        _valueLabelsCheck = new CheckBox
        {
            Content = surface.ValueLabelsLabel,
            IsChecked = _planner.ShowValueLabels,
        };
        _percentLabelsCheck = new CheckBox
        {
            Content = surface.PercentLabelsLabel,
            IsChecked = _planner.ShowPercentLabels,
        };
        _categoryLabelsCheck = new CheckBox
        {
            Content = surface.CategoryLabelsLabel,
            IsChecked = _planner.ShowCategoryLabels,
        };
        _seriesLabelsCheck = new CheckBox
        {
            Content = surface.SeriesLabelsLabel,
            IsChecked = _planner.ShowSeriesLabels,
        };
        _legendKeysCheck = new CheckBox
        {
            Content = surface.LegendKeysLabel,
            IsChecked = _planner.ShowLegendKeys,
        };
        _bubbleSizeLabelsCheck = new CheckBox
        {
            Content = surface.BubbleSizeLabelsLabel,
            IsChecked = _planner.ShowBubbleSize,
        };
        _showLeaderLinesCheck = new CheckBox
        {
            Content = surface.LeaderLinesLabel,
            IsThreeState = true,
            IsChecked = _planner.ShowLeaderLines,
            IsEnabled = chart.ChartType is ChartType.Pie or ChartType.Doughnut,
        };
        _numberFormatBox = new TextBox { Text = _planner.LabelNumberFormat, MinWidth = 160 };
        _separatorBox = new TextBox { Text = _planner.LabelSeparator, MinWidth = 160 };
        _labelFontFamilyBox = new TextBox { Text = _planner.LabelFontFamily, MinWidth = 160 };
        _labelFontSizeBox = new TextBox { Text = Format(_planner.LabelFontSizePt), MinWidth = 120 };
        _labelBoldCheck = new CheckBox { Content = surface.BoldLabel, IsThreeState = true, IsChecked = _planner.LabelBold };
        _labelItalicCheck = new CheckBox { Content = surface.ItalicLabel, IsThreeState = true, IsChecked = _planner.LabelItalic };
        _labelColorBox = new TextBox { Text = _planner.LabelColorText, MinWidth = 160 };
        _labelPositionCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.LabelPositionOptions,
            DisplayMemberPath = nameof(ChartDisplayLabelPositionOption.Label),
            MinWidth = 160,
            SelectedIndex = FindLabelPositionIndex(_planner.LabelPosition),
        };
        _categoryGridlinesCheck = new CheckBox
        {
            Content = surface.CategoryGridlinesLabel,
            IsChecked = _planner.CategoryGridlines,
        };
        _valueGridlinesCheck = new CheckBox
        {
            Content = surface.ValueGridlinesLabel,
            IsChecked = _planner.ValueGridlines,
        };
        _barGapWidthBox = new TextBox { Text = Format(_planner.BarGapWidthPercent), MinWidth = 160 };
        _barOverlapBox = new TextBox { Text = Format(_planner.BarOverlapPercent), MinWidth = 160 };
        _displayBlanksCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.DisplayBlanksOptions,
            DisplayMemberPath = nameof(ChartDisplayBlanksOption.Label),
            MinWidth = 160,
            SelectedIndex = FindDisplayBlanksIndex(_planner.DisplayBlanksAs),
        };
        _showDataLabelsOverMaximumCheck = new CheckBox
        {
            Content = surface.ShowDataLabelsOverMaximumLabel,
            IsThreeState = true,
            IsChecked = _planner.ShowDataLabelsOverMaximum,
        };
        _varyColorsCheck = new CheckBox
        {
            Content = surface.VaryColorsLabel,
            IsChecked = _planner.VaryColors,
        };
        _legendOverlayCheck = new CheckBox
        {
            Content = surface.LegendOverlayLabel,
            IsThreeState = true,
            IsChecked = _planner.LegendOverlay,
        };
        _highLowLinesCheck = new CheckBox
        {
            Content = surface.HighLowLinesLabel,
            IsThreeState = true,
            IsChecked = _planner.HighLowLines,
            IsEnabled = _planner.SupportsHighLowLines,
        };
        _waterfallConnectorLinesCheck = new CheckBox
        {
            Content = surface.WaterfallConnectorLinesLabel,
            IsThreeState = true,
            IsChecked = _planner.WaterfallConnectorLines,
            IsEnabled = _planner.SupportsWaterfallConnectorLines,
        };
        _dropLinesCheck = new CheckBox
        {
            Content = surface.DropLinesLabel,
            IsThreeState = true,
            IsChecked = _planner.DropLines,
            IsEnabled = _planner.SupportsDropLines,
        };
        _upDownBarsCheck = new CheckBox
        {
            Content = surface.UpDownBarsLabel,
            IsThreeState = true,
            IsChecked = _planner.UpDownBars,
            IsEnabled = _planner.SupportsUpDownBars,
        };
        _seriesLinesCheck = new CheckBox
        {
            Content = surface.SeriesLinesLabel,
            IsThreeState = true,
            IsChecked = _planner.SeriesLines,
            IsEnabled = _planner.SupportsSeriesLines,
        };

        var buttons = ChartOptionsDialogChrome.CreateActionRow(surface.OkLabel, OnOk, surface.CancelLabel, Close, new Thickness(8, 14, 8, 8));

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.ChartTitleLabel, _titleBox));
        content.Children.Add(_titleOverlayCheck);
        content.Children.Add(_plotVisibleOnlyCheck);
        content.Children.Add(_roundedCornersCheck);
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.ChartStyleLabel, _styleCombo));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.LegendLabel, _legendCombo));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.LabelPositionLabel, _labelPositionCombo));
        content.Children.Add(_valueLabelsCheck);
        content.Children.Add(_percentLabelsCheck);
        content.Children.Add(_categoryLabelsCheck);
        content.Children.Add(_seriesLabelsCheck);
        content.Children.Add(_legendKeysCheck);
        content.Children.Add(_bubbleSizeLabelsCheck);
        content.Children.Add(_showLeaderLinesCheck);
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.NumberFormatLabel, _numberFormatBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.SeparatorLabel, _separatorBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.FontFamilyLabel, _labelFontFamilyBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.FontSizeLabel, _labelFontSizeBox));
        content.Children.Add(_labelBoldCheck);
        content.Children.Add(_labelItalicCheck);
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.LabelColorLabel, _labelColorBox));
        content.Children.Add(_categoryGridlinesCheck);
        content.Children.Add(_valueGridlinesCheck);
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.BarGapWidthLabel, _barGapWidthBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.BarOverlapLabel, _barOverlapBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.DisplayBlanksAsLabel, _displayBlanksCombo));
        content.Children.Add(_showDataLabelsOverMaximumCheck);
        content.Children.Add(_varyColorsCheck);
        content.Children.Add(_legendOverlayCheck);
        content.Children.Add(_highLowLinesCheck);
        content.Children.Add(_waterfallConnectorLinesCheck);
        content.Children.Add(_dropLinesCheck);
        content.Children.Add(_upDownBarsCheck);
        content.Children.Add(_seriesLinesCheck);
        content.Children.Add(new TextBlock { Text = surface.PlotHint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartDisplayOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetVaryColorsForTests(bool value) => _varyColorsCheck.IsChecked = value;

    internal void SetTitleOverlayForTests(bool value) => _titleOverlayCheck.IsChecked = value;

    internal void SetPlotVisibleOnlyForTests(bool value) => _plotVisibleOnlyCheck.IsChecked = value;

    internal void SetRoundedCornersForTests(bool value) => _roundedCornersCheck.IsChecked = value;

    internal void SetStyleIdForTests(int? styleId) => _styleCombo.SelectedIndex = FindStyleIndex(styleId);

    internal void SetLegendOverlayForTests(bool? value) => _legendOverlayCheck.IsChecked = value;

    internal void SetHighLowLinesForTests(bool? value) => _highLowLinesCheck.IsChecked = value;

    internal void SetWaterfallConnectorLinesForTests(bool? value) => _waterfallConnectorLinesCheck.IsChecked = value;

    internal void SetDropLinesForTests(bool? value) => _dropLinesCheck.IsChecked = value;

    internal void SetUpDownBarsForTests(bool? value) => _upDownBarsCheck.IsChecked = value;

    internal void SetSeriesLinesForTests(bool? value) => _seriesLinesCheck.IsChecked = value;

    internal void SetLabelTextStyleForTests(string? family, double? sizePt, bool? bold, bool? italic, string? color)
    {
        _labelFontFamilyBox.Text = family ?? string.Empty;
        _labelFontSizeBox.Text = Format(sizePt);
        _labelBoldCheck.IsChecked = bold;
        _labelItalicCheck.IsChecked = italic;
        _labelColorBox.Text = color ?? string.Empty;
    }

    internal void SetBubbleSizeLabelsForTests(bool value) => _bubbleSizeLabelsCheck.IsChecked = value;

    internal void SetLeaderLinesForTests(bool? value) => _showLeaderLinesCheck.IsChecked = value;

    private void OnOk()
    {
        _editor.ApplyChartDisplayOptions(BuildCommitPlanForTests());
        DialogResult = true;
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetTitle(_titleBox.Text);
        _planner.SetTitleOverlay(_titleOverlayCheck.IsChecked == true);
        _planner.SetPlotVisibleOnly(_plotVisibleOnlyCheck.IsChecked == true);
        _planner.SetRoundedCorners(_roundedCornersCheck.IsChecked == true);
        _planner.SetStyleId(ChartDialogOptionProjection.ValueAtOrDefault(_planner.AvailableStyleOptions, _styleCombo.SelectedIndex, option => option.Value));
        _planner.SetLegend(ChartDialogOptionProjection.ValueAtOrDefault(ChartDisplayOptionsPlanner.LegendOptions, _legendCombo.SelectedIndex, option => option.Value, default(LegendPosition?)));
        _planner.SetShowValueLabels(_valueLabelsCheck.IsChecked == true);
        _planner.SetShowPercentLabels(_percentLabelsCheck.IsChecked == true);
        _planner.SetShowCategoryLabels(_categoryLabelsCheck.IsChecked == true);
        _planner.SetShowSeriesLabels(_seriesLabelsCheck.IsChecked == true);
        _planner.SetShowLegendKeys(_legendKeysCheck.IsChecked == true);
        _planner.SetShowBubbleSize(_bubbleSizeLabelsCheck.IsChecked == true);
        _planner.SetShowLeaderLines(_showLeaderLinesCheck.IsChecked);
        _planner.SetLabelPosition(ChartDialogOptionProjection.ValueAtOrDefault(ChartDisplayOptionsPlanner.LabelPositionOptions, _labelPositionCombo.SelectedIndex, option => option.Value));
        _planner.SetLabelNumberFormat(_numberFormatBox.Text);
        _planner.SetLabelSeparator(_separatorBox.Text);
        _planner.SetLabelFontFamily(_labelFontFamilyBox.Text);
        _planner.SetLabelFontSize(ParseOptional(_labelFontSizeBox.Text, "Label font size"));
        _planner.SetLabelBold(_labelBoldCheck.IsChecked);
        _planner.SetLabelItalic(_labelItalicCheck.IsChecked);
        _planner.SetLabelColor(_labelColorBox.Text);
        _planner.SetCategoryGridlines(_categoryGridlinesCheck.IsChecked == true);
        _planner.SetValueGridlines(_valueGridlinesCheck.IsChecked == true);
        _planner.SetBarGapWidthPercent(ParseOptionalPercent(_barGapWidthBox.Text, "Bar gap width", 0, 500));
        _planner.SetBarOverlapPercent(ParseOptionalPercent(_barOverlapBox.Text, "Bar overlap", -100, 100));
        _planner.SetDisplayBlanksAs(ChartDialogOptionProjection.ValueAtOrDefault(ChartDisplayOptionsPlanner.DisplayBlanksOptions, _displayBlanksCombo.SelectedIndex, option => option.Value, default(ChartDisplayBlanksAs?)));
        _planner.SetShowDataLabelsOverMaximum(_showDataLabelsOverMaximumCheck.IsChecked);
        _planner.SetVaryColors(_varyColorsCheck.IsChecked == true);
        _planner.SetLegendOverlay(_legendOverlayCheck.IsChecked);
        _planner.SetHighLowLines(_highLowLinesCheck.IsChecked);
        _planner.SetWaterfallConnectorLines(_waterfallConnectorLinesCheck.IsChecked);
        _planner.SetDropLines(_dropLinesCheck.IsChecked);
        _planner.SetUpDownBars(_upDownBarsCheck.IsChecked);
        _planner.SetSeriesLines(_seriesLinesCheck.IsChecked);
    }

    private static int FindLegendIndex(LegendPosition? position) =>
        ChartDialogOptionProjection.FindIndex(ChartDisplayOptionsPlanner.LegendOptions, position, option => option.Value);

    private static int FindStyleIndex(int? styleId) =>
        ChartDialogOptionProjection.FindIndex(ChartDisplayOptionsPlanner.StyleOptionsFor(styleId), styleId, option => option.Value);

    private static int FindLabelPositionIndex(DataLabelPosition position) =>
        ChartDialogOptionProjection.FindIndex(ChartDisplayOptionsPlanner.LabelPositionOptions, position, option => option.Value);

    private static int FindDisplayBlanksIndex(ChartDisplayBlanksAs? value) =>
        ChartDialogOptionProjection.FindIndex(ChartDisplayOptionsPlanner.DisplayBlanksOptions, value, option => option.Value);

    private static string Format(int? value) => ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static string Format(double? value) => ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static double? ParseOptional(string? text, string label)
    {
        return ChartDialogOptionProjection.ParseOptionalDouble(text, CultureInfo.CurrentCulture, value => double.IsFinite(value) && value >= 0, $"{label} must be a non-negative finite number or blank.");
    }

    private static int? ParseOptionalPercent(string? text, string surface, int minimum, int maximum)
    {
        return ChartDialogOptionProjection.ParseOptionalInt(text, CultureInfo.CurrentCulture, value => value >= minimum && value <= maximum, $"{surface} must be a whole number from {minimum} to {maximum}, or blank.");
    }
}
