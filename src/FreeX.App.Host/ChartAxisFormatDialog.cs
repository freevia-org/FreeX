using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogInputParser;
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
    public ChartLayoutOptions ToOptions() => UseXAxis
        ? new ChartLayoutOptions(
            XAxisMinimum: Minimum,
            XAxisMaximum: Maximum,
            XAxisMajorUnit: MajorUnit,
            XAxisMinorUnit: MinorUnit,
            XAxisLogScale: LogScale,
            XAxisNumberFormat: NumberFormat,
            ShowXAxisMajorGridlines: ShowMajorGridlines,
            ShowXAxisMinorGridlines: ShowMinorGridlines,
            XAxisMajorGridlineColor: MajorGridlineColor,
            XAxisMinorGridlineColor: MinorGridlineColor,
            XAxisGridlineThickness: GridlineThickness,
            XAxisMajorTickStyle: MajorTickStyle,
            XAxisMinorTickStyle: MinorTickStyle,
            ShowXAxisLabels: ShowLabels,
            XAxisLabelTextColor: LabelTextColor,
            XAxisLabelFontSize: LabelFontSize,
            XAxisLabelAngle: LabelAngle,
            XAxisLineColor: LineColor,
            XAxisLineThickness: LineThickness,
            ClearXAxisBounds: Minimum is null && Maximum is null)
        : new ChartLayoutOptions(
            YAxisMinimum: Minimum,
            YAxisMaximum: Maximum,
            YAxisMajorUnit: MajorUnit,
            YAxisMinorUnit: MinorUnit,
            YAxisLogScale: LogScale,
            YAxisNumberFormat: NumberFormat,
            ShowYAxisMajorGridlines: ShowMajorGridlines,
            ShowYAxisMinorGridlines: ShowMinorGridlines,
            YAxisMajorGridlineColor: MajorGridlineColor,
            YAxisMinorGridlineColor: MinorGridlineColor,
            YAxisGridlineThickness: GridlineThickness,
            YAxisMajorTickStyle: MajorTickStyle,
            YAxisMinorTickStyle: MinorTickStyle,
            ShowYAxisLabels: ShowLabels,
            YAxisLabelTextColor: LabelTextColor,
            YAxisLabelFontSize: LabelFontSize,
            YAxisLabelAngle: LabelAngle,
            YAxisLineColor: LineColor,
            YAxisLineThickness: LineThickness,
            ClearYAxisBounds: Minimum is null && Maximum is null);
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

    public static ChartAxisFormatDialogResult FromChart(ChartModel chart, bool useXAxis) => useXAxis
        ? CreateResult(true, chart.XAxisMinimum, chart.XAxisMaximum, chart.XAxisMajorUnit, chart.XAxisMinorUnit,
            chart.XAxisLogScale, chart.XAxisNumberFormat, chart.ShowXAxisMajorGridlines, chart.ShowXAxisMinorGridlines,
            chart.XAxisMajorGridlineColor, chart.XAxisMinorGridlineColor, chart.XAxisGridlineThickness,
            chart.XAxisMajorTickStyle, chart.XAxisMinorTickStyle, chart.ShowXAxisLabels, chart.XAxisLabelTextColor,
            chart.XAxisLabelFontSize, chart.XAxisLabelAngle, chart.XAxisLineColor, chart.XAxisLineThickness)
        : CreateResult(false, chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisMajorUnit, chart.YAxisMinorUnit,
            chart.YAxisLogScale, chart.YAxisNumberFormat, chart.ShowYAxisMajorGridlines, chart.ShowYAxisMinorGridlines,
            chart.YAxisMajorGridlineColor, chart.YAxisMinorGridlineColor, chart.YAxisGridlineThickness,
            chart.YAxisMajorTickStyle, chart.YAxisMinorTickStyle, chart.ShowYAxisLabels, chart.YAxisLabelTextColor,
            chart.YAxisLabelFontSize, chart.YAxisLabelAngle, chart.YAxisLineColor, chart.YAxisLineThickness);

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
        double lineThickness) =>
        new(useXAxis, minimum, maximum, majorUnit, minorUnit, logScale, numberFormat, showMajorGridlines,
            showMinorGridlines, majorGridlineColor, minorGridlineColor, gridlineThickness, majorTickStyle,
            minorTickStyle, showLabels, labelTextColor, labelFontSize, labelAngle, lineColor, lineThickness);

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
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartAxisFormat_NumberFormatLabel"), _numberFormatBox, Enum.GetValues<ChartDataLabelNumberFormat>());
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
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartAxisFormat_MajorTickMarksLabel"), _majorTickBox, Enum.GetValues<ChartAxisTickStyle>());
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartAxisFormat_MinorTickMarksLabel"), _minorTickBox, Enum.GetValues<ChartAxisTickStyle>());
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
        if (!TryReadNullableDouble(_minimumBox, out var minimum))
        {
            ShowInvalidInputWarning(UiText.Get("ChartAxisFormat_InvalidMinimumMessage"), _minimumBox);
            return;
        }

        if (!TryReadNullableDouble(_maximumBox, out var maximum))
        {
            ShowInvalidInputWarning(UiText.Get("ChartAxisFormat_InvalidMaximumMessage"), _maximumBox);
            return;
        }

        if (!TryReadNullablePositiveDouble(_majorUnitBox, out var majorUnit))
        {
            ShowInvalidInputWarning(UiText.Get("ChartAxisFormat_InvalidMajorUnitMessage"), _majorUnitBox);
            return;
        }

        if (!TryReadNullablePositiveDouble(_minorUnitBox, out var minorUnit))
        {
            ShowInvalidInputWarning(UiText.Get("ChartAxisFormat_InvalidMinorUnitMessage"), _minorUnitBox);
            return;
        }

        if (!TryReadOptionalColor(_majorGridColorBox, out var majorGridColor))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _majorGridColorBox);
            return;
        }

        if (!TryReadOptionalColor(_minorGridColorBox, out var minorGridColor))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _minorGridColorBox);
            return;
        }

        if (!TryReadPositiveDouble(_gridlineThicknessBox, out var gridlineThickness))
        {
            ShowInvalidInputWarning(UiText.Get("ChartAxisFormat_InvalidGridlineWidthMessage"), _gridlineThicknessBox);
            return;
        }

        if (!TryReadOptionalColor(_labelColorBox, out var labelColor))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _labelColorBox);
            return;
        }

        if (!TryReadClampedDouble(_labelFontSizeBox, min: 6, max: 72, out var labelFontSize))
        {
            ShowInvalidInputWarning(UiText.Get("ChartAxisFormat_InvalidLabelFontSizeMessage"), _labelFontSizeBox);
            return;
        }

        if (!TryReadClampedDouble(_labelAngleBox, min: -90, max: 90, out var labelAngle))
        {
            ShowInvalidInputWarning(UiText.Get("ChartAxisFormat_InvalidLabelAngleMessage"), _labelAngleBox);
            return;
        }

        if (!TryReadOptionalColor(_lineColorBox, out var lineColor))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _lineColorBox);
            return;
        }

        if (!TryReadClampedDouble(_lineThicknessBox, min: 0.5, max: 10, out var lineThickness))
        {
            ShowInvalidInputWarning(UiText.Get("ChartAxisFormat_InvalidAxisLineWidthMessage"), _lineThicknessBox);
            return;
        }

        Result = CreateResult(
            _useXAxis,
            minimum,
            maximum,
            majorUnit,
            minorUnit,
            _logBox.IsChecked == true,
            ChartDialogHelpers.Selected(_numberFormatBox, ChartDataLabelNumberFormat.General),
            _majorGridBox.IsChecked == true,
            _minorGridBox.IsChecked == true,
            majorGridColor,
            minorGridColor,
            gridlineThickness,
            ChartDialogHelpers.Selected(_majorTickBox, ChartAxisTickStyle.Outside),
            ChartDialogHelpers.Selected(_minorTickBox, ChartAxisTickStyle.None),
            _labelsBox.IsChecked == true,
            labelColor,
            labelFontSize,
            labelAngle,
            lineColor,
            lineThickness);
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
