using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

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
    public ChartAreaFormatInput ToInput() =>
        new(
            ChartAreaFillColor,
            PlotAreaFillColor,
            PlotAreaBorderColor,
            PlotAreaBorderThickness,
            ShowLegend,
            LegendPosition,
            LegendOverlay,
            LegendTextColor,
            LegendFillColor,
            LegendBorderColor,
            LegendBorderThickness,
            LegendFontSize);

    public ChartLayoutOptions ToOptions() => ChartAreaFormatPlanner.Plan(ToInput());
}

public sealed class ChartAreaLegendDialog : Window
{
    private readonly TextBox _chartAreaFillBox = new();
    private readonly TextBox _plotAreaFillBox = new();
    private readonly TextBox _plotAreaBorderBox = new();
    private readonly TextBox _plotAreaBorderThicknessBox = new();
    private readonly CheckBox _showLegendBox = new() { Content = LabelText(ChartAreaFormatDialogFieldId.ShowLegend) };
    private readonly ComboBox _legendPositionBox = new();
    private readonly CheckBox _legendOverlayBox = new() { Content = LabelText(ChartAreaFormatDialogFieldId.LegendOverlay) };
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
        Width = ChartAreaFormatPlanner.DialogWidth;
        Height = ChartAreaFormatPlanner.DialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ApplyAutomationIds();
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static ChartAreaLegendDialogResult FromChart(ChartModel chart)
    {
        var input = ChartAreaFormatPlanner.Read(chart);
        return CreateResult(
            input.ChartAreaFillColor,
            input.PlotAreaFillColor,
            input.PlotAreaBorderColor,
            input.PlotAreaBorderThickness,
            input.ShowLegend,
            input.LegendPosition,
            input.LegendOverlay,
            input.LegendTextColor,
            input.LegendFillColor,
            input.LegendBorderColor,
            input.LegendBorderThickness,
            input.LegendFontSize);
    }

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
        double legendFontSize)
    {
        var input = ChartAreaFormatPlanner.Normalize(new ChartAreaFormatInput(
            chartAreaFillColor,
            plotAreaFillColor,
            plotAreaBorderColor,
            plotAreaBorderThickness,
            showLegend,
            legendPosition,
            legendOverlay,
            legendTextColor,
            legendFillColor,
            legendBorderColor,
            legendBorderThickness,
            legendFontSize));
        return new(
            input.ChartAreaFillColor,
            input.PlotAreaFillColor,
            input.PlotAreaBorderColor,
            input.PlotAreaBorderThickness,
            input.ShowLegend,
            input.LegendPosition,
            input.LegendOverlay,
            input.LegendTextColor,
            input.LegendFillColor,
            input.LegendBorderColor,
            input.LegendBorderThickness,
            input.LegendFontSize);
    }

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var section = ChartAreaFormatPlanner.GetFillLineSection();
            var stack = new StackPanel();
            if (section.HelpResourceKey is { } helpKey)
                stack.Children.Add(CreateInlineHelp(UiText.Get(helpKey)));
            ChartDialogHelpers.AddColorText(stack, LabelText(ChartAreaFormatDialogFieldId.ChartAreaFillColor), _chartAreaFillBox);
            ChartDialogHelpers.AddColorText(stack, LabelText(ChartAreaFormatDialogFieldId.PlotAreaFillColor), _plotAreaFillBox);
            ChartDialogHelpers.AddColorText(stack, LabelText(ChartAreaFormatDialogFieldId.PlotAreaBorderColor), _plotAreaBorderBox);
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartAreaFormatDialogFieldId.PlotAreaBorderThickness), _plotAreaBorderThicknessBox, HelpText(ChartAreaFormatDialogFieldId.PlotAreaBorderThickness));
            root.Children.Add(CreateGroupBox(UiText.Get(section.HeaderResourceKey), stack));
        }
        {
            var section = ChartAreaFormatPlanner.GetLegendSection();
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _showLegendBox);
            ChartDialogHelpers.AddCombo(stack, LabelText(ChartAreaFormatDialogFieldId.LegendPosition), _legendPositionBox, ChartAreaFormatPlanner.GetLegendPositionChoices());
            ChartDialogHelpers.AddCheck(stack, _legendOverlayBox);
            ChartDialogHelpers.AddColorText(stack, LabelText(ChartAreaFormatDialogFieldId.LegendTextColor), _legendTextBox);
            ChartDialogHelpers.AddColorText(stack, LabelText(ChartAreaFormatDialogFieldId.LegendFillColor), _legendFillBox);
            ChartDialogHelpers.AddColorText(stack, LabelText(ChartAreaFormatDialogFieldId.LegendBorderColor), _legendBorderBox);
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartAreaFormatDialogFieldId.LegendBorderThickness), _legendBorderThicknessBox, HelpText(ChartAreaFormatDialogFieldId.LegendBorderThickness));
            ChartDialogHelpers.AddNumericText(stack, LabelText(ChartAreaFormatDialogFieldId.LegendFontSize), _legendFontSizeBox, HelpText(ChartAreaFormatDialogFieldId.LegendFontSize));
            root.Children.Add(CreateGroupBox(UiText.Get(section.HeaderResourceKey), stack));
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
        if (!ChartAreaFormatPlanner.TryParseDialogInput(
                _chartAreaFillBox.Text,
                _plotAreaFillBox.Text,
                _plotAreaBorderBox.Text,
                _plotAreaBorderThicknessBox.Text,
                _showLegendBox.IsChecked == true,
                SelectedLegendPosition(),
                _legendOverlayBox.IsChecked == true,
                _legendTextBox.Text,
                _legendFillBox.Text,
                _legendBorderBox.Text,
                _legendBorderThicknessBox.Text,
                _legendFontSizeBox.Text,
                out var input,
                out var issue))
        {
            ShowPlannerParseWarning(issue);
            return;
        }

        Result = CreateResult(
            input.ChartAreaFillColor,
            input.PlotAreaFillColor,
            input.PlotAreaBorderColor,
            input.PlotAreaBorderThickness,
            input.ShowLegend,
            input.LegendPosition,
            input.LegendOverlay,
            input.LegendTextColor,
            input.LegendFillColor,
            input.LegendBorderColor,
            input.LegendBorderThickness,
            input.LegendFontSize);
        DialogResult = true;
    }

    private ChartLegendPosition? SelectedLegendPosition() =>
        _legendPositionBox.SelectedItem is ChartLegendPosition value ? value : null;

    private void ApplyAutomationIds()
    {
        AutomationProperties.SetAutomationId(_chartAreaFillBox, Field(ChartAreaFormatDialogFieldId.ChartAreaFillColor).AutomationId);
        AutomationProperties.SetAutomationId(_plotAreaFillBox, Field(ChartAreaFormatDialogFieldId.PlotAreaFillColor).AutomationId);
        AutomationProperties.SetAutomationId(_plotAreaBorderBox, Field(ChartAreaFormatDialogFieldId.PlotAreaBorderColor).AutomationId);
        AutomationProperties.SetAutomationId(_plotAreaBorderThicknessBox, Field(ChartAreaFormatDialogFieldId.PlotAreaBorderThickness).AutomationId);
        AutomationProperties.SetAutomationId(_showLegendBox, Field(ChartAreaFormatDialogFieldId.ShowLegend).AutomationId);
        AutomationProperties.SetAutomationId(_legendPositionBox, Field(ChartAreaFormatDialogFieldId.LegendPosition).AutomationId);
        AutomationProperties.SetAutomationId(_legendOverlayBox, Field(ChartAreaFormatDialogFieldId.LegendOverlay).AutomationId);
        AutomationProperties.SetAutomationId(_legendTextBox, Field(ChartAreaFormatDialogFieldId.LegendTextColor).AutomationId);
        AutomationProperties.SetAutomationId(_legendFillBox, Field(ChartAreaFormatDialogFieldId.LegendFillColor).AutomationId);
        AutomationProperties.SetAutomationId(_legendBorderBox, Field(ChartAreaFormatDialogFieldId.LegendBorderColor).AutomationId);
        AutomationProperties.SetAutomationId(_legendBorderThicknessBox, Field(ChartAreaFormatDialogFieldId.LegendBorderThickness).AutomationId);
        AutomationProperties.SetAutomationId(_legendFontSizeBox, Field(ChartAreaFormatDialogFieldId.LegendFontSize).AutomationId);
    }

    private static string LabelText(ChartAreaFormatDialogFieldId id) =>
        UiText.Get(Field(id).LabelResourceKey);

    private static string HelpText(ChartAreaFormatDialogFieldId id) =>
        UiText.Get(Field(id).HelpResourceKey ?? throw new InvalidOperationException($"Field {id} has no help resource key."));

    private static ChartAreaFormatDialogFieldDescriptor Field(ChartAreaFormatDialogFieldId id) =>
        ChartAreaFormatPlanner.GetDialogField(id);

    private void ShowPlannerParseWarning(ChartAreaFormatParseIssue issue)
    {
        var presentation = ChartValidationPresentationPlanner.Describe(issue);
        var target = presentation.FocusTarget switch
        {
            ChartAreaFormatDialogFieldId.PlotAreaFillColor => _plotAreaFillBox,
            ChartAreaFormatDialogFieldId.PlotAreaBorderColor => _plotAreaBorderBox,
            ChartAreaFormatDialogFieldId.PlotAreaBorderThickness => _plotAreaBorderThicknessBox,
            ChartAreaFormatDialogFieldId.LegendTextColor => _legendTextBox,
            ChartAreaFormatDialogFieldId.LegendFillColor => _legendFillBox,
            ChartAreaFormatDialogFieldId.LegendBorderColor => _legendBorderBox,
            ChartAreaFormatDialogFieldId.LegendBorderThickness => _legendBorderThicknessBox,
            ChartAreaFormatDialogFieldId.LegendFontSize => _legendFontSizeBox,
            _ => _chartAreaFillBox
        };
        ShowInvalidInputWarning(presentation.Message.Resolve(UiText.Get, UiText.Format), target);
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        return true;
    }
}

