using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

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
    public ChartTrendlineInput ToInput() =>
        new(ShowTrendline, Type, Period, Order, ShowEquation, ShowRSquared, Color, Thickness, DashStyle);

    public ChartLayoutOptions ToOptions() => ChartTrendlinePlanner.Plan(ToInput());
}

public sealed class ChartTrendlineOptionsDialog : Window
{
    private readonly CheckBox _showBox = new() { Content = LabelText(ChartTrendlineDialogFieldId.ShowTrendline) };
    private readonly CheckBox _equationBox = new() { Content = LabelText(ChartTrendlineDialogFieldId.ShowEquation) };
    private readonly CheckBox _rSquaredBox = new() { Content = LabelText(ChartTrendlineDialogFieldId.ShowRSquared) };
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
        ApplyAutomationIds();
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static ChartTrendlineOptionsDialogResult FromChart(ChartModel chart)
    {
        var input = ChartTrendlinePlanner.Read(chart);
        return CreateResult(
            input.ShowTrendline,
            input.Type,
            input.Period,
            input.Order,
            input.ShowEquation,
            input.ShowRSquared,
            input.Color,
            input.Thickness ?? chart.TrendlineThickness,
            input.DashStyle ?? chart.TrendlineDashStyle);
    }

    public static ChartTrendlineOptionsDialogResult CreateResult(
        bool showTrendline,
        ChartTrendlineType type,
        int period,
        int order,
        bool showEquation,
        bool showRSquared,
        CellColor? color,
        double thickness,
        ChartLineDashStyle dashStyle)
    {
        var input = ChartTrendlinePlanner.Normalize(new ChartTrendlineInput(
            showTrendline,
            type,
            period,
            order,
            showEquation,
            showRSquared,
            color,
            thickness,
            dashStyle));
        return new(
            input.ShowTrendline,
            input.Type,
            input.Period,
            input.Order,
            input.ShowEquation,
            input.ShowRSquared,
            input.Color,
            input.Thickness ?? thickness,
            input.DashStyle ?? dashStyle);
    }

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var section = ChartTrendlinePlanner.GetOptionsSection();
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _showBox);
            ChartDialogHelpers.AddCombo(
                stack,
                LabelText(ChartTrendlineDialogFieldId.Type),
                _typeBox,
                ChartTrendlinePlanner.GetTypeChoices().Select(choice => choice.Type).ToArray());
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartTrendlineDialogFieldId.Period), _periodBox, HelpText(ChartTrendlineDialogFieldId.Period));
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartTrendlineDialogFieldId.Order), _orderBox, HelpText(ChartTrendlineDialogFieldId.Order));
            ChartDialogHelpers.AddCheck(stack, _equationBox);
            ChartDialogHelpers.AddCheck(stack, _rSquaredBox);
            root.Children.Add(CreateGroupBox(UiText.Get(section.HeaderResourceKey), stack));
        }
        {
            var section = ChartTrendlinePlanner.GetLineSection();
            var stack = new StackPanel();
            ChartDialogHelpers.AddColorText(stack, LabelText(ChartTrendlineDialogFieldId.LineColor), _colorBox);
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartTrendlineDialogFieldId.LineThickness), _thicknessBox, HelpText(ChartTrendlineDialogFieldId.LineThickness));
            ChartDialogHelpers.AddCombo(stack, LabelText(ChartTrendlineDialogFieldId.DashStyle), _dashBox, ChartTrendlinePlanner.GetDashStyleChoices());
            root.Children.Add(CreateGroupBox(UiText.Get(section.HeaderResourceKey), stack));
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
        if (!ChartTrendlinePlanner.TryParseDialogInput(
                _showBox.IsChecked == true,
                SelectedTrendlineType(),
                _periodBox.Text,
                _orderBox.Text,
                _equationBox.IsChecked == true,
                _rSquaredBox.IsChecked == true,
                _colorBox.Text,
                _thicknessBox.Text,
                SelectedDashStyle(),
                out var input,
                out var issue))
        {
            ShowPlannerParseWarning(issue);
            return;
        }

        Result = CreateResult(
            input.ShowTrendline,
            input.Type,
            input.Period,
            input.Order,
            input.ShowEquation,
            input.ShowRSquared,
            input.Color,
            input.Thickness.GetValueOrDefault(),
            input.DashStyle.GetValueOrDefault());
        DialogResult = true;
    }

    private ChartTrendlineType? SelectedTrendlineType() =>
        _typeBox.SelectedItem is ChartTrendlineType value ? value : null;

    private ChartLineDashStyle? SelectedDashStyle() =>
        _dashBox.SelectedItem is ChartLineDashStyle value ? value : null;

    private void ShowPlannerParseWarning(ChartTrendlineDialogParseIssue issue)
    {
        var presentation = ChartValidationPresentationPlanner.Describe(issue);
        var target = presentation.FocusTarget switch
        {
            ChartTrendlineDialogFieldId.Order => _orderBox,
            ChartTrendlineDialogFieldId.LineColor => _colorBox,
            ChartTrendlineDialogFieldId.LineThickness => _thicknessBox,
            _ => _periodBox
        };
        ShowInvalidInputWarning(presentation.Message.Resolve(UiText.Get, UiText.Format), target);
    }

    private void ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
    }

    private void ApplyAutomationIds()
    {
        AutomationProperties.SetAutomationId(_showBox, Field(ChartTrendlineDialogFieldId.ShowTrendline).AutomationId);
        AutomationProperties.SetAutomationId(_typeBox, Field(ChartTrendlineDialogFieldId.Type).AutomationId);
        AutomationProperties.SetAutomationId(_periodBox, Field(ChartTrendlineDialogFieldId.Period).AutomationId);
        AutomationProperties.SetAutomationId(_orderBox, Field(ChartTrendlineDialogFieldId.Order).AutomationId);
        AutomationProperties.SetAutomationId(_equationBox, Field(ChartTrendlineDialogFieldId.ShowEquation).AutomationId);
        AutomationProperties.SetAutomationId(_rSquaredBox, Field(ChartTrendlineDialogFieldId.ShowRSquared).AutomationId);
        AutomationProperties.SetAutomationId(_colorBox, Field(ChartTrendlineDialogFieldId.LineColor).AutomationId);
        AutomationProperties.SetAutomationId(_thicknessBox, Field(ChartTrendlineDialogFieldId.LineThickness).AutomationId);
        AutomationProperties.SetAutomationId(_dashBox, Field(ChartTrendlineDialogFieldId.DashStyle).AutomationId);
    }

    private static string LabelText(ChartTrendlineDialogFieldId id) =>
        UiText.Get(Field(id).LabelResourceKey);

    private static string HelpText(ChartTrendlineDialogFieldId id) =>
        UiText.Get(Field(id).HelpResourceKey ?? throw new InvalidOperationException($"Field {id} has no help resource key."));

    private static ChartTrendlineDialogFieldDescriptor Field(ChartTrendlineDialogFieldId id) =>
        ChartTrendlinePlanner.GetDialogField(id);
}
