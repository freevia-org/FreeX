using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        Loaded += (_, _) => FocusInitialKeyboardTarget();
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
            ChartDialogHelpers.AddCombo(stack, "_Series", _seriesBox, Enumerable.Range(0, Math.Max(1, seriesCount)).Select(index => $"Series {index + 1}").ToArray());
            stack.Children.Add(CreateInlineHelp("Choose the series to format without changing the chart data."));
            root.Children.Add(CreateGroupBox("Series Options", stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddColorText(stack, "_Fill color", _fillBox);
            ChartDialogHelpers.AddColorText(stack, "_Line color", _strokeBox);
            ChartDialogHelpers.AddNumericText(stack, "Line _width", _strokeThicknessBox, "Blank keeps the automatic line width.");
            ChartDialogHelpers.AddCombo(stack, "_Dash style", _dashBox, Enum.GetValues<ChartLineDashStyle>().Cast<object>().Prepend("(none)").ToArray());
            ChartDialogHelpers.AddCombo(stack, "_Marker", _markerBox, Enum.GetValues<ChartMarkerStyle>().Cast<object>().Prepend("(none)").ToArray());
            ChartDialogHelpers.AddNumericText(stack, "Marker _size", _markerSizeBox, "Blank keeps the automatic marker size.");
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

    private void FocusInitialKeyboardTarget()
    {
        _seriesBox.Focus();
        Keyboard.Focus(_seriesBox);
    }

    private void Accept()
    {
        if (!TryReadOptionalColor(_fillBox, out var fillColor))
        {
            ShowInvalidInputWarning("Enter a color as #RRGGBB or none.", _fillBox);
            return;
        }

        if (!TryReadOptionalColor(_strokeBox, out var strokeColor))
        {
            ShowInvalidInputWarning("Enter a color as #RRGGBB or none.", _strokeBox);
            return;
        }

        if (!TryReadNullablePositiveDouble(_strokeThicknessBox, out var strokeThickness))
        {
            ShowInvalidInputWarning("Enter a positive line width or leave it blank.", _strokeThicknessBox);
            return;
        }

        if (!TryReadNullablePositiveDouble(_markerSizeBox, out var markerSize))
        {
            ShowInvalidInputWarning("Enter a positive marker size or leave it blank.", _markerSizeBox);
            return;
        }

        Result = CreateResult(
            _seriesBox.SelectedIndex < 0 ? 0 : _seriesBox.SelectedIndex,
            fillColor,
            strokeColor,
            strokeThickness,
            _dashBox.SelectedItem is ChartLineDashStyle dash ? dash : null,
            _markerBox.SelectedItem is ChartMarkerStyle marker ? marker : null,
            markerSize);
        DialogResult = true;
    }

    private static bool TryReadOptionalColor(TextBox textBox, out CellColor? color) =>
        ColorInputParser.TryParseOptionalHexColor(textBox.Text, out color);

    private static bool TryReadNullablePositiveDouble(TextBox textBox, out double? value)
    {
        value = null;
        var text = textBox.Text.Trim();
        if (string.IsNullOrEmpty(text) || text.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
            || !double.IsFinite(parsed)
            || parsed <= 0)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        MessageBox.Show(
            this,
            message,
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
        return true;
    }
}
