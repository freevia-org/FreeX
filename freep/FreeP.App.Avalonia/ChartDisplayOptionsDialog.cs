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
    private readonly ChartDisplayOptionsDialogSession _session;
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
        _session = new ChartDisplayOptionsDialogSession(editor);
        var state = _session.State;
        var surface = _session.Surface;

        Title = surface.Title;
        Width = ChartDisplayOptionsPlanner.DefaultDialogWidth;
        Height = ChartDisplayOptionsPlanner.DefaultDialogHeight;
        MinWidth = 360;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _titleBox = new TextBox { Text = state.Title, MinWidth = 230 };
        _titleOverlayCheck = new CheckBox
        {
            Content = surface.TitleOverlayLabel,
            IsChecked = state.TitleOverlay,
        };
        _titlePositionCombo = new ComboBox
        {
            ItemsSource = _session.TitlePositionOptions,
            SelectedIndex = state.TitlePositionIndex,
            MinWidth = 150,
            IsEnabled = state.SupportsChartExTitleLayout,
        };
        _titleAlignmentCombo = new ComboBox
        {
            ItemsSource = _session.TitleAlignmentOptions,
            SelectedIndex = state.TitleAlignmentIndex,
            MinWidth = 150,
            IsEnabled = state.SupportsChartExTitleLayout,
        };
        _plotVisibleOnlyCheck = new CheckBox
        {
            Content = surface.PlotVisibleOnlyLabel,
            IsChecked = state.PlotVisibleOnly,
        };
        _roundedCornersCheck = new CheckBox
        {
            Content = surface.RoundedCornersLabel,
            IsChecked = state.RoundedCorners,
        };
        _styleCombo = new ComboBox
        {
            ItemsSource = _session.StyleOptions,
            SelectedIndex = state.StyleIndex,
            MinWidth = 150,
        };
        _legendCombo = new ComboBox
        {
            ItemsSource = _session.LegendOptions,
            SelectedIndex = state.LegendIndex,
            MinWidth = 150,
        };
        _valueLabelsCheck = new CheckBox
        {
            Content = surface.ValueLabelsLabel,
            IsChecked = state.ShowValueLabels,
        };
        _percentLabelsCheck = new CheckBox
        {
            Content = surface.PercentLabelsLabel,
            IsChecked = state.ShowPercentLabels,
        };
        _categoryLabelsCheck = new CheckBox
        {
            Content = surface.CategoryLabelsLabel,
            IsChecked = state.ShowCategoryLabels,
        };
        _seriesLabelsCheck = new CheckBox
        {
            Content = surface.SeriesLabelsLabel,
            IsChecked = state.ShowSeriesLabels,
        };
        _legendKeysCheck = new CheckBox
        {
            Content = surface.LegendKeysLabel,
            IsChecked = state.ShowLegendKeys,
        };
        _bubbleSizeLabelsCheck = new CheckBox
        {
            Content = surface.BubbleSizeLabelsLabel,
            IsChecked = state.ShowBubbleSize,
        };
        _showLeaderLinesCheck = new CheckBox
        {
            Content = surface.LeaderLinesLabel,
            IsThreeState = true,
            IsChecked = state.ShowLeaderLines,
            IsEnabled = state.SupportsLeaderLines,
        };
        _numberFormatBox = new TextBox { Text = state.LabelNumberFormat, MinWidth = 150 };
        _separatorBox = new TextBox { Text = state.LabelSeparator, MinWidth = 150 };
        _labelFontFamilyBox = new TextBox { Text = state.LabelFontFamily, MinWidth = 150 };
        _labelFontSizeBox = new TextBox { Text = state.LabelFontSizeText, MinWidth = 130 };
        _labelBoldCheck = new CheckBox { Content = surface.BoldLabel, IsThreeState = true, IsChecked = state.LabelBold };
        _labelItalicCheck = new CheckBox { Content = surface.ItalicLabel, IsThreeState = true, IsChecked = state.LabelItalic };
        _labelColorBox = new TextBox { Text = state.LabelColor, MinWidth = 150 };
        _labelPositionCombo = new ComboBox
        {
            ItemsSource = _session.LabelPositionOptions,
            SelectedIndex = state.LabelPositionIndex,
            MinWidth = 150,
        };
        _categoryGridlinesCheck = new CheckBox
        {
            Content = surface.CategoryGridlinesLabel,
            IsChecked = state.CategoryGridlines,
        };
        _valueGridlinesCheck = new CheckBox
        {
            Content = surface.ValueGridlinesLabel,
            IsChecked = state.ValueGridlines,
        };
        _barGapWidthBox = new TextBox { Text = state.BarGapWidthText, MinWidth = 150 };
        _barOverlapBox = new TextBox { Text = state.BarOverlapText, MinWidth = 150 };
        _displayBlanksCombo = new ComboBox
        {
            ItemsSource = _session.DisplayBlanksOptions,
            SelectedIndex = state.DisplayBlanksIndex,
            MinWidth = 150,
        };
        _showDataLabelsOverMaximumCheck = new CheckBox
        {
            Content = surface.ShowDataLabelsOverMaximumLabel,
            IsThreeState = true,
            IsChecked = state.ShowDataLabelsOverMaximum,
        };
        _varyColorsCheck = new CheckBox
        {
            Content = surface.VaryColorsLabel,
            IsChecked = state.VaryColors,
        };
        _legendOverlayCheck = new CheckBox
        {
            Content = surface.LegendOverlayLabel,
            IsThreeState = true,
            IsChecked = state.LegendOverlay,
        };
        _highLowLinesCheck = new CheckBox
        {
            Content = surface.HighLowLinesLabel,
            IsThreeState = true,
            IsChecked = state.HighLowLines,
            IsEnabled = state.SupportsHighLowLines,
        };
        _waterfallConnectorLinesCheck = new CheckBox
        {
            Content = surface.WaterfallConnectorLinesLabel,
            IsThreeState = true,
            IsChecked = state.WaterfallConnectorLines,
            IsEnabled = state.SupportsWaterfallConnectorLines,
        };
        _dropLinesCheck = new CheckBox
        {
            Content = surface.DropLinesLabel,
            IsThreeState = true,
            IsChecked = state.DropLines,
            IsEnabled = state.SupportsDropLines,
        };
        _upDownBarsCheck = new CheckBox
        {
            Content = surface.UpDownBarsLabel,
            IsThreeState = true,
            IsChecked = state.UpDownBars,
            IsEnabled = state.SupportsUpDownBars,
        };
        _seriesLinesCheck = new CheckBox
        {
            Content = surface.SeriesLinesLabel,
            IsThreeState = true,
            IsChecked = state.SeriesLines,
            IsEnabled = state.SupportsSeriesLines,
        };

        var buttons = ChartOptionsDialogChrome.CreateActionRow(surface.OkLabel, OnOk, surface.CancelLabel, () => Close(false));

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                ChartOptionsDialogChrome.CreateRow(surface.ChartTitleLabel, _titleBox),
                _titleOverlayCheck,
                ChartOptionsDialogChrome.CreateRow(surface.TitlePositionLabel, _titlePositionCombo),
                ChartOptionsDialogChrome.CreateRow(surface.TitleAlignmentLabel, _titleAlignmentCombo),
                _plotVisibleOnlyCheck,
                _roundedCornersCheck,
                ChartOptionsDialogChrome.CreateRow(surface.ChartStyleLabel, _styleCombo),
                ChartOptionsDialogChrome.CreateRow(surface.LegendLabel, _legendCombo),
                ChartOptionsDialogChrome.CreateRow(surface.LabelPositionLabel, _labelPositionCombo),
                _valueLabelsCheck,
                _percentLabelsCheck,
                _categoryLabelsCheck,
                _seriesLabelsCheck,
                _legendKeysCheck,
                _bubbleSizeLabelsCheck,
                _showLeaderLinesCheck,
                ChartOptionsDialogChrome.CreateRow(surface.NumberFormatLabel, _numberFormatBox),
                ChartOptionsDialogChrome.CreateRow(surface.SeparatorLabel, _separatorBox),
                ChartOptionsDialogChrome.CreateRow(surface.FontFamilyLabel, _labelFontFamilyBox),
                ChartOptionsDialogChrome.CreateRow(surface.FontSizeLabel, _labelFontSizeBox),
                _labelBoldCheck,
                _labelItalicCheck,
                ChartOptionsDialogChrome.CreateRow(surface.LabelColorLabel, _labelColorBox),
                _categoryGridlinesCheck,
                _valueGridlinesCheck,
                ChartOptionsDialogChrome.CreateRow(surface.BarGapWidthLabel, _barGapWidthBox),
                ChartOptionsDialogChrome.CreateRow(surface.BarOverlapLabel, _barOverlapBox),
                ChartOptionsDialogChrome.CreateRow(surface.DisplayBlanksAsLabel, _displayBlanksCombo),
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

    internal ChartDisplayOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetVaryColorsForTests(bool value) => _varyColorsCheck.IsChecked = value;

    internal void SetTitleOverlayForTests(bool value) => _titleOverlayCheck.IsChecked = value;

    internal void SetTitlePositionForTests(ChartExTitlePosition value) =>
        _titlePositionCombo.SelectedIndex = _session.FindTitlePositionIndex(value);

    internal void SetTitleAlignmentForTests(ChartExTitleAlignment value) =>
        _titleAlignmentCombo.SelectedIndex = _session.FindTitleAlignmentIndex(value);

    internal void SetPlotVisibleOnlyForTests(bool value) => _plotVisibleOnlyCheck.IsChecked = value;

    internal void SetRoundedCornersForTests(bool value) => _roundedCornersCheck.IsChecked = value;

    internal void SetStyleIdForTests(int? styleId) => _styleCombo.SelectedIndex = _session.FindStyleIndex(styleId);

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
        _legendCombo.SelectedIndex = _session.FindLegendIndex(legend);
        _valueLabelsCheck.IsChecked = showValueLabels;
        _percentLabelsCheck.IsChecked = showPercentLabels;
        _categoryLabelsCheck.IsChecked = showCategoryLabels;
        _seriesLabelsCheck.IsChecked = showSeriesLabels;
        _legendKeysCheck.IsChecked = showLegendKeys;
        _bubbleSizeLabelsCheck.IsChecked = showBubbleSize;
        _showLeaderLinesCheck.IsChecked = null;
        _numberFormatBox.Text = numberFormat ?? string.Empty;
        _separatorBox.Text = separator ?? string.Empty;
        _labelPositionCombo.SelectedIndex = _session.FindLabelPositionIndex(labelPosition);
        _categoryGridlinesCheck.IsChecked = categoryGridlines;
        _valueGridlinesCheck.IsChecked = valueGridlines;
        _barGapWidthBox.Text = _session.Format(barGapWidthPercent);
        _barOverlapBox.Text = _session.Format(barOverlapPercent);
        _displayBlanksCombo.SelectedIndex = _session.FindDisplayBlanksIndex(displayBlanksAs);
        _showDataLabelsOverMaximumCheck.IsChecked = showDataLabelsOverMaximum;
        _labelFontFamilyBox.Text = labelFontFamily ?? string.Empty;
        _labelFontSizeBox.Text = _session.Format(labelFontSizePt);
        _labelBoldCheck.IsChecked = labelBold;
        _labelItalicCheck.IsChecked = labelItalic;
        _labelColorBox.Text = labelColor ?? string.Empty;
    }

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (result.ShouldClose)
            Close(true);
        else
            Close(false);
    }

    private ChartDisplayOptionsDialogInput ReadInput() => new(
        _titleBox.Text,
        _titleOverlayCheck.IsChecked == true,
        _titlePositionCombo.SelectedIndex,
        _titleAlignmentCombo.SelectedIndex,
        _plotVisibleOnlyCheck.IsChecked == true,
        _roundedCornersCheck.IsChecked == true,
        _styleCombo.SelectedIndex,
        _legendCombo.SelectedIndex,
        _valueLabelsCheck.IsChecked == true,
        _percentLabelsCheck.IsChecked == true,
        _categoryLabelsCheck.IsChecked == true,
        _seriesLabelsCheck.IsChecked == true,
        _legendKeysCheck.IsChecked == true,
        _bubbleSizeLabelsCheck.IsChecked == true,
        _showLeaderLinesCheck.IsChecked,
        _numberFormatBox.Text,
        _separatorBox.Text,
        _labelFontFamilyBox.Text,
        _labelFontSizeBox.Text,
        _labelBoldCheck.IsChecked,
        _labelItalicCheck.IsChecked,
        _labelColorBox.Text,
        _labelPositionCombo.SelectedIndex,
        _categoryGridlinesCheck.IsChecked == true,
        _valueGridlinesCheck.IsChecked == true,
        _barGapWidthBox.Text,
        _barOverlapBox.Text,
        _displayBlanksCombo.SelectedIndex,
        _showDataLabelsOverMaximumCheck.IsChecked,
        _varyColorsCheck.IsChecked == true,
        _legendOverlayCheck.IsChecked,
        _highLowLinesCheck.IsChecked,
        _waterfallConnectorLinesCheck.IsChecked,
        _dropLinesCheck.IsChecked,
        _upDownBarsCheck.IsChecked,
        _seriesLinesCheck.IsChecked);
}
