using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

using static Freexcel.App.Host.ChartDialogInputParser;
using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

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
    private readonly CheckBox _logBox = new() { Content = "_Logarithmic scale" };
    private readonly ComboBox _numberFormatBox = new();
    private readonly CheckBox _majorGridBox = new() { Content = "_Major gridlines" };
    private readonly CheckBox _minorGridBox = new() { Content = "M_inor gridlines" };
    private readonly TextBox _majorGridColorBox = new();
    private readonly TextBox _minorGridColorBox = new();
    private readonly TextBox _gridlineThicknessBox = new();
    private readonly ComboBox _majorTickBox = new();
    private readonly ComboBox _minorTickBox = new();
    private readonly CheckBox _labelsBox = new() { Content = "Show _labels" };
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
        Title = useXAxis ? "Format X Axis" : "Format Y Axis";
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
            stack.Children.Add(CreateInlineHelp("Leave bounds blank for Auto."));
            ChartDialogHelpers.AddNumericText(stack, "_Minimum (blank for Auto)", _minimumBox, "Blank or Auto keeps the automatic minimum.");
            ChartDialogHelpers.AddNumericText(stack, "Ma_ximum (blank for Auto)", _maximumBox, "Blank or Auto keeps the automatic maximum.");
            ChartDialogHelpers.AddNumericText(stack, "Major _unit", _majorUnitBox, "Blank keeps the automatic major unit.");
            ChartDialogHelpers.AddNumericText(stack, "Minor u_nit", _minorUnitBox, "Blank keeps the automatic minor unit.");
            ChartDialogHelpers.AddCheck(stack, _logBox);
            ChartDialogHelpers.AddCombo(stack, "Number _format", _numberFormatBox, Enum.GetValues<ChartDataLabelNumberFormat>());
            root.Children.Add(CreateGroupBox("Axis Options", stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _majorGridBox);
            ChartDialogHelpers.AddCheck(stack, _minorGridBox);
            ChartDialogHelpers.AddColorText(stack, "_Major gridline color", _majorGridColorBox);
            ChartDialogHelpers.AddColorText(stack, "M_inor gridline color", _minorGridColorBox);
            ChartDialogHelpers.AddNumericText(stack, "Gridline _width", _gridlineThicknessBox, "Enter a gridline width in points.");
            root.Children.Add(CreateGroupBox("Gridlines", stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCombo(stack, "_Major tick marks", _majorTickBox, Enum.GetValues<ChartAxisTickStyle>());
            ChartDialogHelpers.AddCombo(stack, "M_inor tick marks", _minorTickBox, Enum.GetValues<ChartAxisTickStyle>());
            ChartDialogHelpers.AddCheck(stack, _labelsBox);
            ChartDialogHelpers.AddColorText(stack, "Label _color", _labelColorBox);
            ChartDialogHelpers.AddNumericText(stack, "Label _font size", _labelFontSizeBox, "Enter a font size in points.");
            ChartDialogHelpers.AddNumericText(stack, "Label _angle", _labelAngleBox, "Enter label rotation in degrees.");
            ChartDialogHelpers.AddColorText(stack, "Axis _line color", _lineColorBox);
            ChartDialogHelpers.AddNumericText(stack, "Axis line _width", _lineThicknessBox, "Enter an axis line width in points.");
            root.Children.Add(CreateGroupBox("Tick Marks", stack));
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
            ShowInvalidInputWarning("Enter a numeric minimum value or leave it blank.", _minimumBox);
            return;
        }

        if (!TryReadNullableDouble(_maximumBox, out var maximum))
        {
            ShowInvalidInputWarning("Enter a numeric maximum value or leave it blank.", _maximumBox);
            return;
        }

        if (!TryReadNullablePositiveDouble(_majorUnitBox, out var majorUnit))
        {
            ShowInvalidInputWarning("Enter a positive major unit or leave it blank.", _majorUnitBox);
            return;
        }

        if (!TryReadNullablePositiveDouble(_minorUnitBox, out var minorUnit))
        {
            ShowInvalidInputWarning("Enter a positive minor unit or leave it blank.", _minorUnitBox);
            return;
        }

        if (!TryReadOptionalColor(_majorGridColorBox, out var majorGridColor))
        {
            ShowInvalidInputWarning("Enter a color as #RRGGBB or none.", _majorGridColorBox);
            return;
        }

        if (!TryReadOptionalColor(_minorGridColorBox, out var minorGridColor))
        {
            ShowInvalidInputWarning("Enter a color as #RRGGBB or none.", _minorGridColorBox);
            return;
        }

        if (!TryReadPositiveDouble(_gridlineThicknessBox, out var gridlineThickness))
        {
            ShowInvalidInputWarning("Enter a positive gridline width.", _gridlineThicknessBox);
            return;
        }

        if (!TryReadOptionalColor(_labelColorBox, out var labelColor))
        {
            ShowInvalidInputWarning("Enter a color as #RRGGBB or none.", _labelColorBox);
            return;
        }

        if (!TryReadClampedDouble(_labelFontSizeBox, min: 6, max: 72, out var labelFontSize))
        {
            ShowInvalidInputWarning("Enter a label font size from 6 to 72 points.", _labelFontSizeBox);
            return;
        }

        if (!TryReadClampedDouble(_labelAngleBox, min: -90, max: 90, out var labelAngle))
        {
            ShowInvalidInputWarning("Enter a label angle from -90 to 90 degrees.", _labelAngleBox);
            return;
        }

        if (!TryReadOptionalColor(_lineColorBox, out var lineColor))
        {
            ShowInvalidInputWarning("Enter a color as #RRGGBB or none.", _lineColorBox);
            return;
        }

        if (!TryReadClampedDouble(_lineThicknessBox, min: 0.5, max: 10, out var lineThickness))
        {
            ShowInvalidInputWarning("Enter an axis line width from 0.5 to 10 points.", _lineThicknessBox);
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
