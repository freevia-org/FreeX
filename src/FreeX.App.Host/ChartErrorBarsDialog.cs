using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogInputParser;
using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

public sealed record ChartErrorBarsDialogResult(
    bool ShowErrorBars,
    ChartErrorBarKind Kind,
    ChartErrorBarDirection Direction,
    double Value,
    bool EndCaps)
{
    public ChartLayoutOptions ToOptions() => new(
        ShowErrorBars: ShowErrorBars,
        ErrorBarKind: Kind,
        ErrorBarDirection: Direction,
        ErrorBarValue: Value,
        ErrorBarEndCaps: EndCaps);
}

public sealed class ChartErrorBarsDialog : Window
{
    private readonly CheckBox _showBox = new() { Content = UiText.Get("ChartErrorBars_ShowErrorBars") };
    private readonly CheckBox _endCapsBox = new() { Content = UiText.Get("ChartErrorBars_EndCaps") };
    private readonly ComboBox _kindBox = new();
    private readonly ComboBox _directionBox = new();
    private readonly TextBox _valueBox = new();

    public ChartErrorBarsDialogResult Result { get; private set; }

    public ChartErrorBarsDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = UiText.Get("ChartErrorBars_Title");
        Width = 360;
        Height = 290;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static ChartErrorBarsDialogResult FromChart(ChartModel chart) => CreateResult(
        chart.ShowErrorBars,
        chart.ErrorBarKind,
        chart.ErrorBarDirection,
        chart.ErrorBarValue,
        chart.ErrorBarEndCaps);

    public static ChartErrorBarsDialogResult CreateResult(
        bool showErrorBars,
        ChartErrorBarKind kind,
        ChartErrorBarDirection direction,
        double value,
        bool endCaps) =>
        new(
            showErrorBars,
            Enum.IsDefined(kind) ? kind : ChartErrorBarKind.StandardError,
            Enum.IsDefined(direction) ? direction : ChartErrorBarDirection.Both,
            Math.Clamp(double.IsFinite(value) ? value : 5, 0, 1000),
            endCaps);

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        var stack = new StackPanel();
        ChartDialogHelpers.AddCheck(stack, _showBox);
        ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartErrorBars_TypeLabel"), _kindBox, Enum.GetValues<ChartErrorBarKind>());
        ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartErrorBars_DirectionLabel"), _directionBox, Enum.GetValues<ChartErrorBarDirection>());
        ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartErrorBars_ValueLabel"), _valueBox, UiText.Get("ChartErrorBars_ValueHelpText"));
        System.Windows.Automation.AutomationProperties.SetName(_valueBox, UiText.Get("ChartErrorBars_ValueAutomationName"));
        ChartDialogHelpers.AddCheck(stack, _endCapsBox);
        root.Children.Add(CreateGroupBox(UiText.Get("ChartErrorBars_ErrorAmountGroup"), stack));
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartErrorBarsDialogResult result)
    {
        _showBox.IsChecked = result.ShowErrorBars;
        _kindBox.SelectedItem = result.Kind;
        _directionBox.SelectedItem = result.Direction;
        _valueBox.Text = result.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _endCapsBox.IsChecked = result.EndCaps;
    }

    private void FocusInitialKeyboardTarget()
    {
        _showBox.Focus();
        Keyboard.Focus(_showBox);
    }

    private void Accept()
    {
        if (!TryReadClampedDouble(_valueBox, min: 0, max: 1000, out var value))
        {
            ShowInvalidInputWarning(UiText.Get("ChartErrorBars_InvalidValueMessage"), _valueBox);
            return;
        }

        Result = CreateResult(
            _showBox.IsChecked == true,
            ChartDialogHelpers.Selected(_kindBox, ChartErrorBarKind.StandardError),
            ChartDialogHelpers.Selected(_directionBox, ChartErrorBarDirection.Both),
            value,
            _endCapsBox.IsChecked == true);
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
