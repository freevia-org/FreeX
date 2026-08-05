using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Small PowerPoint-style chart display/options dialog.</summary>
public sealed class ChartDisplayOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartDisplayOptionsDialogSession _session;
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
        _session = new ChartDisplayOptionsDialogSession(editor);
        var state = _session.State;
        var surface = _session.Surface;

        Title = surface.Title;
        Width = ChartDisplayOptionsPlanner.DefaultDialogWidth;
        Height = ChartDisplayOptionsPlanner.DefaultDialogHeight + 40;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _titleBox = new TextBox { Text = state.Title, MinWidth = 240 };
        _titleOverlayCheck = new CheckBox
        {
            Content = surface.TitleOverlayLabel,
            IsChecked = state.TitleOverlay,
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
            MinWidth = 160,
            SelectedIndex = state.StyleIndex,
        };
        _legendCombo = new ComboBox
        {
            ItemsSource = _session.LegendOptions,
            MinWidth = 160,
            SelectedIndex = state.LegendIndex,
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
        _numberFormatBox = new TextBox { Text = state.LabelNumberFormat, MinWidth = 160 };
        _separatorBox = new TextBox { Text = state.LabelSeparator, MinWidth = 160 };
        _labelFontFamilyBox = new TextBox { Text = state.LabelFontFamily, MinWidth = 160 };
        _labelFontSizeBox = new TextBox { Text = state.LabelFontSizeText, MinWidth = 120 };
        _labelBoldCheck = new CheckBox { Content = surface.BoldLabel, IsThreeState = true, IsChecked = state.LabelBold };
        _labelItalicCheck = new CheckBox { Content = surface.ItalicLabel, IsThreeState = true, IsChecked = state.LabelItalic };
        _labelColorBox = new TextBox { Text = state.LabelColor, MinWidth = 160 };
        _labelPositionCombo = new ComboBox
        {
            ItemsSource = _session.LabelPositionOptions,
            MinWidth = 160,
            SelectedIndex = state.LabelPositionIndex,
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
        _barGapWidthBox = new TextBox { Text = state.BarGapWidthText, MinWidth = 160 };
        _barOverlapBox = new TextBox { Text = state.BarOverlapText, MinWidth = 160 };
        _displayBlanksCombo = new ComboBox
        {
            ItemsSource = _session.DisplayBlanksOptions,
            MinWidth = 160,
            SelectedIndex = state.DisplayBlanksIndex,
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

    internal ChartDisplayOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetVaryColorsForTests(bool value) => _varyColorsCheck.IsChecked = value;

    internal void SetTitleOverlayForTests(bool value) => _titleOverlayCheck.IsChecked = value;

    internal void SetPlotVisibleOnlyForTests(bool value) => _plotVisibleOnlyCheck.IsChecked = value;

    internal void SetRoundedCornersForTests(bool value) => _roundedCornersCheck.IsChecked = value;

    internal void SetStyleIdForTests(int? styleId) => _styleCombo.SelectedIndex = _session.FindStyleIndex(styleId);

    internal void SetLegendOverlayForTests(bool? value) => _legendOverlayCheck.IsChecked = value;

    internal void SetHighLowLinesForTests(bool? value) => _highLowLinesCheck.IsChecked = value;

    internal void SetWaterfallConnectorLinesForTests(bool? value) => _waterfallConnectorLinesCheck.IsChecked = value;

    internal void SetDropLinesForTests(bool? value) => _dropLinesCheck.IsChecked = value;

    internal void SetUpDownBarsForTests(bool? value) => _upDownBarsCheck.IsChecked = value;

    internal void SetSeriesLinesForTests(bool? value) => _seriesLinesCheck.IsChecked = value;

    internal void SetLabelTextStyleForTests(string? family, double? sizePt, bool? bold, bool? italic, string? color)
    {
        _labelFontFamilyBox.Text = family ?? string.Empty;
        _labelFontSizeBox.Text = _session.Format(sizePt);
        _labelBoldCheck.IsChecked = bold;
        _labelItalicCheck.IsChecked = italic;
        _labelColorBox.Text = color ?? string.Empty;
    }

    internal void SetBubbleSizeLabelsForTests(bool value) => _bubbleSizeLabelsCheck.IsChecked = value;

    internal void SetLeaderLinesForTests(bool? value) => _showLeaderLinesCheck.IsChecked = value;

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (!result.ShouldClose)
        {
            MessageBox.Show(this, result.ValidationMessage, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private ChartDisplayOptionsDialogInput ReadInput() => new(
        _titleBox.Text,
        _titleOverlayCheck.IsChecked == true,
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
