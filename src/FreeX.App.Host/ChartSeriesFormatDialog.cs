using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

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
    public ChartSeriesFormatInput ToInput() =>
        new(SeriesIndex, FillColor, StrokeColor, StrokeThickness, MarkerStyle, MarkerSize, DashStyle);

    public ChartLayoutOptions ToOptions(ChartModel chart) => ChartSeriesFormatPlanner.Plan(chart, ToInput());
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
        Result = FromChart(chart);
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

    public static ChartSeriesFormatDialogResult FromChart(ChartModel chart)
    {
        var input = ChartSeriesFormatPlanner.ReadDefault(chart);
        return CreateResult(input.SeriesIndex, input.FillColor, input.StrokeColor, input.StrokeThickness, input.DashStyle, input.MarkerStyle, input.MarkerSize);
    }

    public static ChartSeriesFormatDialogResult CreateResult(
        int seriesIndex,
        CellColor? fillColor,
        CellColor? strokeColor,
        double? strokeThickness,
        ChartLineDashStyle? dashStyle,
        ChartMarkerStyle? markerStyle,
        double? markerSize)
    {
        var input = ChartSeriesFormatPlanner.Normalize(new ChartSeriesFormatInput(
            seriesIndex,
            fillColor,
            strokeColor,
            strokeThickness,
            markerStyle,
            markerSize,
            dashStyle));
        return new(
            input.SeriesIndex,
            input.FillColor,
            input.StrokeColor,
            input.StrokeThickness,
            input.DashStyle,
            input.MarkerStyle,
            input.MarkerSize);
    }

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
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartSeriesFormat_DashStyleLabel"), _dashBox, ChartSeriesFormatPlanner.GetDashStyleChoices().Cast<object>().Prepend(UiText.Get("Common_NoneParenthetical")).ToArray());
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartSeriesFormat_MarkerLabel"), _markerBox, ChartSeriesFormatPlanner.GetMarkerStyleChoices().Cast<object>().Prepend(UiText.Get("Common_NoneParenthetical")).ToArray());
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
        if (!ChartSeriesFormatPlanner.TryParseDialogInput(
                _seriesBox.SelectedIndex < 0 ? 0 : _seriesBox.SelectedIndex,
                _fillBox.Text,
                _strokeBox.Text,
                _strokeThicknessBox.Text,
                SelectedDashStyle(),
                SelectedMarkerStyle(),
                _markerSizeBox.Text,
                out var input,
                out var issue))
        {
            ShowPlannerParseWarning(issue);
            return;
        }

        Result = CreateResult(
            input.SeriesIndex,
            input.FillColor,
            input.StrokeColor,
            input.StrokeThickness,
            input.DashStyle,
            input.MarkerStyle,
            input.MarkerSize);
        DialogResult = true;
    }

    private ChartLineDashStyle? SelectedDashStyle() =>
        _dashBox.SelectedItem is ChartLineDashStyle value ? value : null;

    private ChartMarkerStyle? SelectedMarkerStyle() =>
        _markerBox.SelectedItem is ChartMarkerStyle value ? value : null;

    private void ShowPlannerParseWarning(ChartSeriesFormatParseIssue issue)
    {
        var (message, target) = issue switch
        {
            ChartSeriesFormatParseIssue.StrokeColor => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _strokeBox),
            ChartSeriesFormatParseIssue.StrokeThickness => (UiText.Get("ChartSeriesFormat_InvalidLineWidthMessage"), _strokeThicknessBox),
            ChartSeriesFormatParseIssue.MarkerSize => (UiText.Get("ChartSeriesFormat_InvalidMarkerSizeMessage"), _markerSizeBox),
            _ => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _fillBox)
        };
        ShowInvalidInputWarning(message, target);
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
