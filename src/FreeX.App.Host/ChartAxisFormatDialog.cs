using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

public sealed record ChartAxisFormatDialogResult(
    bool UseXAxis,
    double? Minimum,
    double? Maximum,
    double? MajorUnit,
    double? MinorUnit,
    bool LogScale,
    ChartDataLabelNumberFormat NumberFormat,
    bool ShowMajorGridlines,
    bool ShowMinorGridlines,
    CellColor? MajorGridlineColor,
    CellColor? MinorGridlineColor,
    double GridlineThickness,
    ChartAxisTickStyle MajorTickStyle,
    ChartAxisTickStyle MinorTickStyle,
    bool ShowLabels,
    CellColor? LabelTextColor,
    double LabelFontSize,
    double LabelAngle,
    CellColor? LineColor,
    double LineThickness)
{
    public ChartAxisInput ToInput() =>
        new(
            UseXAxis: UseXAxis,
            Minimum: Minimum,
            Maximum: Maximum,
            MajorUnit: MajorUnit,
            MinorUnit: MinorUnit,
            LogScale: LogScale,
            NumberFormat: NumberFormat,
            ShowMajorGridlines: ShowMajorGridlines,
            ShowMinorGridlines: ShowMinorGridlines,
            MajorGridlineColor: MajorGridlineColor,
            MinorGridlineColor: MinorGridlineColor,
            GridlineThickness: GridlineThickness,
            MajorTickStyle: MajorTickStyle,
            MinorTickStyle: MinorTickStyle,
            ShowLabels: ShowLabels,
            LabelTextColor: LabelTextColor,
            LabelFontSize: LabelFontSize,
            LabelAngle: LabelAngle,
            LineColor: LineColor,
            LineThickness: LineThickness);

    public ChartLayoutOptions ToOptions() => ChartAxisPlanner.Plan(ToInput());
}

public sealed class ChartAxisFormatDialog : Window
{
    private readonly bool _useXAxis;
    private readonly TextBox _minimumBox = new();
    private readonly TextBox _maximumBox = new();
    private readonly TextBox _majorUnitBox = new();
    private readonly TextBox _minorUnitBox = new();
    private readonly CheckBox _logBox = new() { Content = UiText.Get("ChartAxisFormat_LogScale") };
    private readonly ComboBox _numberFormatBox = new();
    private readonly CheckBox _majorGridBox = new() { Content = UiText.Get("ChartAxisFormat_MajorGridlines") };
    private readonly CheckBox _minorGridBox = new() { Content = UiText.Get("ChartAxisFormat_MinorGridlines") };
    private readonly TextBox _majorGridColorBox = new();
    private readonly TextBox _minorGridColorBox = new();
    private readonly TextBox _gridlineThicknessBox = new();
    private readonly ComboBox _majorTickBox = new();
    private readonly ComboBox _minorTickBox = new();
    private readonly CheckBox _labelsBox = new() { Content = UiText.Get("ChartAxisFormat_ShowLabels") };
    private readonly TextBox _labelColorBox = new();
    private readonly TextBox _labelFontSizeBox = new();
    private readonly TextBox _labelAngleBox = new();
    private readonly TextBox _lineColorBox = new();
    private readonly TextBox _lineThicknessBox = new();

    public ChartAxisFormatDialogResult Result { get; private set; }

