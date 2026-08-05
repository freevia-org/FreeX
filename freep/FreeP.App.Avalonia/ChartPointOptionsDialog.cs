using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartPointOptionsDialog : Window
{
    private readonly ChartPointOptionsDialogSession _session;
    private readonly ComboBox _seriesCombo;
    private readonly ComboBox _pointCombo;
    private readonly TextBox _fillColorBox;
    private readonly TextBox _strokeColorBox;
    private readonly TextBox _strokeWidthBox;
    private readonly CheckBox _usePointDataLabelsCheck;
    private readonly CheckBox _showValueLabelsCheck;
    private readonly CheckBox _showPercentLabelsCheck;
    private readonly CheckBox _showCategoryLabelsCheck;
    private readonly CheckBox _showSeriesLabelsCheck;
    private readonly CheckBox _showLegendKeysCheck;
    private readonly CheckBox _showBubbleSizeCheck;
    private readonly CheckBox _showLeaderLinesCheck;
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
    private readonly TextBox _explosionBox;

    internal ChartPointOptionsDialog(
        EditingSession editor,
        int? initialSeriesIndex = null,
        int? initialPointIndex = null)
    {
        _session = new ChartPointOptionsDialogSession(
            editor,
            initialSeriesIndex,
            initialPointIndex);
        var surface = _session.Surface;
        var state = _session.State;

        Title = surface.Title;
        Width = ChartPointOptionsPlanner.DefaultDialogWidth;
        Height = ChartPointOptionsPlanner.DefaultDialogHeight;
        MinWidth = 400;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _seriesCombo = new ComboBox
        {
            ItemsSource = state.SeriesOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = state.SeriesIndex,
            MinWidth = 220,
        };
        _seriesCombo.SelectionChanged += (_, _) =>
        {
            var selectedState = _session.SelectSeries(_seriesCombo.SelectedIndex);
            RefreshPoints(selectedState);
            LoadControls(selectedState);
        };
        _pointCombo = new ComboBox { MinWidth = 220 };
        _pointCombo.SelectionChanged += (_, _) =>
        {
            LoadControls(_session.SelectPoint(_pointCombo.SelectedIndex));
        };
        _fillColorBox = new TextBox { MinWidth = 150 };
        _strokeColorBox = new TextBox { MinWidth = 150 };
        _strokeWidthBox = new TextBox { MinWidth = 120 };
        _usePointDataLabelsCheck = new CheckBox { Content = surface.PointDataLabelsLabel };
        _showValueLabelsCheck = new CheckBox { Content = surface.ValueLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showPercentLabelsCheck = new CheckBox { Content = surface.PercentLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showCategoryLabelsCheck = new CheckBox { Content = surface.CategoryLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showSeriesLabelsCheck = new CheckBox { Content = surface.SeriesLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showLegendKeysCheck = new CheckBox { Content = surface.LegendKeysLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showBubbleSizeCheck = new CheckBox { Content = surface.BubbleSizeLabelsLabel, Margin = new Thickness(20, 0, 0, 0) };
        _showLeaderLinesCheck = new CheckBox
        {
            Content = surface.LeaderLinesLabel,
            Margin = new Thickness(20, 0, 0, 0),
            IsThreeState = true,
        };
        _labelPositionCombo = new ComboBox
        {
            ItemsSource = _session.LabelPositionOptions.Select(option => option.Label).ToArray(),
            MinWidth = 160,
        };
        _labelNumberFormatBox = new TextBox { MinWidth = 150 };
        _labelSeparatorBox = new TextBox { MinWidth = 150 };
        _labelFontFamilyBox = new TextBox { MinWidth = 150 };
        _labelFontSizeBox = new TextBox { MinWidth = 120 };
        _labelBoldCheck = new CheckBox { Content = surface.BoldLabel, IsThreeState = true };
        _labelItalicCheck = new CheckBox { Content = surface.ItalicLabel, IsThreeState = true };
        _labelColorBox = new TextBox { MinWidth = 150 };
        _markerCombo = new ComboBox
        {
            ItemsSource = _session.MarkerOptions.Select(option => option.Label).ToArray(),
            MinWidth = 160,
        };
        _markerSizeBox = new TextBox { MinWidth = 120 };
        _explosionBox = new TextBox { MinWidth = 120 };
        RefreshPoints(state);
        LoadControls(state);

        var buttons = ChartOptionsDialogChrome.CreateActionRow(surface.OkLabel, OnOk, surface.CancelLabel, () => Close(false));

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                ChartOptionsDialogChrome.CreateRow(surface.SeriesLabel, _seriesCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.PointLabel, _pointCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.FillColorLabel, _fillColorBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.StrokeColorLabel, _strokeColorBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.StrokeWidthLabel, _strokeWidthBox, 180),
                _usePointDataLabelsCheck,
                _showValueLabelsCheck,
                _showPercentLabelsCheck,
                _showCategoryLabelsCheck,
                _showSeriesLabelsCheck,
                _showLegendKeysCheck,
                _showBubbleSizeCheck,
                _showLeaderLinesCheck,
                ChartOptionsDialogChrome.CreateRow(surface.LabelPositionLabel, _labelPositionCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.NumberFormatLabel, _labelNumberFormatBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.SeparatorLabel, _labelSeparatorBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.FontFamilyLabel, _labelFontFamilyBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.FontSizeLabel, _labelFontSizeBox, 180),
                _labelBoldCheck,
                _labelItalicCheck,
                ChartOptionsDialogChrome.CreateRow(surface.LabelColorLabel, _labelColorBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.MarkerLabel, _markerCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.MarkerSizeLabel, _markerSizeBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.ExplosionLabel, _explosionBox, 180),
                new TextBlock { Text = surface.AutoHint, Opacity = 0.7 },
                buttons,
            },
        };
    }

    internal ChartPointOptions BuildCommitPlanForTests()
    {
        return _session.BuildCommitPlan(ReadInput());
    }

    internal void SetOptionsForTests(
        int seriesIndex,
        int pointIndex,
        string? fillColor,
        string? strokeColor,
        double? strokeWidthPt,
        ChartMarkerSymbol? markerSymbol,
        double? markerSizePt,
        bool usePointDataLabels = false,
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
        int? explosionPercent = null,
        bool? showLeaderLines = null)
    {
        _seriesCombo.SelectedIndex = seriesIndex;
        RefreshPoints(_session.State);
        _pointCombo.SelectedIndex = pointIndex;
        _fillColorBox.Text = fillColor ?? string.Empty;
        _strokeColorBox.Text = strokeColor ?? string.Empty;
        _strokeWidthBox.Text = _session.Format(strokeWidthPt);
        _usePointDataLabelsCheck.IsChecked = usePointDataLabels;
        _showValueLabelsCheck.IsChecked = showValueLabels;
        _showPercentLabelsCheck.IsChecked = showPercentLabels;
        _showCategoryLabelsCheck.IsChecked = showCategoryLabels;
        _showSeriesLabelsCheck.IsChecked = showSeriesLabels;
        _showLegendKeysCheck.IsChecked = showLegendKeys;
        _showBubbleSizeCheck.IsChecked = showBubbleSize;
        _showLeaderLinesCheck.IsChecked = showLeaderLines;
        _labelPositionCombo.SelectedIndex = _session.FindLabelPositionIndex(labelPosition);
        _labelNumberFormatBox.Text = labelNumberFormat ?? string.Empty;
        _labelSeparatorBox.Text = labelSeparator ?? string.Empty;
        _labelFontFamilyBox.Text = labelFontFamily ?? string.Empty;
        _labelFontSizeBox.Text = _session.Format(labelFontSizePt);
        _labelBoldCheck.IsChecked = labelBold;
        _labelItalicCheck.IsChecked = labelItalic;
        _labelColorBox.Text = labelColor ?? string.Empty;
        _markerCombo.SelectedIndex = _session.FindMarkerIndex(markerSymbol);
        _markerSizeBox.Text = _session.Format(markerSizePt);
        _explosionBox.Text = _session.Format(explosionPercent);
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

    private void RefreshPoints(ChartPointOptionsDialogState state)
    {
        _pointCombo.ItemsSource = state.PointOptions.Select(option => option.Label).ToArray();
        _pointCombo.SelectedIndex = Math.Min(state.PointIndex, Math.Max(0, state.PointOptions.Count - 1));
    }

    private void LoadControls(ChartPointOptionsDialogState state)
    {
        _fillColorBox.Text = state.FillColorText;
        _strokeColorBox.Text = state.StrokeColorText;
        _strokeWidthBox.Text = state.StrokeWidthText;
        _usePointDataLabelsCheck.IsChecked = state.UsePointDataLabels;
        _showValueLabelsCheck.IsChecked = state.ShowValueLabels;
        _showPercentLabelsCheck.IsChecked = state.ShowPercentLabels;
        _showCategoryLabelsCheck.IsChecked = state.ShowCategoryLabels;
        _showSeriesLabelsCheck.IsChecked = state.ShowSeriesLabels;
        _showLegendKeysCheck.IsChecked = state.ShowLegendKeys;
        _showBubbleSizeCheck.IsChecked = state.ShowBubbleSize;
        _showLeaderLinesCheck.IsChecked = state.ShowLeaderLines;
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
        _explosionBox.Text = state.ExplosionText;
    }

    private ChartPointOptionsDialogInput ReadInput() => new(
        _seriesCombo.SelectedIndex,
        _pointCombo.SelectedIndex,
        _fillColorBox.Text,
        _strokeColorBox.Text,
        _strokeWidthBox.Text,
        _usePointDataLabelsCheck.IsChecked == true,
        _showValueLabelsCheck.IsChecked == true,
        _showPercentLabelsCheck.IsChecked == true,
        _showCategoryLabelsCheck.IsChecked == true,
        _showSeriesLabelsCheck.IsChecked == true,
        _showLegendKeysCheck.IsChecked == true,
        _showBubbleSizeCheck.IsChecked == true,
        _showLeaderLinesCheck.IsChecked,
        _labelPositionCombo.SelectedIndex,
        _labelNumberFormatBox.Text,
        _labelSeparatorBox.Text,
        _labelFontFamilyBox.Text,
        _labelFontSizeBox.Text,
        _labelBoldCheck.IsChecked,
        _labelItalicCheck.IsChecked,
        _labelColorBox.Text,
        _markerCombo.SelectedIndex,
        _markerSizeBox.Text,
        _explosionBox.Text);
}
