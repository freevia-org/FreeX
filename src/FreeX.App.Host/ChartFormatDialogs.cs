using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

public sealed record ChartAreaLegendDialogResult(
    CellColor? ChartAreaFillColor,
    CellColor? PlotAreaFillColor,
    CellColor? PlotAreaBorderColor,
    double PlotAreaBorderThickness,
    bool ShowLegend,
    ChartLegendPosition LegendPosition,
    bool LegendOverlay,
    CellColor? LegendTextColor,
    CellColor? LegendFillColor,
    CellColor? LegendBorderColor,
    double LegendBorderThickness,
    double LegendFontSize)
{
    public ChartAreaFormatInput ToInput() =>
        new(
            ChartAreaFillColor,
            PlotAreaFillColor,
            PlotAreaBorderColor,
            PlotAreaBorderThickness,
            ShowLegend,
            LegendPosition,
            LegendOverlay,
            LegendTextColor,
            LegendFillColor,
            LegendBorderColor,
            LegendBorderThickness,
            LegendFontSize);

    public ChartLayoutOptions ToOptions() => ChartAreaFormatPlanner.Plan(ToInput());
}

public sealed class ChartAreaLegendDialog : Window
{
    private readonly TextBox _chartAreaFillBox = new();
    private readonly TextBox _plotAreaFillBox = new();
    private readonly TextBox _plotAreaBorderBox = new();
    private readonly TextBox _plotAreaBorderThicknessBox = new();
    private readonly CheckBox _showLegendBox = new() { Content = UiText.Get("ChartAreaLegend_ShowLegend") };
    private readonly ComboBox _legendPositionBox = new();
    private readonly CheckBox _legendOverlayBox = new() { Content = UiText.Get("ChartAreaLegend_OverlayLegend") };
    private readonly TextBox _legendTextBox = new();
    private readonly TextBox _legendFillBox = new();
    private readonly TextBox _legendBorderBox = new();
    private readonly TextBox _legendBorderThicknessBox = new();
    private readonly TextBox _legendFontSizeBox = new();

    public ChartAreaLegendDialogResult Result { get; private set; }

    public ChartAreaLegendDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = UiText.Get("ChartAreaLegend_Title");
        Width = 420;
        Height = 590;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static ChartAreaLegendDialogResult FromChart(ChartModel chart)
    {
        var input = ChartAreaFormatPlanner.Read(chart);
        return CreateResult(
            input.ChartAreaFillColor,
            input.PlotAreaFillColor,
            input.PlotAreaBorderColor,
            input.PlotAreaBorderThickness,
            input.ShowLegend,
            input.LegendPosition,
            input.LegendOverlay,
            input.LegendTextColor,
            input.LegendFillColor,
            input.LegendBorderColor,
            input.LegendBorderThickness,
            input.LegendFontSize);
    }

