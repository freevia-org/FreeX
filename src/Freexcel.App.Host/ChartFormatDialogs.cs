using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Freexcel.Core.Commands;
using Freexcel.Core.Model;

using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

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
    private readonly CheckBox _showLegendBox = new() { Content = "_Show legend" };
    private readonly ComboBox _legendPositionBox = new();
    private readonly CheckBox _legendOverlayBox = new() { Content = "O_verlay legend on chart" };
    private readonly TextBox _legendTextBox = new();
    private readonly TextBox _legendFillBox = new();
    private readonly TextBox _legendBorderBox = new();
    private readonly TextBox _legendBorderThicknessBox = new();
    private readonly TextBox _legendFontSizeBox = new();

    public ChartAreaLegendDialogResult Result { get; private set; }

    public ChartAreaLegendDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "Format Chart Area";
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
            stack.Children.Add(CreateInlineHelp("Set the chart and plot area fills, borders, and line weights."));
            ChartDialogHelpers.AddColorText(stack, "Chart area fill color", _chartAreaFillBox);
            ChartDialogHelpers.AddColorText(stack, "Plot area fill color", _plotAreaFillBox);
            ChartDialogHelpers.AddColorText(stack, "Plot area border color", _plotAreaBorderBox);
            ChartDialogHelpers.AddNumericText(stack, "Plot area border width", _plotAreaBorderThicknessBox, "Enter a line width from 0 to 10 points.");
            root.Children.Add(CreateGroupBox("Fill & Line", stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _showLegendBox);
            ChartDialogHelpers.AddCombo(stack, "Legend position", _legendPositionBox, Enum.GetValues<ChartLegendPosition>());
            ChartDialogHelpers.AddCheck(stack, _legendOverlayBox);
            ChartDialogHelpers.AddColorText(stack, "Legend text color", _legendTextBox);
            ChartDialogHelpers.AddColorText(stack, "Legend fill color", _legendFillBox);
            ChartDialogHelpers.AddColorText(stack, "Legend border color", _legendBorderBox);
            ChartDialogHelpers.AddNumericText(stack, "Legend border width", _legendBorderThicknessBox, "Enter a line width from 0 to 10 points.");
            ChartDialogHelpers.AddNumericText(stack, "Legend font size", _legendFontSizeBox, "Enter a font size from 6 to 72 points.");
            root.Children.Add(CreateGroupBox("Legend", stack));
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
            ShowInvalidInputWarning("Enter a color as #RRGGBB or none.", _chartAreaFillBox);
            return;
        }

        if (!TryReadClampedDouble(_plotAreaBorderThicknessBox, min: 0, max: 10, out var plotAreaBorderThickness))
        {
            ShowInvalidInputWarning("Enter a plot area border width from 0 to 10 points.", _plotAreaBorderThicknessBox);
            return;
        }

        if (!TryReadClampedDouble(_legendFontSizeBox, min: 6, max: 72, out var legendFontSize))
        {
            ShowInvalidInputWarning("Enter a legend font size from 6 to 72 points.", _legendFontSizeBox);
            return;
        }

        Result = CreateResult(
            chartAreaFillColor,
            ChartDialogHelpers.ParseColor(_plotAreaFillBox.Text),
            ChartDialogHelpers.ParseColor(_plotAreaBorderBox.Text),
            plotAreaBorderThickness,
            _showLegendBox.IsChecked == true,
            ChartDialogHelpers.Selected(_legendPositionBox, ChartLegendPosition.Right),
            _legendOverlayBox.IsChecked == true,
            ChartDialogHelpers.ParseColor(_legendTextBox.Text),
            ChartDialogHelpers.ParseColor(_legendFillBox.Text),
            ChartDialogHelpers.ParseColor(_legendBorderBox.Text),
            ChartDialogHelpers.ParseDouble(_legendBorderThicknessBox.Text, 0),
            legendFontSize);
        DialogResult = true;
    }

    private static double FiniteOrDefault(double value, double fallback) =>
        double.IsFinite(value) ? value : fallback;

    private static bool TryReadOptionalColor(TextBox textBox, out CellColor? color) =>
        ColorInputParser.TryParseOptionalHexColor(textBox.Text, out color);

    private static bool TryReadClampedDouble(TextBox textBox, double min, double max, out double value)
    {
        value = 0;
        return double.TryParse(
                textBox.Text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value)
            && double.IsFinite(value)
            && value >= min
            && value <= max;
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

