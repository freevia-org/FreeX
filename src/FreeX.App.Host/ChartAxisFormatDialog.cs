using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

public sealed class ChartAxisFormatDialog : Window
{
    private readonly bool _useXAxis;
    private readonly TextBox _minimumBox = new();
    private readonly TextBox _maximumBox = new();
    private readonly TextBox _majorUnitBox = new();
    private readonly TextBox _minorUnitBox = new();
    private readonly CheckBox _logBox = new() { Content = LabelText(ChartAxisDialogFieldId.LogScale) };
    private readonly ComboBox _numberFormatBox = new();
    private readonly CheckBox _majorGridBox = new() { Content = LabelText(ChartAxisDialogFieldId.MajorGridlines) };
    private readonly CheckBox _minorGridBox = new() { Content = LabelText(ChartAxisDialogFieldId.MinorGridlines) };
    private readonly TextBox _majorGridColorBox = new();
    private readonly TextBox _minorGridColorBox = new();
    private readonly TextBox _gridlineThicknessBox = new();
    private readonly ComboBox _majorTickBox = new();
    private readonly ComboBox _minorTickBox = new();
    private readonly CheckBox _labelsBox = new() { Content = LabelText(ChartAxisDialogFieldId.ShowLabels) };
    private readonly TextBox _labelColorBox = new();
    private readonly TextBox _labelFontSizeBox = new();
    private readonly TextBox _labelAngleBox = new();
    private readonly TextBox _lineColorBox = new();
    private readonly TextBox _lineThicknessBox = new();

    public ChartAxisInput Result { get; private set; }

