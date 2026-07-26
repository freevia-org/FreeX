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
    private readonly ComboBox _legendCombo;
    private readonly CheckBox _valueLabelsCheck;
    private readonly CheckBox _percentLabelsCheck;
    private readonly CheckBox _categoryLabelsCheck;
    private readonly CheckBox _seriesLabelsCheck;
    private readonly CheckBox _legendKeysCheck;
    private readonly TextBox _numberFormatBox;
    private readonly TextBox _separatorBox;
    private readonly ComboBox _labelPositionCombo;
    private readonly CheckBox _categoryGridlinesCheck;
    private readonly CheckBox _valueGridlinesCheck;
    private readonly TextBox _barGapWidthBox;
    private readonly TextBox _barOverlapBox;
    private readonly ComboBox _displayBlanksCombo;
    private readonly CheckBox _showDataLabelsOverMaximumCheck;
    private readonly CheckBox _varyColorsCheck;

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
        _numberFormatBox = new TextBox { Text = _planner.LabelNumberFormat, MinWidth = 160 };
        _separatorBox = new TextBox { Text = _planner.LabelSeparator, MinWidth = 160 };
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
        content.Children.Add(MakeRow(surface.LegendLabel, _legendCombo));
        content.Children.Add(MakeRow(surface.LabelPositionLabel, _labelPositionCombo));
        content.Children.Add(_valueLabelsCheck);
        content.Children.Add(_percentLabelsCheck);
        content.Children.Add(_categoryLabelsCheck);
        content.Children.Add(_seriesLabelsCheck);
        content.Children.Add(_legendKeysCheck);
        content.Children.Add(MakeRow(surface.NumberFormatLabel, _numberFormatBox));
        content.Children.Add(MakeRow(surface.SeparatorLabel, _separatorBox));
        content.Children.Add(_categoryGridlinesCheck);
        content.Children.Add(_valueGridlinesCheck);
        content.Children.Add(MakeRow(surface.BarGapWidthLabel, _barGapWidthBox));
        content.Children.Add(MakeRow(surface.BarOverlapLabel, _barOverlapBox));
        content.Children.Add(MakeRow(surface.DisplayBlanksAsLabel, _displayBlanksCombo));
        content.Children.Add(_showDataLabelsOverMaximumCheck);
        content.Children.Add(_varyColorsCheck);
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

    private void OnOk()
    {
        _editor.ApplyChartDisplayOptions(BuildCommitPlanForTests());
        DialogResult = true;
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetTitle(_titleBox.Text);
        _planner.SetLegend(_legendCombo.SelectedItem is ChartDisplayLegendOption legend ? legend.Value : null);
        _planner.SetShowValueLabels(_valueLabelsCheck.IsChecked == true);
        _planner.SetShowPercentLabels(_percentLabelsCheck.IsChecked == true);
        _planner.SetShowCategoryLabels(_categoryLabelsCheck.IsChecked == true);
        _planner.SetShowSeriesLabels(_seriesLabelsCheck.IsChecked == true);
        _planner.SetShowLegendKeys(_legendKeysCheck.IsChecked == true);
        if (_labelPositionCombo.SelectedItem is ChartDisplayLabelPositionOption position)
            _planner.SetLabelPosition(position.Value);
        _planner.SetLabelNumberFormat(_numberFormatBox.Text);
        _planner.SetLabelSeparator(_separatorBox.Text);
        _planner.SetCategoryGridlines(_categoryGridlinesCheck.IsChecked == true);
        _planner.SetValueGridlines(_valueGridlinesCheck.IsChecked == true);
        _planner.SetBarGapWidthPercent(ParseOptionalPercent(_barGapWidthBox.Text, "Bar gap width", 0, 500));
        _planner.SetBarOverlapPercent(ParseOptionalPercent(_barOverlapBox.Text, "Bar overlap", -100, 100));
        if (_displayBlanksCombo.SelectedItem is ChartDisplayBlanksOption blanks)
            _planner.SetDisplayBlanksAs(blanks.Value);
        _planner.SetShowDataLabelsOverMaximum(_showDataLabelsOverMaximumCheck.IsChecked);
        _planner.SetVaryColors(_varyColorsCheck.IsChecked == true);
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

    private static int FindLabelPositionIndex(DataLabelPosition position) =>
        Math.Max(0, ChartDisplayOptionsPlanner.LabelPositionOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == position).index);

    private static int FindDisplayBlanksIndex(ChartDisplayBlanksAs? value) =>
        ChartDisplayOptionsPlanner.DisplayBlanksOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index;

    private static string Format(int? value) => value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

    private static int? ParseOptionalPercent(string? text, string surface, int minimum, int maximum)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value >= minimum && value <= maximum)
            return value;
        throw new FormatException($"{surface} must be a whole number from {minimum} to {maximum}, or blank.");
    }
}
