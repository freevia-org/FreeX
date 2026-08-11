using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

public sealed class ChartErrorBarsDialog : Window
{
    private readonly CheckBox _showBox = new() { Content = LabelText(ChartErrorBarsDialogFieldId.ShowErrorBars) };
    private readonly CheckBox _endCapsBox = new() { Content = LabelText(ChartErrorBarsDialogFieldId.EndCaps) };
    private readonly ComboBox _kindBox = new();
    private readonly ComboBox _directionBox = new();
    private readonly TextBox _valueBox = new();

    public ChartErrorBarsInput Result { get; private set; }

    public ChartErrorBarsDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = UiText.Get("ChartErrorBars_Title");
        Width = 360;
        Height = 290;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ApplyAutomationIds();
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static ChartErrorBarsInput FromChart(ChartModel chart) => ChartErrorBarsPlanner.Read(chart);

    public static ChartErrorBarsInput CreateResult(
        bool showErrorBars,
        ChartErrorBarKind kind,
        ChartErrorBarDirection direction,
        double value,
        bool endCaps)
    {
        return ChartErrorBarsPlanner.Normalize(new ChartErrorBarsInput(
            showErrorBars,
            kind,
            direction,
            value,
            endCaps));
    }

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        var section = ChartErrorBarsPlanner.GetErrorAmountSection();
        var stack = new StackPanel();
        ChartDialogHelpers.AddCheck(stack, _showBox);
        ChartDialogHelpers.AddCombo(stack, LabelText(ChartErrorBarsDialogFieldId.Kind), _kindBox, ChartErrorBarsPlanner.GetKindChoices().Select(choice => choice.Kind));
        ChartDialogHelpers.AddCombo(stack, LabelText(ChartErrorBarsDialogFieldId.Direction), _directionBox, ChartErrorBarsPlanner.GetDirectionChoices().Select(choice => choice.Direction));
        ChartDialogHelpers.AddNumericText(stack, LabelText(ChartErrorBarsDialogFieldId.Value), _valueBox, HelpText(ChartErrorBarsDialogFieldId.Value));
        AutomationProperties.SetName(_valueBox, AutomationNameText(ChartErrorBarsDialogFieldId.Value));
        ChartDialogHelpers.AddCheck(stack, _endCapsBox);
        root.Children.Add(CreateGroupBox(UiText.Get(section.HeaderResourceKey), stack));
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartErrorBarsInput result)
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
        var presentation = ChartValidationPresentationPlanner.Describe(issue);
        ShowInvalidInputWarning(presentation.Message.Resolve(UiText.Get, UiText.Format), _valueBox);
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        return true;
    }

    private void ApplyAutomationIds()
    {
        AutomationProperties.SetAutomationId(_showBox, Field(ChartErrorBarsDialogFieldId.ShowErrorBars).AutomationId);
        AutomationProperties.SetAutomationId(_kindBox, Field(ChartErrorBarsDialogFieldId.Kind).AutomationId);
        AutomationProperties.SetAutomationId(_directionBox, Field(ChartErrorBarsDialogFieldId.Direction).AutomationId);
        AutomationProperties.SetAutomationId(_valueBox, Field(ChartErrorBarsDialogFieldId.Value).AutomationId);
        AutomationProperties.SetAutomationId(_endCapsBox, Field(ChartErrorBarsDialogFieldId.EndCaps).AutomationId);
    }

    private static string LabelText(ChartErrorBarsDialogFieldId id) =>
        UiText.Get(Field(id).LabelResourceKey);

    private static string HelpText(ChartErrorBarsDialogFieldId id) =>
        UiText.Get(Field(id).HelpResourceKey ?? throw new InvalidOperationException($"Field {id} has no help resource key."));

    private static string AutomationNameText(ChartErrorBarsDialogFieldId id) =>
        UiText.Get(Field(id).AutomationNameResourceKey ?? Field(id).LabelResourceKey);

    private static ChartErrorBarsDialogFieldDescriptor Field(ChartErrorBarsDialogFieldId id) =>
        ChartErrorBarsPlanner.GetDialogField(id);
}
