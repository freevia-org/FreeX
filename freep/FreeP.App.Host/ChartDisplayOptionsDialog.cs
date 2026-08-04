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
        content.Children.Add(MakeRow(surface.ChartTitleLabel, _titleBox));
        content.Children.Add(_titleOverlayCheck);
        content.Children.Add(_plotVisibleOnlyCheck);
        content.Children.Add(_roundedCornersCheck);
        content.Children.Add(MakeRow(surface.ChartStyleLabel, _styleCombo));
        content.Children.Add(MakeRow(surface.LegendLabel, _legendCombo));
        content.Children.Add(MakeRow(surface.LabelPositionLabel, _labelPositionCombo));
        content.Children.Add(_valueLabelsCheck);
        content.Children.Add(_percentLabelsCheck);
        content.Children.Add(_categoryLabelsCheck);
        content.Children.Add(_seriesLabelsCheck);
        content.Children.Add(_legendKeysCheck);
        content.Children.Add(_bubbleSizeLabelsCheck);
        content.Children.Add(_showLeaderLinesCheck);
        content.Children.Add(MakeRow(surface.NumberFormatLabel, _numberFormatBox));
        content.Children.Add(MakeRow(surface.SeparatorLabel, _separatorBox));
        content.Children.Add(MakeRow(surface.FontFamilyLabel, _labelFontFamilyBox));
        content.Children.Add(MakeRow(surface.FontSizeLabel, _labelFontSizeBox));
        content.Children.Add(_labelBoldCheck);
        content.Children.Add(_labelItalicCheck);
        content.Children.Add(MakeRow(surface.LabelColorLabel, _labelColorBox));
        content.Children.Add(_categoryGridlinesCheck);
        content.Children.Add(_valueGridlinesCheck);
        content.Children.Add(MakeRow(surface.BarGapWidthLabel, _barGapWidthBox));
        content.Children.Add(MakeRow(surface.BarOverlapLabel, _barOverlapBox));
        content.Children.Add(MakeRow(surface.DisplayBlanksAsLabel, _displayBlanksCombo));
        content.Children.Add(_showDataLabelsOverMaximumCheck);
        content.Children.Add(_varyColorsCheck);
        content.Children.Add(_legendOverlayCheck);
        content.Children.Add(_highLowLinesCheck);
        content.Children.Add(_waterfallConnectorLinesCheck);
        content.Children.Add(_dropLinesCheck);
        content.Children.Add(_upDownBarsCheck);
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
        if (_styleCombo.SelectedItem is ChartDisplayStyleOption style)
            _planner.SetStyleId(style.Value);
        _planner.SetLegend(_legendCombo.SelectedItem is ChartDisplayLegendOption legend ? legend.Value : null);
        _planner.SetShowValueLabels(_valueLabelsCheck.IsChecked == true);
        _planner.SetShowPercentLabels(_percentLabelsCheck.IsChecked == true);
        _planner.SetShowCategoryLabels(_categoryLabelsCheck.IsChecked == true);
        _planner.SetShowSeriesLabels(_seriesLabelsCheck.IsChecked == true);
        _planner.SetShowLegendKeys(_legendKeysCheck.IsChecked == true);
        _planner.SetShowBubbleSize(_bubbleSizeLabelsCheck.IsChecked == true);
        _planner.SetShowLeaderLines(_showLeaderLinesCheck.IsChecked);
        if (_labelPositionCombo.SelectedItem is ChartDisplayLabelPositionOption position)
            _planner.SetLabelPosition(position.Value);
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
        if (_displayBlanksCombo.SelectedItem is ChartDisplayBlanksOption blanks)
            _planner.SetDisplayBlanksAs(blanks.Value);
        _planner.SetShowDataLabelsOverMaximum(_showDataLabelsOverMaximumCheck.IsChecked);
        _planner.SetVaryColors(_varyColorsCheck.IsChecked == true);
        _planner.SetLegendOverlay(_legendOverlayCheck.IsChecked);
        _planner.SetHighLowLines(_highLowLinesCheck.IsChecked);
        _planner.SetWaterfallConnectorLines(_waterfallConnectorLinesCheck.IsChecked);
        _planner.SetDropLines(_dropLinesCheck.IsChecked);
        _planner.SetUpDownBars(_upDownBarsCheck.IsChecked);
    }

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        row.Children.Add(new Label { Content = label, Width = 150, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }

    private static int FindLegendIndex(LegendPosition? position) =>
        ChartDisplayOptionsPlanner.LegendOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == position).index;

    private static int FindStyleIndex(int? styleId) =>
        Math.Max(0, ChartDisplayOptionsPlanner.StyleOptionsFor(styleId)
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == styleId).index);

    private static int FindLabelPositionIndex(DataLabelPosition position) =>
        Math.Max(0, ChartDisplayOptionsPlanner.LabelPositionOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == position).index);

    private static int FindDisplayBlanksIndex(ChartDisplayBlanksAs? value) =>
        ChartDisplayOptionsPlanner.DisplayBlanksOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index;

    private static string Format(int? value) => value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

    private static string Format(double? value) => value?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;

    private static double? ParseOptional(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            double.IsFinite(value) && value >= 0)
            return value;
        throw new FormatException($"{label} must be a non-negative finite number or blank.");
    }

    private static int? ParseOptionalPercent(string? text, string surface, int minimum, int maximum)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value >= minimum && value <= maximum)
            return value;
        throw new FormatException($"{surface} must be a whole number from {minimum} to {maximum}, or blank.");
    }
}
