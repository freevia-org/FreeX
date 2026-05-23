using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

public sealed record ChartSeriesFormatDialogResult(
    int SeriesIndex,
    CellColor? FillColor,
    CellColor? StrokeColor,
    double? StrokeThickness,
    ChartLineDashStyle? DashStyle,
    ChartMarkerStyle? MarkerStyle,
    double? MarkerSize)
{
    public ChartLayoutOptions ToOptions(IReadOnlyList<ChartSeriesFormat> currentFormats)
    {
        var formats = currentFormats.ToList();
        var replacement = new ChartSeriesFormat(
            SeriesIndex,
            FillColor,
            StrokeColor,
            StrokeThickness,
            DashStyle,
            MarkerStyle,
            MarkerSize);
        var existingIndex = formats.FindIndex(format => format.SeriesIndex == SeriesIndex);
        if (existingIndex >= 0)
            formats[existingIndex] = replacement;
        else
            formats.Add(replacement);
        return new ChartLayoutOptions(SeriesFormats: formats);
    }
}

public sealed class ChartSeriesFormatDialog : Window
{
    private readonly ComboBox _seriesBox = new();
    private readonly ComboBox _dashBox = new();
    private readonly ComboBox _markerBox = new();
    private readonly TextBox _fillBox = new();
    private readonly TextBox _strokeBox = new();
    private readonly TextBox _strokeThicknessBox = new();
    private readonly TextBox _markerSizeBox = new();

    public ChartSeriesFormatDialogResult Result { get; private set; }

    public ChartSeriesFormatDialog(ChartModel chart, int seriesCount)
    {
        Result = FromChart(chart, seriesCount);
        Title = "Format Data Series";
        Width = 380;
        Height = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent(seriesCount);
        Load(Result);
    }

    public static ChartSeriesFormatDialogResult FromChart(ChartModel chart, int seriesCount)
    {
        var seriesIndex = Math.Clamp(chart.SeriesFormats.FirstOrDefault()?.SeriesIndex ?? 0, 0, Math.Max(0, seriesCount - 1));
        var format = chart.SeriesFormats.FirstOrDefault(item => item.SeriesIndex == seriesIndex) ?? new ChartSeriesFormat(seriesIndex);
        return CreateResult(seriesIndex, format.FillColor, format.StrokeColor, format.StrokeThickness, format.DashStyle, format.MarkerStyle, format.MarkerSize);
    }

    public static ChartSeriesFormatDialogResult CreateResult(
        int seriesIndex,
        CellColor? fillColor,
        CellColor? strokeColor,
        double? strokeThickness,
        ChartLineDashStyle? dashStyle,
        ChartMarkerStyle? markerStyle,
        double? markerSize) =>
        new(Math.Max(0, seriesIndex), fillColor, strokeColor, strokeThickness, dashStyle, markerStyle, markerSize);

    private StackPanel CreateContent(int seriesCount)
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCombo(stack, "Series", _seriesBox, Enumerable.Range(0, Math.Max(1, seriesCount)).Select(index => $"Series {index + 1}").ToArray());
            stack.Children.Add(CreateInlineHelp("Choose the series to format without changing the chart data."));
            root.Children.Add(CreateGroupBox("Series Options", stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddColorText(stack, "Fill color", _fillBox);
            ChartDialogHelpers.AddColorText(stack, "Line color", _strokeBox);
            ChartDialogHelpers.AddNumericText(stack, "Line width", _strokeThicknessBox, "Blank keeps the automatic line width.");
            ChartDialogHelpers.AddCombo(stack, "Dash style", _dashBox, Enum.GetValues<ChartLineDashStyle>().Cast<object>().Prepend("(none)").ToArray());
            ChartDialogHelpers.AddCombo(stack, "Marker", _markerBox, Enum.GetValues<ChartMarkerStyle>().Cast<object>().Prepend("(none)").ToArray());
            ChartDialogHelpers.AddNumericText(stack, "Marker size", _markerSizeBox, "Blank keeps the automatic marker size.");
            root.Children.Add(CreateGroupBox("Fill & Line", stack));
        }
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartSeriesFormatDialogResult result)
    {
        _seriesBox.SelectedIndex = Math.Min(result.SeriesIndex, Math.Max(0, _seriesBox.Items.Count - 1));
        _fillBox.Text = ChartDialogHelpers.FormatColor(result.FillColor);
        _strokeBox.Text = ChartDialogHelpers.FormatColor(result.StrokeColor);
        _strokeThicknessBox.Text = ChartDialogHelpers.FormatNullable(result.StrokeThickness);
        _dashBox.SelectedItem = result.DashStyle is null ? "(none)" : result.DashStyle.Value;
        _markerBox.SelectedItem = result.MarkerStyle is null ? "(none)" : result.MarkerStyle.Value;
        _markerSizeBox.Text = ChartDialogHelpers.FormatNullable(result.MarkerSize);
    }

    private void Accept()
    {
        Result = CreateResult(
            _seriesBox.SelectedIndex < 0 ? 0 : _seriesBox.SelectedIndex,
            ChartDialogHelpers.ParseColor(_fillBox.Text),
            ChartDialogHelpers.ParseColor(_strokeBox.Text),
            ChartDialogHelpers.ParseNullableDouble(_strokeThicknessBox.Text),
            _dashBox.SelectedItem is ChartLineDashStyle dash ? dash : null,
            _markerBox.SelectedItem is ChartMarkerStyle marker ? marker : null,
            ChartDialogHelpers.ParseNullableDouble(_markerSizeBox.Text));
        DialogResult = true;
    }
}
