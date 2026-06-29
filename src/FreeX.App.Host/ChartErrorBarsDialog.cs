using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

public sealed record ChartErrorBarsDialogResult(
    bool ShowErrorBars,
    ChartErrorBarKind Kind,
    ChartErrorBarDirection Direction,
    double Value,
    bool EndCaps)
{
    public ChartErrorBarsInput ToInput() => new(ShowErrorBars, Kind, Direction, Value, EndCaps);

    public ChartLayoutOptions ToOptions() => ChartErrorBarsPlanner.Plan(ToInput());
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

    public static ChartErrorBarsDialogResult FromChart(ChartModel chart)
    {
        var input = ChartErrorBarsPlanner.Read(chart);
        return CreateResult(input.ShowErrorBars, input.Kind, input.Direction, input.Value, input.EndCaps);
    }

    public static ChartErrorBarsDialogResult CreateResult(
        bool showErrorBars,
        ChartErrorBarKind kind,
        ChartErrorBarDirection direction,
        double value,
        bool endCaps)
    {
        var input = ChartErrorBarsPlanner.Normalize(new ChartErrorBarsInput(
            showErrorBars,
            kind,
            direction,
            value,
            endCaps));
        return new(input.ShowErrorBars, input.Kind, input.Direction, input.Value, input.EndCaps);
    }

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        var stack = new StackPanel();
        ChartDialogHelpers.AddCheck(stack, _showBox);
        ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartErrorBars_TypeLabel"), _kindBox, ChartErrorBarsPlanner.GetKindChoices().Select(choice => choice.Kind));
        ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartErrorBars_DirectionLabel"), _directionBox, ChartErrorBarsPlanner.GetDirectionChoices().Select(choice => choice.Direction));
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
        if (!ChartErrorBarsPlanner.TryParseDialogInput(
                _showBox.IsChecked == true,
                SelectedKind(),
                SelectedDirection(),
                _valueBox.Text,
                _endCapsBox.IsChecked == true,
                out var input,
                out var issue))
        {
            ShowPlannerParseWarning(issue);
            return;
        }

        Result = CreateResult(
            input.ShowErrorBars,
            input.Kind,
            input.Direction,
            input.Value,
            input.EndCaps);
        DialogResult = true;
    }

    private ChartErrorBarKind? SelectedKind() =>
        _kindBox.SelectedItem is ChartErrorBarKind value ? value : null;

    private ChartErrorBarDirection? SelectedDirection() =>
        _directionBox.SelectedItem is ChartErrorBarDirection value ? value : null;

    private void ShowPlannerParseWarning(ChartErrorBarsParseIssue issue)
    {
        var (message, target) = issue switch
        {
            ChartErrorBarsParseIssue.Value => (UiText.Get("ChartErrorBars_InvalidValueMessage"), _valueBox),
            _ => (UiText.Get("ChartErrorBars_InvalidValueMessage"), _valueBox),
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
