using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogInputParser;
using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

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
    private readonly CheckBox _showBox = new() { Content = UiText.Get("ChartTrendline_ShowTrendline") };
    private readonly CheckBox _equationBox = new() { Content = UiText.Get("ChartTrendline_DisplayEquation") };
    private readonly CheckBox _rSquaredBox = new() { Content = UiText.Get("ChartTrendline_DisplayRSquared") };
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
        Title = UiText.Get("ChartTrendline_Title");
        Width = 380;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
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
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartTrendline_TypeLabel"), _typeBox, Enum.GetValues<ChartTrendlineType>());
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartTrendline_PeriodLabel"), _periodBox, UiText.Get("ChartTrendline_PeriodHelpText"));
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartTrendline_OrderLabel"), _orderBox, UiText.Get("ChartTrendline_OrderHelpText"));
            ChartDialogHelpers.AddCheck(stack, _equationBox);
            ChartDialogHelpers.AddCheck(stack, _rSquaredBox);
            root.Children.Add(CreateGroupBox(UiText.Get("ChartTrendline_OptionsGroup"), stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartTrendline_LineColorLabel"), _colorBox);
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartTrendline_LineWidthLabel"), _thicknessBox, UiText.Get("ChartTrendline_LineWidthHelpText"));
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartTrendline_DashStyleLabel"), _dashBox, Enum.GetValues<ChartLineDashStyle>());
            root.Children.Add(CreateGroupBox(UiText.Get("ChartDialog_FillLineGroup"), stack));
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

    private void FocusInitialKeyboardTarget()
    {
        _showBox.Focus();
        Keyboard.Focus(_showBox);
    }

    private void Accept()
    {
        if (!TryReadIntInRange(_periodBox, min: 2, max: 255, out var period))
        {
            ShowInvalidInputWarning(UiText.Get("ChartTrendline_InvalidPeriodMessage"), _periodBox);
            return;
        }

        if (!TryReadIntInRange(_orderBox, min: 2, max: 6, out var order))
        {
            ShowInvalidInputWarning(UiText.Get("ChartTrendline_InvalidOrderMessage"), _orderBox);
            return;
        }

        if (!TryReadOptionalColor(_colorBox, out var color))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _colorBox);
            return;
        }

        if (!TryReadClampedDouble(_thicknessBox, min: 0.5, max: 10, out var thickness))
        {
            ShowInvalidInputWarning(UiText.Get("ChartTrendline_InvalidWidthMessage"), _thicknessBox);
            return;
        }

        Result = CreateResult(
            _showBox.IsChecked == true,
            ChartDialogHelpers.Selected(_typeBox, ChartTrendlineType.Linear),
            period,
            order,
            _equationBox.IsChecked == true,
            _rSquaredBox.IsChecked == true,
            color,
            thickness,
            ChartDialogHelpers.Selected(_dashBox, ChartLineDashStyle.Solid));
        DialogResult = true;
    }

    private static bool TryReadIntInRange(TextBox textBox, int min, int max, out int value)
    {
        value = 0;
        return int.TryParse(
                textBox.Text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value)
            && value >= min
            && value <= max;
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
