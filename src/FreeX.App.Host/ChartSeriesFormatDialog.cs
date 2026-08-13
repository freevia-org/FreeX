using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

public sealed class ChartSeriesFormatDialog : Window
{
    private readonly ComboBox _seriesBox = new();
    private readonly ComboBox _dashBox = new();
    private readonly ComboBox _markerBox = new();
    private readonly TextBox _fillBox = new();
    private readonly TextBox _strokeBox = new();
    private readonly TextBox _strokeThicknessBox = new();
    private readonly TextBox _markerSizeBox = new();

    public ChartSeriesFormatInput Result { get; private set; }

    public ChartSeriesFormatDialog(ChartModel chart, int seriesCount)
    {
        Result = ChartSeriesFormatPlanner.ReadDefault(chart);
        Title = UiText.Get("ChartSeriesFormat_Title");
        Width = 380;
        Height = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ApplyAutomationIds();
        Content = CreateContent(seriesCount);
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private StackPanel CreateContent(int seriesCount)
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var section = ChartSeriesFormatPlanner.GetSeriesOptionsSection();
            var stack = new StackPanel();
            ChartDialogHelpers.AddCombo(stack, LabelText(ChartSeriesFormatDialogFieldId.Series), _seriesBox, Enumerable.Range(0, Math.Max(1, seriesCount)).Select(index => UiText.Format("SelectDataSource_SeriesNameFormat", index + 1)).ToArray());
            if (section.HelpResourceKey is { } helpKey)
                stack.Children.Add(CreateInlineHelp(UiText.Get(helpKey)));
            root.Children.Add(CreateGroupBox(UiText.Get(section.HeaderResourceKey), stack));
        }
        {
            var section = ChartSeriesFormatPlanner.GetFillLineSection();
            var stack = new StackPanel();
            ChartDialogHelpers.AddColorText(stack, LabelText(ChartSeriesFormatDialogFieldId.FillColor), _fillBox);
            ChartDialogHelpers.AddColorText(stack, LabelText(ChartSeriesFormatDialogFieldId.StrokeColor), _strokeBox);
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartSeriesFormatDialogFieldId.StrokeThickness), _strokeThicknessBox, HelpText(ChartSeriesFormatDialogFieldId.StrokeThickness));
            ChartDialogHelpers.AddCombo(stack, LabelText(ChartSeriesFormatDialogFieldId.DashStyle), _dashBox, ChartSeriesFormatPlanner.GetDashStyleChoices().Cast<object>().Prepend(UiText.Get("Common_NoneParenthetical")).ToArray());
            ChartDialogHelpers.AddCombo(stack, LabelText(ChartSeriesFormatDialogFieldId.MarkerStyle), _markerBox, ChartSeriesFormatPlanner.GetMarkerStyleChoices().Cast<object>().Prepend(UiText.Get("Common_NoneParenthetical")).ToArray());
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartSeriesFormatDialogFieldId.MarkerSize), _markerSizeBox, HelpText(ChartSeriesFormatDialogFieldId.MarkerSize));
            root.Children.Add(CreateGroupBox(UiText.Get(section.HeaderResourceKey), stack));
        }
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartSeriesFormatInput result)
    {
        _seriesBox.SelectedIndex = Math.Min(result.SeriesIndex, Math.Max(0, _seriesBox.Items.Count - 1));
        _fillBox.Text = ChartDialogHelpers.FormatColor(result.FillColor);
        _strokeBox.Text = ChartDialogHelpers.FormatColor(result.StrokeColor);
        _strokeThicknessBox.Text = ChartDialogHelpers.FormatNullable(result.StrokeThickness);
        _dashBox.SelectedItem = result.DashStyle is null ? UiText.Get("Common_NoneParenthetical") : result.DashStyle.Value;
        _markerBox.SelectedItem = result.MarkerStyle is null ? UiText.Get("Common_NoneParenthetical") : result.MarkerStyle.Value;
        _markerSizeBox.Text = ChartDialogHelpers.FormatNullable(result.MarkerSize);
    }

    private void FocusInitialKeyboardTarget()
    {
        _seriesBox.Focus();
        Keyboard.Focus(_seriesBox);
    }

    private void Accept()
    {
        if (!ChartSeriesFormatPlanner.TryParseDialogInput(
                _seriesBox.SelectedIndex < 0 ? 0 : _seriesBox.SelectedIndex,
                _fillBox.Text,
                _strokeBox.Text,
                _strokeThicknessBox.Text,
                SelectedDashStyle(),
                SelectedMarkerStyle(),
                _markerSizeBox.Text,
                out var input,
                out var issue))
        {
            ShowPlannerParseWarning(issue);
            return;
        }

        Result = input;
        DialogResult = true;
    }

    private ChartLineDashStyle? SelectedDashStyle() =>
        _dashBox.SelectedItem is ChartLineDashStyle value ? value : null;

    private ChartMarkerStyle? SelectedMarkerStyle() =>
        _markerBox.SelectedItem is ChartMarkerStyle value ? value : null;

    private void ShowPlannerParseWarning(ChartSeriesFormatParseIssue issue)
    {
        var presentation = ChartValidationPresentationPlanner.Describe(issue);
        var target = presentation.FocusTarget switch
        {
            ChartSeriesFormatDialogFieldId.StrokeColor => _strokeBox,
            ChartSeriesFormatDialogFieldId.StrokeThickness => _strokeThicknessBox,
            ChartSeriesFormatDialogFieldId.MarkerSize => _markerSizeBox,
            _ => _fillBox
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
        AutomationProperties.SetAutomationId(_seriesBox, Field(ChartSeriesFormatDialogFieldId.Series).AutomationId);
        AutomationProperties.SetAutomationId(_fillBox, Field(ChartSeriesFormatDialogFieldId.FillColor).AutomationId);
        AutomationProperties.SetAutomationId(_strokeBox, Field(ChartSeriesFormatDialogFieldId.StrokeColor).AutomationId);
        AutomationProperties.SetAutomationId(_strokeThicknessBox, Field(ChartSeriesFormatDialogFieldId.StrokeThickness).AutomationId);
        AutomationProperties.SetAutomationId(_dashBox, Field(ChartSeriesFormatDialogFieldId.DashStyle).AutomationId);
        AutomationProperties.SetAutomationId(_markerBox, Field(ChartSeriesFormatDialogFieldId.MarkerStyle).AutomationId);
        AutomationProperties.SetAutomationId(_markerSizeBox, Field(ChartSeriesFormatDialogFieldId.MarkerSize).AutomationId);
    }

    private static string LabelText(ChartSeriesFormatDialogFieldId id) =>
        UiText.Get(Field(id).LabelResourceKey);

    private static string HelpText(ChartSeriesFormatDialogFieldId id) =>
        UiText.Get(Field(id).HelpResourceKey ?? throw new InvalidOperationException($"Field {id} has no help resource key."));

    private static ChartSeriesFormatDialogFieldDescriptor Field(ChartSeriesFormatDialogFieldId id) =>
        ChartSeriesFormatPlanner.GetDialogField(id);
}