    public static ChartAreaLegendDialogResult CreateResult(
        CellColor? chartAreaFillColor,
        CellColor? plotAreaFillColor,
        CellColor? plotAreaBorderColor,
        double plotAreaBorderThickness,
        bool showLegend,
        ChartLegendPosition legendPosition,
        bool legendOverlay,
        CellColor? legendTextColor,
        CellColor? legendFillColor,
        CellColor? legendBorderColor,
        double legendBorderThickness,
        double legendFontSize)
    {
        var input = ChartAreaFormatPlanner.Normalize(new ChartAreaFormatInput(
            chartAreaFillColor,
            plotAreaFillColor,
            plotAreaBorderColor,
            plotAreaBorderThickness,
            showLegend,
            legendPosition,
            legendOverlay,
            legendTextColor,
            legendFillColor,
            legendBorderColor,
            legendBorderThickness,
            legendFontSize));
        return new(
            input.ChartAreaFillColor,
            input.PlotAreaFillColor,
            input.PlotAreaBorderColor,
            input.PlotAreaBorderThickness,
            input.ShowLegend,
            input.LegendPosition,
            input.LegendOverlay,
            input.LegendTextColor,
            input.LegendFillColor,
            input.LegendBorderColor,
            input.LegendBorderThickness,
            input.LegendFontSize);
    }

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var stack = new StackPanel();
            stack.Children.Add(CreateInlineHelp(UiText.Get("ChartAreaLegend_FillLineHelpText")));
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartAreaLegend_ChartAreaFillColorLabel"), _chartAreaFillBox);
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartAreaLegend_PlotAreaFillColorLabel"), _plotAreaFillBox);
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartAreaLegend_PlotAreaBorderColorLabel"), _plotAreaBorderBox);
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartAreaLegend_PlotAreaBorderWidthLabel"), _plotAreaBorderThicknessBox, UiText.Get("ChartDialog_LineWidthHelpText"));
            root.Children.Add(CreateGroupBox(UiText.Get("ChartDialog_FillLineGroup"), stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _showLegendBox);
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartAreaLegend_LegendPositionLabel"), _legendPositionBox, ChartAreaFormatPlanner.GetLegendPositionChoices());
            ChartDialogHelpers.AddCheck(stack, _legendOverlayBox);
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartAreaLegend_LegendTextColorLabel"), _legendTextBox);
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartAreaLegend_LegendFillColorLabel"), _legendFillBox);
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartAreaLegend_LegendBorderColorLabel"), _legendBorderBox);
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartAreaLegend_LegendBorderWidthLabel"), _legendBorderThicknessBox, UiText.Get("ChartDialog_LineWidthHelpText"));
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartAreaLegend_LegendFontSizeLabel"), _legendFontSizeBox, UiText.Get("ChartAreaLegend_LegendFontSizeHelpText"));
            root.Children.Add(CreateGroupBox(UiText.Get("ChartAreaLegend_LegendGroup"), stack));
        }
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartAreaLegendDialogResult result)
    {
        _chartAreaFillBox.Text = ChartDialogHelpers.FormatColor(result.ChartAreaFillColor);
        _plotAreaFillBox.Text = ChartDialogHelpers.FormatColor(result.PlotAreaFillColor);
        _plotAreaBorderBox.Text = ChartDialogHelpers.FormatColor(result.PlotAreaBorderColor);
        _plotAreaBorderThicknessBox.Text = result.PlotAreaBorderThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _showLegendBox.IsChecked = result.ShowLegend;
        _legendPositionBox.SelectedItem = result.LegendPosition;
        _legendOverlayBox.IsChecked = result.LegendOverlay;
        _legendTextBox.Text = ChartDialogHelpers.FormatColor(result.LegendTextColor);
        _legendFillBox.Text = ChartDialogHelpers.FormatColor(result.LegendFillColor);
        _legendBorderBox.Text = ChartDialogHelpers.FormatColor(result.LegendBorderColor);
        _legendBorderThicknessBox.Text = result.LegendBorderThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _legendFontSizeBox.Text = result.LegendFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void FocusInitialKeyboardTarget()
    {
        _chartAreaFillBox.Focus();
        _chartAreaFillBox.SelectAll();
        Keyboard.Focus(_chartAreaFillBox);
    }

    private void Accept()
    {
        if (!ChartAreaFormatPlanner.TryParseDialogInput(
                _chartAreaFillBox.Text,
                _plotAreaFillBox.Text,
                _plotAreaBorderBox.Text,
                _plotAreaBorderThicknessBox.Text,
                _showLegendBox.IsChecked == true,
                SelectedLegendPosition(),
                _legendOverlayBox.IsChecked == true,
                _legendTextBox.Text,
                _legendFillBox.Text,
                _legendBorderBox.Text,
                _legendBorderThicknessBox.Text,
                _legendFontSizeBox.Text,
                out var input,
                out var issue))
        {
            ShowPlannerParseWarning(issue);
            return;
        }

        Result = CreateResult(
            input.ChartAreaFillColor,
            input.PlotAreaFillColor,
            input.PlotAreaBorderColor,
            input.PlotAreaBorderThickness,
            input.ShowLegend,
            input.LegendPosition,
            input.LegendOverlay,
            input.LegendTextColor,
            input.LegendFillColor,
            input.LegendBorderColor,
            input.LegendBorderThickness,
            input.LegendFontSize);
        DialogResult = true;
    }

    private ChartLegendPosition? SelectedLegendPosition() =>
        _legendPositionBox.SelectedItem is ChartLegendPosition value ? value : null;

    private void ShowPlannerParseWarning(ChartAreaFormatParseIssue issue)
    {
        var (message, target) = issue switch
        {
            ChartAreaFormatParseIssue.PlotAreaFillColor => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _plotAreaFillBox),
            ChartAreaFormatParseIssue.PlotAreaBorderColor => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _plotAreaBorderBox),
            ChartAreaFormatParseIssue.PlotAreaBorderThickness => (UiText.Get("ChartAreaLegend_InvalidPlotAreaBorderWidthMessage"), _plotAreaBorderThicknessBox),
            ChartAreaFormatParseIssue.LegendTextColor => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _legendTextBox),
            ChartAreaFormatParseIssue.LegendFillColor => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _legendFillBox),
            ChartAreaFormatParseIssue.LegendBorderColor => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _legendBorderBox),
            ChartAreaFormatParseIssue.LegendBorderThickness => (UiText.Get("ChartAreaLegend_InvalidLegendBorderWidthMessage"), _legendBorderThicknessBox),
            ChartAreaFormatParseIssue.LegendFontSize => (UiText.Get("ChartAreaLegend_InvalidLegendFontSizeMessage"), _legendFontSizeBox),
            _ => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _chartAreaFillBox)
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

