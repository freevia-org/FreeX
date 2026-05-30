using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using FreeX.Core.Commands;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogInputParser;
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
    public ChartLayoutOptions ToOptions() => new(
        ChartAreaFillColor: ChartAreaFillColor,
        PlotAreaFillColor: PlotAreaFillColor,
        PlotAreaBorderColor: PlotAreaBorderColor,
        PlotAreaBorderThickness: PlotAreaBorderThickness,
        ShowLegend: ShowLegend,
        LegendPosition: LegendPosition,
        LegendOverlay: LegendOverlay,
        LegendTextColor: LegendTextColor,
        LegendFillColor: LegendFillColor,
        LegendBorderColor: LegendBorderColor,
        LegendBorderThickness: LegendBorderThickness,
        LegendFontSize: LegendFontSize);
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

    public static ChartAreaLegendDialogResult FromChart(ChartModel chart) => CreateResult(
        chart.ChartAreaFillColor,
        chart.PlotAreaFillColor,
        chart.PlotAreaBorderColor,
        chart.PlotAreaBorderThickness,
        chart.ShowLegend,
        chart.LegendPosition,
        chart.LegendOverlay,
        chart.LegendTextColor,
        chart.LegendFillColor,
        chart.LegendBorderColor,
        chart.LegendBorderThickness,
        chart.LegendFontSize);

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
        double legendFontSize) =>
        new(
            chartAreaFillColor,
            plotAreaFillColor,
            plotAreaBorderColor,
            Math.Clamp(FiniteOrDefault(plotAreaBorderThickness, 1), 0, 10),
            showLegend,
            Enum.IsDefined(legendPosition) ? legendPosition : ChartLegendPosition.Right,
            legendOverlay,
            legendTextColor,
            legendFillColor,
            legendBorderColor,
            Math.Clamp(FiniteOrDefault(legendBorderThickness, 0), 0, 10),
            Math.Clamp(FiniteOrDefault(legendFontSize, 12), 6, 72));

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
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartAreaLegend_LegendPositionLabel"), _legendPositionBox, Enum.GetValues<ChartLegendPosition>());
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
        if (!TryReadOptionalColor(_chartAreaFillBox, out var chartAreaFillColor))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _chartAreaFillBox);
            return;
        }

        if (!TryReadOptionalColor(_plotAreaFillBox, out var plotAreaFillColor))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _plotAreaFillBox);
            return;
        }

        if (!TryReadOptionalColor(_plotAreaBorderBox, out var plotAreaBorderColor))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _plotAreaBorderBox);
            return;
        }

        if (!TryReadClampedDouble(_plotAreaBorderThicknessBox, min: 0, max: 10, out var plotAreaBorderThickness))
        {
            ShowInvalidInputWarning(UiText.Get("ChartAreaLegend_InvalidPlotAreaBorderWidthMessage"), _plotAreaBorderThicknessBox);
            return;
        }

        if (!TryReadOptionalColor(_legendTextBox, out var legendTextColor))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _legendTextBox);
            return;
        }

        if (!TryReadOptionalColor(_legendFillBox, out var legendFillColor))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _legendFillBox);
            return;
        }

        if (!TryReadOptionalColor(_legendBorderBox, out var legendBorderColor))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _legendBorderBox);
            return;
        }

        if (!TryReadClampedDouble(_legendBorderThicknessBox, min: 0, max: 10, out var legendBorderThickness))
        {
            ShowInvalidInputWarning(UiText.Get("ChartAreaLegend_InvalidLegendBorderWidthMessage"), _legendBorderThicknessBox);
            return;
        }

        if (!TryReadClampedDouble(_legendFontSizeBox, min: 6, max: 72, out var legendFontSize))
        {
            ShowInvalidInputWarning(UiText.Get("ChartAreaLegend_InvalidLegendFontSizeMessage"), _legendFontSizeBox);
            return;
        }

        Result = CreateResult(
            chartAreaFillColor,
            plotAreaFillColor,
            plotAreaBorderColor,
            plotAreaBorderThickness,
            _showLegendBox.IsChecked == true,
            ChartDialogHelpers.Selected(_legendPositionBox, ChartLegendPosition.Right),
            _legendOverlayBox.IsChecked == true,
            legendTextColor,
            legendFillColor,
            legendBorderColor,
            legendBorderThickness,
            legendFontSize);
        DialogResult = true;
    }

    private static double FiniteOrDefault(double value, double fallback) =>
        double.IsFinite(value) ? value : fallback;

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
        return true;
    }
}