    public ChartAxisFormatDialog(ChartModel chart, bool useXAxis)
    {
        _useXAxis = useXAxis;
        Result = ChartAxisPlanner.Read(chart, useXAxis);
        Title = useXAxis ? UiText.Get("ChartAxisFormat_XAxisTitle") : UiText.Get("ChartAxisFormat_YAxisTitle");
        Width = 430;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ApplyAutomationIds();
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var section = ChartAxisPlanner.GetAxisOptionsSection();
            var stack = new StackPanel();
            if (section.HelpResourceKey is { } helpKey)
                stack.Children.Add(CreateInlineHelp(UiText.Get(helpKey)));
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartAxisDialogFieldId.Minimum), _minimumBox, HelpText(ChartAxisDialogFieldId.Minimum));
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartAxisDialogFieldId.Maximum), _maximumBox, HelpText(ChartAxisDialogFieldId.Maximum));
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartAxisDialogFieldId.MajorUnit), _majorUnitBox, HelpText(ChartAxisDialogFieldId.MajorUnit));
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartAxisDialogFieldId.MinorUnit), _minorUnitBox, HelpText(ChartAxisDialogFieldId.MinorUnit));
            ChartDialogHelpers.AddCheck(stack, _logBox);
            ChartDialogHelpers.AddCombo(stack, LabelText(ChartAxisDialogFieldId.NumberFormat), _numberFormatBox, ChartAxisPlanner.GetNumberFormatChoices().Select(choice => choice.NumberFormat));
            root.Children.Add(CreateGroupBox(UiText.Get(section.HeaderResourceKey), stack));
        }
        {
            var section = ChartAxisPlanner.GetGridlinesSection();
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _majorGridBox);
            ChartDialogHelpers.AddCheck(stack, _minorGridBox);
            ChartDialogHelpers.AddColorText(stack, LabelText(ChartAxisDialogFieldId.MajorGridlineColor), _majorGridColorBox);
            ChartDialogHelpers.AddColorText(stack, LabelText(ChartAxisDialogFieldId.MinorGridlineColor), _minorGridColorBox);
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartAxisDialogFieldId.GridlineThickness), _gridlineThicknessBox, HelpText(ChartAxisDialogFieldId.GridlineThickness));
            root.Children.Add(CreateGroupBox(UiText.Get(section.HeaderResourceKey), stack));
        }
        {
            var section = ChartAxisPlanner.GetTickMarksSection();
            var stack = new StackPanel();
            ChartDialogHelpers.AddCombo(stack, LabelText(ChartAxisDialogFieldId.MajorTickMarks), _majorTickBox, ChartAxisPlanner.GetTickStyleChoices());
            ChartDialogHelpers.AddCombo(stack, LabelText(ChartAxisDialogFieldId.MinorTickMarks), _minorTickBox, ChartAxisPlanner.GetTickStyleChoices());
            ChartDialogHelpers.AddCheck(stack, _labelsBox);
            ChartDialogHelpers.AddColorText(stack, LabelText(ChartAxisDialogFieldId.LabelTextColor), _labelColorBox);
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartAxisDialogFieldId.LabelFontSize), _labelFontSizeBox, HelpText(ChartAxisDialogFieldId.LabelFontSize));
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartAxisDialogFieldId.LabelAngle), _labelAngleBox, HelpText(ChartAxisDialogFieldId.LabelAngle));
            ChartDialogHelpers.AddColorText(stack, LabelText(ChartAxisDialogFieldId.LineColor), _lineColorBox);
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartAxisDialogFieldId.LineThickness), _lineThicknessBox, HelpText(ChartAxisDialogFieldId.LineThickness));
            root.Children.Add(CreateGroupBox(UiText.Get(section.HeaderResourceKey), stack));
        }
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartAxisInput result)
    {
        _minimumBox.Text = ChartDialogHelpers.FormatNullable(result.Minimum);
        _maximumBox.Text = ChartDialogHelpers.FormatNullable(result.Maximum);
        _majorUnitBox.Text = ChartDialogHelpers.FormatNullable(result.MajorUnit);
        _minorUnitBox.Text = ChartDialogHelpers.FormatNullable(result.MinorUnit);
        _logBox.IsChecked = result.LogScale;
        _numberFormatBox.SelectedItem = result.NumberFormat;
        _majorGridBox.IsChecked = result.ShowMajorGridlines;
        _minorGridBox.IsChecked = result.ShowMinorGridlines;
        _majorGridColorBox.Text = ChartDialogHelpers.FormatColor(result.MajorGridlineColor);
        _minorGridColorBox.Text = ChartDialogHelpers.FormatColor(result.MinorGridlineColor);
        _gridlineThicknessBox.Text = (result.GridlineThickness ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _majorTickBox.SelectedItem = result.MajorTickStyle ?? ChartAxisTickStyle.Outside;
        _minorTickBox.SelectedItem = result.MinorTickStyle ?? ChartAxisTickStyle.None;
        _labelsBox.IsChecked = result.ShowLabels ?? true;
        _labelColorBox.Text = ChartDialogHelpers.FormatColor(result.LabelTextColor);
        _labelFontSizeBox.Text = (result.LabelFontSize ?? 11).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _labelAngleBox.Text = (result.LabelAngle ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _lineColorBox.Text = ChartDialogHelpers.FormatColor(result.LineColor);
        _lineThicknessBox.Text = (result.LineThickness ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void FocusInitialKeyboardTarget()
    {
        _minimumBox.Focus();
        _minimumBox.SelectAll();
        Keyboard.Focus(_minimumBox);
    }

    private void Accept()
    {
        if (!ChartAxisPlanner.TryParseDialogInput(
                _useXAxis,
                _minimumBox.Text,
                _maximumBox.Text,
                _majorUnitBox.Text,
                _minorUnitBox.Text,
                _logBox.IsChecked == true,
                SelectedNumberFormat(),
                _majorGridBox.IsChecked == true,
                _minorGridBox.IsChecked == true,
                _majorGridColorBox.Text,
                _minorGridColorBox.Text,
                _gridlineThicknessBox.Text,
                SelectedMajorTickStyle(),
                SelectedMinorTickStyle(),
                _labelsBox.IsChecked == true,
                _labelColorBox.Text,
                _labelFontSizeBox.Text,
                _labelAngleBox.Text,
                _lineColorBox.Text,
                _lineThicknessBox.Text,
                out var input,
                out var issue))
        {
            ShowPlannerParseWarning(issue);
            return;
        }

        Result = input;
        DialogResult = true;
    }

    private ChartDataLabelNumberFormat? SelectedNumberFormat() =>
        _numberFormatBox.SelectedItem is ChartDataLabelNumberFormat value ? value : null;

    private ChartAxisTickStyle? SelectedMajorTickStyle() =>
        _majorTickBox.SelectedItem is ChartAxisTickStyle value ? value : null;

    private ChartAxisTickStyle? SelectedMinorTickStyle() =>
        _minorTickBox.SelectedItem is ChartAxisTickStyle value ? value : null;

    private void ShowPlannerParseWarning(ChartAxisFormatParseIssue issue)
    {
        var presentation = ChartValidationPresentationPlanner.Describe(issue);
        var target = presentation.FocusTarget switch
        {
            ChartAxisDialogFieldId.Maximum => _maximumBox,
            ChartAxisDialogFieldId.MajorUnit => _majorUnitBox,
            ChartAxisDialogFieldId.MinorUnit => _minorUnitBox,
            ChartAxisDialogFieldId.MajorGridlineColor => _majorGridColorBox,
            ChartAxisDialogFieldId.MinorGridlineColor => _minorGridColorBox,
            ChartAxisDialogFieldId.GridlineThickness => _gridlineThicknessBox,
            ChartAxisDialogFieldId.LabelTextColor => _labelColorBox,
            ChartAxisDialogFieldId.LabelFontSize => _labelFontSizeBox,
            ChartAxisDialogFieldId.LabelAngle => _labelAngleBox,
            ChartAxisDialogFieldId.LineColor => _lineColorBox,
            ChartAxisDialogFieldId.LineThickness => _lineThicknessBox,
            _ => _minimumBox,
        };
        ShowInvalidInputWarning(presentation.Message.Resolve(UiText.Get, UiText.Format), target);
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        return true;
    }

    private void ApplyAutomationIds()
    {
        AutomationProperties.SetAutomationId(_minimumBox, Field(ChartAxisDialogFieldId.Minimum).AutomationId);
        AutomationProperties.SetAutomationId(_maximumBox, Field(ChartAxisDialogFieldId.Maximum).AutomationId);
        AutomationProperties.SetAutomationId(_majorUnitBox, Field(ChartAxisDialogFieldId.MajorUnit).AutomationId);
        AutomationProperties.SetAutomationId(_minorUnitBox, Field(ChartAxisDialogFieldId.MinorUnit).AutomationId);
        AutomationProperties.SetAutomationId(_logBox, Field(ChartAxisDialogFieldId.LogScale).AutomationId);
        AutomationProperties.SetAutomationId(_numberFormatBox, Field(ChartAxisDialogFieldId.NumberFormat).AutomationId);
        AutomationProperties.SetAutomationId(_majorGridBox, Field(ChartAxisDialogFieldId.MajorGridlines).AutomationId);
        AutomationProperties.SetAutomationId(_minorGridBox, Field(ChartAxisDialogFieldId.MinorGridlines).AutomationId);
        AutomationProperties.SetAutomationId(_majorGridColorBox, Field(ChartAxisDialogFieldId.MajorGridlineColor).AutomationId);
        AutomationProperties.SetAutomationId(_minorGridColorBox, Field(ChartAxisDialogFieldId.MinorGridlineColor).AutomationId);
        AutomationProperties.SetAutomationId(_gridlineThicknessBox, Field(ChartAxisDialogFieldId.GridlineThickness).AutomationId);
        AutomationProperties.SetAutomationId(_majorTickBox, Field(ChartAxisDialogFieldId.MajorTickMarks).AutomationId);
        AutomationProperties.SetAutomationId(_minorTickBox, Field(ChartAxisDialogFieldId.MinorTickMarks).AutomationId);
        AutomationProperties.SetAutomationId(_labelsBox, Field(ChartAxisDialogFieldId.ShowLabels).AutomationId);
        AutomationProperties.SetAutomationId(_labelColorBox, Field(ChartAxisDialogFieldId.LabelTextColor).AutomationId);
        AutomationProperties.SetAutomationId(_labelFontSizeBox, Field(ChartAxisDialogFieldId.LabelFontSize).AutomationId);
        AutomationProperties.SetAutomationId(_labelAngleBox, Field(ChartAxisDialogFieldId.LabelAngle).AutomationId);
        AutomationProperties.SetAutomationId(_lineColorBox, Field(ChartAxisDialogFieldId.LineColor).AutomationId);
        AutomationProperties.SetAutomationId(_lineThicknessBox, Field(ChartAxisDialogFieldId.LineThickness).AutomationId);
    }

    private static string LabelText(ChartAxisDialogFieldId id) =>
        UiText.Get(Field(id).LabelResourceKey);

    private static string HelpText(ChartAxisDialogFieldId id) =>
        UiText.Get(Field(id).HelpResourceKey ?? throw new InvalidOperationException($"Field {id} has no help resource key."));

    private static ChartAxisDialogFieldDescriptor Field(ChartAxisDialogFieldId id) =>
        ChartAxisPlanner.GetDialogField(id);
}