    public ChartAxisFormatDialog(ChartModel chart, bool useXAxis)
    {
        _useXAxis = useXAxis;
        Result = FromChart(chart, useXAxis);
        Title = useXAxis ? UiText.Get("ChartAxisFormat_XAxisTitle") : UiText.Get("ChartAxisFormat_YAxisTitle");
        Width = 430;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static ChartAxisFormatDialogResult FromChart(ChartModel chart, bool useXAxis)
    {
        var input = ChartAxisPlanner.Read(chart, useXAxis);
        return CreateResult(
            input.UseXAxis,
            input.Minimum,
            input.Maximum,
            input.MajorUnit,
            input.MinorUnit,
            input.LogScale,
            input.NumberFormat,
            input.ShowMajorGridlines,
            input.ShowMinorGridlines,
            input.MajorGridlineColor,
            input.MinorGridlineColor,
            input.GridlineThickness ?? 1,
            input.MajorTickStyle ?? ChartAxisTickStyle.Outside,
            input.MinorTickStyle ?? ChartAxisTickStyle.None,
            input.ShowLabels ?? true,
            input.LabelTextColor,
            input.LabelFontSize ?? 11,
            input.LabelAngle ?? 0,
            input.LineColor,
            input.LineThickness ?? 1);
    }

    public static ChartAxisFormatDialogResult CreateResult(
        bool useXAxis,
        double? minimum,
        double? maximum,
        double? majorUnit,
        double? minorUnit,
        bool logScale,
        ChartDataLabelNumberFormat numberFormat,
        bool showMajorGridlines,
        bool showMinorGridlines,
        CellColor? majorGridlineColor,
        CellColor? minorGridlineColor,
        double gridlineThickness,
        ChartAxisTickStyle majorTickStyle,
        ChartAxisTickStyle minorTickStyle,
        bool showLabels,
        CellColor? labelTextColor,
        double labelFontSize,
        double labelAngle,
        CellColor? lineColor,
        double lineThickness)
    {
        var input = ChartAxisPlanner.Normalize(new ChartAxisInput(
            UseXAxis: useXAxis,
            Minimum: minimum,
            Maximum: maximum,
            MajorUnit: majorUnit,
            MinorUnit: minorUnit,
            LogScale: logScale,
            NumberFormat: numberFormat,
            ShowMajorGridlines: showMajorGridlines,
            ShowMinorGridlines: showMinorGridlines,
            MajorGridlineColor: majorGridlineColor,
            MinorGridlineColor: minorGridlineColor,
            GridlineThickness: gridlineThickness,
            MajorTickStyle: majorTickStyle,
            MinorTickStyle: minorTickStyle,
            ShowLabels: showLabels,
            LabelTextColor: labelTextColor,
            LabelFontSize: labelFontSize,
            LabelAngle: labelAngle,
            LineColor: lineColor,
            LineThickness: lineThickness));
        return new(
            input.UseXAxis,
            input.Minimum,
            input.Maximum,
            input.MajorUnit,
            input.MinorUnit,
            input.LogScale,
            input.NumberFormat,
            input.ShowMajorGridlines,
            input.ShowMinorGridlines,
            input.MajorGridlineColor,
            input.MinorGridlineColor,
            input.GridlineThickness ?? 1,
            input.MajorTickStyle ?? ChartAxisTickStyle.Outside,
            input.MinorTickStyle ?? ChartAxisTickStyle.None,
            input.ShowLabels ?? true,
            input.LabelTextColor,
            input.LabelFontSize ?? 11,
            input.LabelAngle ?? 0,
            input.LineColor,
            input.LineThickness ?? 1);
    }

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var stack = new StackPanel();
            stack.Children.Add(CreateInlineHelp(UiText.Get("ChartAxisFormat_BoundsHelpText")));
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartAxisFormat_MinimumLabel"), _minimumBox, UiText.Get("ChartAxisFormat_MinimumHelpText"));
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartAxisFormat_MaximumLabel"), _maximumBox, UiText.Get("ChartAxisFormat_MaximumHelpText"));
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartAxisFormat_MajorUnitLabel"), _majorUnitBox, UiText.Get("ChartAxisFormat_MajorUnitHelpText"));
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartAxisFormat_MinorUnitLabel"), _minorUnitBox, UiText.Get("ChartAxisFormat_MinorUnitHelpText"));
            ChartDialogHelpers.AddCheck(stack, _logBox);
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartAxisFormat_NumberFormatLabel"), _numberFormatBox, ChartAxisPlanner.GetNumberFormatChoices().Select(choice => choice.NumberFormat));
            root.Children.Add(CreateGroupBox(UiText.Get("ChartAxisFormat_AxisOptionsGroup"), stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _majorGridBox);
            ChartDialogHelpers.AddCheck(stack, _minorGridBox);
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartAxisFormat_MajorGridlineColorLabel"), _majorGridColorBox);
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartAxisFormat_MinorGridlineColorLabel"), _minorGridColorBox);
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartAxisFormat_GridlineWidthLabel"), _gridlineThicknessBox, UiText.Get("ChartAxisFormat_GridlineWidthHelpText"));
            root.Children.Add(CreateGroupBox(UiText.Get("ChartAxisFormat_GridlinesGroup"), stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartAxisFormat_MajorTickMarksLabel"), _majorTickBox, ChartAxisPlanner.GetTickStyleChoices());
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartAxisFormat_MinorTickMarksLabel"), _minorTickBox, ChartAxisPlanner.GetTickStyleChoices());
            ChartDialogHelpers.AddCheck(stack, _labelsBox);
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartAxisFormat_LabelColorLabel"), _labelColorBox);
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartAxisFormat_LabelFontSizeLabel"), _labelFontSizeBox, UiText.Get("ChartAxisFormat_LabelFontSizeHelpText"));
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartAxisFormat_LabelAngleLabel"), _labelAngleBox, UiText.Get("ChartAxisFormat_LabelAngleHelpText"));
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartAxisFormat_AxisLineColorLabel"), _lineColorBox);
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartAxisFormat_AxisLineWidthLabel"), _lineThicknessBox, UiText.Get("ChartAxisFormat_AxisLineWidthHelpText"));
            root.Children.Add(CreateGroupBox(UiText.Get("ChartAxisFormat_TickMarksGroup"), stack));
        }
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartAxisFormatDialogResult result)
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
        _gridlineThicknessBox.Text = result.GridlineThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _majorTickBox.SelectedItem = result.MajorTickStyle;
        _minorTickBox.SelectedItem = result.MinorTickStyle;
        _labelsBox.IsChecked = result.ShowLabels;
        _labelColorBox.Text = ChartDialogHelpers.FormatColor(result.LabelTextColor);
        _labelFontSizeBox.Text = result.LabelFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _labelAngleBox.Text = result.LabelAngle.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _lineColorBox.Text = ChartDialogHelpers.FormatColor(result.LineColor);
        _lineThicknessBox.Text = result.LineThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
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

        Result = CreateResult(
            input.UseXAxis,
            input.Minimum,
            input.Maximum,
            input.MajorUnit,
            input.MinorUnit,
            input.LogScale,
            input.NumberFormat,
            input.ShowMajorGridlines,
            input.ShowMinorGridlines,
            input.MajorGridlineColor,
            input.MinorGridlineColor,
            input.GridlineThickness ?? 1,
            input.MajorTickStyle ?? ChartAxisTickStyle.Outside,
            input.MinorTickStyle ?? ChartAxisTickStyle.None,
            input.ShowLabels ?? true,
            input.LabelTextColor,
            input.LabelFontSize ?? 11,
            input.LabelAngle ?? 0,
            input.LineColor,
            input.LineThickness ?? 1);
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
        var (message, target) = issue switch
        {
            ChartAxisFormatParseIssue.Maximum => (UiText.Get("ChartAxisFormat_InvalidMaximumMessage"), _maximumBox),
            ChartAxisFormatParseIssue.MajorUnit => (UiText.Get("ChartAxisFormat_InvalidMajorUnitMessage"), _majorUnitBox),
            ChartAxisFormatParseIssue.MinorUnit => (UiText.Get("ChartAxisFormat_InvalidMinorUnitMessage"), _minorUnitBox),
            ChartAxisFormatParseIssue.MajorGridlineColor => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _majorGridColorBox),
            ChartAxisFormatParseIssue.MinorGridlineColor => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _minorGridColorBox),
            ChartAxisFormatParseIssue.GridlineThickness => (UiText.Get("ChartAxisFormat_InvalidGridlineWidthMessage"), _gridlineThicknessBox),
            ChartAxisFormatParseIssue.LabelTextColor => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _labelColorBox),
            ChartAxisFormatParseIssue.LabelFontSize => (UiText.Get("ChartAxisFormat_InvalidLabelFontSizeMessage"), _labelFontSizeBox),
            ChartAxisFormatParseIssue.LabelAngle => (UiText.Get("ChartAxisFormat_InvalidLabelAngleMessage"), _labelAngleBox),
            ChartAxisFormatParseIssue.LineColor => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _lineColorBox),
            ChartAxisFormatParseIssue.LineThickness => (UiText.Get("ChartAxisFormat_InvalidAxisLineWidthMessage"), _lineThicknessBox),
            _ => (UiText.Get("ChartAxisFormat_InvalidMinimumMessage"), _minimumBox),
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
