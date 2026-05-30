using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogInputParser;
using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

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
        Title = UiText.Get("ChartSeriesFormat_Title");
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
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartSeriesFormat_SeriesLabel"), _seriesBox, Enumerable.Range(0, Math.Max(1, seriesCount)).Select(index => UiText.Format("SelectDataSource_SeriesNameFormat", index + 1)).ToArray());
            stack.Children.Add(CreateInlineHelp(UiText.Get("ChartSeriesFormat_SeriesHelpText")));
            root.Children.Add(CreateGroupBox(UiText.Get("ChartSeriesFormat_SeriesOptionsGroup"), stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartSeriesFormat_FillColorLabel"), _fillBox);
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartSeriesFormat_LineColorLabel"), _strokeBox);
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartSeriesFormat_LineWidthLabel"), _strokeThicknessBox, UiText.Get("ChartSeriesFormat_LineWidthHelpText"));
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartSeriesFormat_DashStyleLabel"), _dashBox, Enum.GetValues<ChartLineDashStyle>().Cast<object>().Prepend(UiText.Get("Common_NoneParenthetical")).ToArray());
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartSeriesFormat_MarkerLabel"), _markerBox, Enum.GetValues<ChartMarkerStyle>().Cast<object>().Prepend(UiText.Get("Common_NoneParenthetical")).ToArray());
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartSeriesFormat_MarkerSizeLabel"), _markerSizeBox, UiText.Get("ChartSeriesFormat_MarkerSizeHelpText"));
            root.Children.Add(CreateGroupBox(UiText.Get("ChartDialog_FillLineGroup"), stack));
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
        _dashBox.SelectedItem = result.DashStyle is null ? UiText.Get("Common_NoneParenthetical") : result.DashStyle.Value;
        _markerBox.SelectedItem = result.MarkerStyle is null ? UiText.Get("Common_NoneParenthetical") : result.MarkerStyle.Value;
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
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _fillBox);
            return;
        }

        if (!TryReadOptionalColor(_strokeBox, out var strokeColor))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _strokeBox);
            return;
        }

        if (!TryReadNullablePositiveDouble(_strokeThicknessBox, out var strokeThickness))
        {
            ShowInvalidInputWarning(UiText.Get("ChartSeriesFormat_InvalidLineWidthMessage"), _strokeThicknessBox);
            return;
        }

        if (!TryReadNullablePositiveDouble(_markerSizeBox, out var markerSize))
        {
            ShowInvalidInputWarning(UiText.Get("ChartSeriesFormat_InvalidMarkerSizeMessage"), _markerSizeBox);
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

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
        return true;
    }
}
