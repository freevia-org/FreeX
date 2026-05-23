using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

public sealed record ChartTrendlineOptionsDialogResult(
    bool ShowTrendline,
    ChartTrendlineType Type,
    int Period,
    int Order,
    bool ShowEquation,
    bool ShowRSquared,
    CellColor? Color,
    double Thickness,
    ChartLineDashStyle DashStyle)
{
    public ChartLayoutOptions ToOptions() => new(
        ShowLinearTrendline: ShowTrendline,
        TrendlineType: Type,
        TrendlinePeriod: Period,
        TrendlineOrder: Order,
        ShowTrendlineEquation: ShowEquation,
        ShowTrendlineRSquared: ShowRSquared,
        TrendlineColor: Color,
        TrendlineThickness: Thickness,
        TrendlineDashStyle: DashStyle);
}

public sealed class ChartTrendlineOptionsDialog : Window
{
    private readonly CheckBox _showBox = new() { Content = "_Show trendline" };
    private readonly CheckBox _equationBox = new() { Content = "Display _equation" };
    private readonly CheckBox _rSquaredBox = new() { Content = "Display _R-squared value" };
    private readonly ComboBox _typeBox = new();
    private readonly ComboBox _dashBox = new();
    private readonly TextBox _periodBox = new();
    private readonly TextBox _orderBox = new();
    private readonly TextBox _colorBox = new();
    private readonly TextBox _thicknessBox = new();

    public ChartTrendlineOptionsDialogResult Result { get; private set; }

    public ChartTrendlineOptionsDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "Format Trendline";
        Width = 380;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static ChartTrendlineOptionsDialogResult FromChart(ChartModel chart) => CreateResult(
        chart.ShowLinearTrendline,
        chart.TrendlineType,
        chart.TrendlinePeriod,
        chart.TrendlineOrder,
        chart.ShowTrendlineEquation,
        chart.ShowTrendlineRSquared,
        chart.TrendlineColor,
        chart.TrendlineThickness,
        chart.TrendlineDashStyle);

    public static ChartTrendlineOptionsDialogResult CreateResult(
        bool showTrendline,
        ChartTrendlineType type,
        int period,
        int order,
        bool showEquation,
        bool showRSquared,
        CellColor? color,
        double thickness,
        ChartLineDashStyle dashStyle) =>
        new(showTrendline, type, Math.Clamp(period, 2, 255), Math.Clamp(order, 2, 6), showEquation, showRSquared, color, thickness, dashStyle);

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _showBox);
            ChartDialogHelpers.AddCombo(stack, "Type", _typeBox, Enum.GetValues<ChartTrendlineType>());
            ChartDialogHelpers.AddNumericText(stack, "Moving average period", _periodBox, "Enter a period from 2 to 255.");
            ChartDialogHelpers.AddNumericText(stack, "Polynomial order", _orderBox, "Enter an order from 2 to 6.");
            ChartDialogHelpers.AddCheck(stack, _equationBox);
            ChartDialogHelpers.AddCheck(stack, _rSquaredBox);
            root.Children.Add(CreateGroupBox("Trendline Options", stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddColorText(stack, "Line color", _colorBox);
            ChartDialogHelpers.AddNumericText(stack, "Line width", _thicknessBox, "Enter a line width in points.");
            ChartDialogHelpers.AddCombo(stack, "Dash style", _dashBox, Enum.GetValues<ChartLineDashStyle>());
            root.Children.Add(CreateGroupBox("Fill & Line", stack));
        }
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartTrendlineOptionsDialogResult result)
    {
        _showBox.IsChecked = result.ShowTrendline;
        _typeBox.SelectedItem = result.Type;
        _periodBox.Text = result.Period.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _orderBox.Text = result.Order.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _equationBox.IsChecked = result.ShowEquation;
        _rSquaredBox.IsChecked = result.ShowRSquared;
        _colorBox.Text = ChartDialogHelpers.FormatColor(result.Color);
        _thicknessBox.Text = result.Thickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _dashBox.SelectedItem = result.DashStyle;
    }

    private void Accept()
    {
        Result = CreateResult(
            _showBox.IsChecked == true,
            ChartDialogHelpers.Selected(_typeBox, ChartTrendlineType.Linear),
            (int)ChartDialogHelpers.ParseDouble(_periodBox.Text, 2),
            (int)ChartDialogHelpers.ParseDouble(_orderBox.Text, 2),
            _equationBox.IsChecked == true,
            _rSquaredBox.IsChecked == true,
            ChartDialogHelpers.ParseColor(_colorBox.Text),
            ChartDialogHelpers.ParseDouble(_thicknessBox.Text, 1.5),
            ChartDialogHelpers.Selected(_dashBox, ChartLineDashStyle.Solid));
        DialogResult = true;
    }
}
