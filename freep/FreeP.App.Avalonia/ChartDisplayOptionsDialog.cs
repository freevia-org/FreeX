using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartDisplayOptionsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly EditingSession _editor;
    private readonly ChartDisplayOptionsPlanner _planner;
    private readonly TextBox _titleBox;
    private readonly CheckBox _titleOverlayCheck;
    private readonly ComboBox _titlePositionCombo;
    private readonly ComboBox _titleAlignmentCombo;
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

    internal ChartDisplayOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartDisplayOptionsPlanner.FromChart(chart);
        var surface = ChartDisplayOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartDisplayOptionsPlanner.DefaultDialogWidth;
        Height = ChartDisplayOptionsPlanner.DefaultDialogHeight;
        MinWidth = 360;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _titleBox = new TextBox { Text = _planner.Title, MinWidth = 230 };
        _titleOverlayCheck = new CheckBox
        {
            Content = surface.TitleOverlayLabel,
            IsChecked = _planner.TitleOverlay,
        };
        _titlePositionCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.TitlePositionOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = FindTitlePositionIndex(_planner.TitlePosition),
            MinWidth = 150,
            IsEnabled = _planner.SupportsChartExTitleLayout,
        };
        _titleAlignmentCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.TitleAlignmentOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = FindTitleAlignmentIndex(_planner.TitleAlignment),
            MinWidth = 150,
            IsEnabled = _planner.SupportsChartExTitleLayout,
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
            ItemsSource = _planner.AvailableStyleOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = FindStyleIndex(_planner.StyleId),
            MinWidth = 150,
        };
        _legendCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.LegendOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = FindLegendIndex(_planner.Legend),
            MinWidth = 150,
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
        _numberFormatBox = new TextBox { Text = _planner.LabelNumberFormat, MinWidth = 150 };
        _separatorBox = new TextBox { Text = _planner.LabelSeparator, MinWidth = 150 };
        _labelFontFamilyBox = new TextBox { Text = _planner.LabelFontFamily, MinWidth = 150 };
        _labelFontSizeBox = new TextBox { Text = Format(_planner.LabelFontSizePt), MinWidth = 130 };
        _labelBoldCheck = new CheckBox { Content = surface.BoldLabel, IsThreeState = true, IsChecked = _planner.LabelBold };
        _labelItalicCheck = new CheckBox { Content = surface.ItalicLabel, IsThreeState = true, IsChecked = _planner.LabelItalic };
        _labelColorBox = new TextBox { Text = _planner.LabelColorText, MinWidth = 150 };
        _labelPositionCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.LabelPositionOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = FindLabelPositionIndex(_planner.LabelPosition),
            MinWidth = 150,
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
        _barGapWidthBox = new TextBox { Text = Format(_planner.BarGapWidthPercent), MinWidth = 150 };
        _barOverlapBox = new TextBox { Text = Format(_planner.BarOverlapPercent), MinWidth = 150 };
        _displayBlanksCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.DisplayBlanksOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = FindDisplayBlanksIndex(_planner.DisplayBlanksAs),
            MinWidth = 150,
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
                MakeRow(surface.ChartTitleLabel, _titleBox),
                _titleOverlayCheck,
                MakeRow(surface.TitlePositionLabel, _titlePositionCombo),
                MakeRow(surface.TitleAlignmentLabel, _titleAlignmentCombo),
                _plotVisibleOnlyCheck,
                _roundedCornersCheck,
                MakeRow(surface.ChartStyleLabel, _styleCombo),
                MakeRow(surface.LegendLabel, _legendCombo),
                MakeRow(surface.LabelPositionLabel, _labelPositionCombo),
                _valueLabelsCheck,
                _percentLabelsCheck,
                _categoryLabelsCheck,
                _seriesLabelsCheck,
                _legendKeysCheck,
                _bubbleSizeLabelsCheck,
                _showLeaderLinesCheck,
                MakeRow(surface.NumberFormatLabel, _numberFormatBox),
                MakeRow(surface.SeparatorLabel, _separatorBox),
                MakeRow(surface.FontFamilyLabel, _labelFontFamilyBox),
                MakeRow(surface.FontSizeLabel, _labelFontSizeBox),
                _labelBoldCheck,
                _labelItalicCheck,
                MakeRow(surface.LabelColorLabel, _labelColorBox),
                _categoryGridlinesCheck,
                _valueGridlinesCheck,
                MakeRow(surface.BarGapWidthLabel, _barGapWidthBox),
                MakeRow(surface.BarOverlapLabel, _barOverlapBox),
                MakeRow(surface.DisplayBlanksAsLabel, _displayBlanksCombo),
                _showDataLabelsOverMaximumCheck,
                _varyColorsCheck,
                _legendOverlayCheck,
                _highLowLinesCheck,
                _waterfallConnectorLinesCheck,
                _dropLinesCheck,
                _upDownBarsCheck,
                _seriesLinesCheck,
                new TextBlock { Text = surface.PlotHint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                buttons,
            },
        };
    }

    internal ChartDisplayOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetVaryColorsForTests(bool value) => _varyColorsCheck.IsChecked = value;

    internal void SetTitleOverlayForTests(bool value) => _titleOverlayCheck.IsChecked = value;

    internal void SetTitlePositionForTests(ChartExTitlePosition value) =>
        _titlePositionCombo.SelectedIndex = FindTitlePositionIndex(value);

    internal void SetTitleAlignmentForTests(ChartExTitleAlignment value) =>
        _titleAlignmentCombo.SelectedIndex = FindTitleAlignmentIndex(value);

    internal void SetPlotVisibleOnlyForTests(bool value) => _plotVisibleOnlyCheck.IsChecked = value;

    internal void SetRoundedCornersForTests(bool value) => _roundedCornersCheck.IsChecked = value;

    internal void SetStyleIdForTests(int? styleId) => _styleCombo.SelectedIndex = FindStyleIndex(styleId);

    internal void SetLegendOverlayForTests(bool? value) => _legendOverlayCheck.IsChecked = value;

    internal void SetHighLowLinesForTests(bool? value) => _highLowLinesCheck.IsChecked = value;

    internal void SetWaterfallConnectorLinesForTests(bool? value) => _waterfallConnectorLinesCheck.IsChecked = value;

    internal void SetDropLinesForTests(bool? value) => _dropLinesCheck.IsChecked = value;

    internal void SetUpDownBarsForTests(bool? value) => _upDownBarsCheck.IsChecked = value;

    internal void SetSeriesLinesForTests(bool? value) => _seriesLinesCheck.IsChecked = value;

    internal void SetLeaderLinesForTests(bool? value) => _showLeaderLinesCheck.IsChecked = value;

    internal void SetOptionsForTests(
        string title,
        LegendPosition? legend,
        bool showValueLabels,
        DataLabelPosition labelPosition,
        bool categoryGridlines,
        bool valueGridlines,
        bool showPercentLabels = false,
        bool showCategoryLabels = false,
        bool showSeriesLabels = false,
        bool showLegendKeys = false,
        string? numberFormat = null,
        string? separator = null,
        int? barGapWidthPercent = null,
        int? barOverlapPercent = null,
        ChartDisplayBlanksAs? displayBlanksAs = null,
        bool? showDataLabelsOverMaximum = null,
        string? labelFontFamily = null,
        double? labelFontSizePt = null,
        bool? labelBold = null,
        bool? labelItalic = null,
        string? labelColor = null,
        bool showBubbleSize = false)
    {
        _titleBox.Text = title;
        _legendCombo.SelectedIndex = FindLegendIndex(legend);
        _valueLabelsCheck.IsChecked = showValueLabels;
        _percentLabelsCheck.IsChecked = showPercentLabels;
        _categoryLabelsCheck.IsChecked = showCategoryLabels;
        _seriesLabelsCheck.IsChecked = showSeriesLabels;
        _legendKeysCheck.IsChecked = showLegendKeys;
        _bubbleSizeLabelsCheck.IsChecked = showBubbleSize;
        _showLeaderLinesCheck.IsChecked = null;
        _numberFormatBox.Text = numberFormat ?? string.Empty;
        _separatorBox.Text = separator ?? string.Empty;
        _labelPositionCombo.SelectedIndex = FindLabelPositionIndex(labelPosition);
        _categoryGridlinesCheck.IsChecked = categoryGridlines;
        _valueGridlinesCheck.IsChecked = valueGridlines;
        _barGapWidthBox.Text = Format(barGapWidthPercent);
        _barOverlapBox.Text = Format(barOverlapPercent);
        _displayBlanksCombo.SelectedIndex = FindDisplayBlanksIndex(displayBlanksAs);
        _showDataLabelsOverMaximumCheck.IsChecked = showDataLabelsOverMaximum;
        _labelFontFamilyBox.Text = labelFontFamily ?? string.Empty;
        _labelFontSizeBox.Text = Format(labelFontSizePt);
        _labelBoldCheck.IsChecked = labelBold;
        _labelItalicCheck.IsChecked = labelItalic;
        _labelColorBox.Text = labelColor ?? string.Empty;
    }

    private void OnOk()
    {
        _editor.ApplyChartDisplayOptions(BuildCommitPlanForTests());
        Close(true);
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetTitle(_titleBox.Text);
        _planner.SetTitleOverlay(_titleOverlayCheck.IsChecked == true);
        if (_planner.SupportsChartExTitleLayout)
        {
            if (_titlePositionCombo.SelectedIndex >= 0 &&
                _titlePositionCombo.SelectedIndex < ChartDisplayOptionsPlanner.TitlePositionOptions.Count)
                _planner.SetTitlePosition(ChartDisplayOptionsPlanner.TitlePositionOptions[_titlePositionCombo.SelectedIndex].Value);
            if (_titleAlignmentCombo.SelectedIndex >= 0 &&
                _titleAlignmentCombo.SelectedIndex < ChartDisplayOptionsPlanner.TitleAlignmentOptions.Count)
                _planner.SetTitleAlignment(ChartDisplayOptionsPlanner.TitleAlignmentOptions[_titleAlignmentCombo.SelectedIndex].Value);
        }
        _planner.SetPlotVisibleOnly(_plotVisibleOnlyCheck.IsChecked == true);
        _planner.SetRoundedCorners(_roundedCornersCheck.IsChecked == true);
        var styleIndex = _styleCombo.SelectedIndex;
        if (styleIndex >= 0 && styleIndex < ChartDisplayOptionsPlanner.StyleOptions.Count)
            _planner.SetStyleId(ChartDisplayOptionsPlanner.StyleOptions[styleIndex].Value);
        var legendIndex = _legendCombo.SelectedIndex;
        _planner.SetLegend(legendIndex >= 0 && legendIndex < ChartDisplayOptionsPlanner.LegendOptions.Count
            ? ChartDisplayOptionsPlanner.LegendOptions[legendIndex].Value
            : null);
        _planner.SetShowValueLabels(_valueLabelsCheck.IsChecked == true);
        _planner.SetShowPercentLabels(_percentLabelsCheck.IsChecked == true);
        _planner.SetShowCategoryLabels(_categoryLabelsCheck.IsChecked == true);
        _planner.SetShowSeriesLabels(_seriesLabelsCheck.IsChecked == true);
        _planner.SetShowLegendKeys(_legendKeysCheck.IsChecked == true);
        _planner.SetShowBubbleSize(_bubbleSizeLabelsCheck.IsChecked == true);
        _planner.SetShowLeaderLines(_showLeaderLinesCheck.IsChecked);
        var labelIndex = _labelPositionCombo.SelectedIndex;
        if (labelIndex >= 0 && labelIndex < ChartDisplayOptionsPlanner.LabelPositionOptions.Count)
            _planner.SetLabelPosition(ChartDisplayOptionsPlanner.LabelPositionOptions[labelIndex].Value);
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
        var blanksIndex = _displayBlanksCombo.SelectedIndex;
        if (blanksIndex >= 0 && blanksIndex < ChartDisplayOptionsPlanner.DisplayBlanksOptions.Count)
            _planner.SetDisplayBlanksAs(ChartDisplayOptionsPlanner.DisplayBlanksOptions[blanksIndex].Value);
        _planner.SetShowDataLabelsOverMaximum(_showDataLabelsOverMaximumCheck.IsChecked);
        _planner.SetVaryColors(_varyColorsCheck.IsChecked == true);
        _planner.SetLegendOverlay(_legendOverlayCheck.IsChecked);
        _planner.SetHighLowLines(_highLowLinesCheck.IsChecked);
        _planner.SetWaterfallConnectorLines(_waterfallConnectorLinesCheck.IsChecked);
        _planner.SetDropLines(_dropLinesCheck.IsChecked);
        _planner.SetUpDownBars(_upDownBarsCheck.IsChecked);
        _planner.SetSeriesLines(_seriesLinesCheck.IsChecked);
    }

    private static Control MakeRow(string label, Control control)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("150, *"),
        };
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

    private static int FindLegendIndex(LegendPosition? position) =>
        Math.Max(0, ChartDisplayOptionsPlanner.LegendOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == position).index);

    private static int FindStyleIndex(int? styleId) =>
        Math.Max(0, ChartDisplayOptionsPlanner.StyleOptionsFor(styleId)
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == styleId).index);

    private static int FindLabelPositionIndex(DataLabelPosition position) =>
        Math.Max(0, ChartDisplayOptionsPlanner.LabelPositionOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == position).index);

    private static int FindTitlePositionIndex(ChartExTitlePosition position) =>
        Math.Max(0, ChartDisplayOptionsPlanner.TitlePositionOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == position).index);

    private static int FindTitleAlignmentIndex(ChartExTitleAlignment alignment) =>
        Math.Max(0, ChartDisplayOptionsPlanner.TitleAlignmentOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == alignment).index);

    private static int FindDisplayBlanksIndex(ChartDisplayBlanksAs? value) =>
        Math.Max(0, ChartDisplayOptionsPlanner.DisplayBlanksOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);

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
