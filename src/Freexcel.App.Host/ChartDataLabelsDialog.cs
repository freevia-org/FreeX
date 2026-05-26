using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

public sealed record ChartDataLabelsDialogResult(
    bool ShowDataLabels,
    ChartDataLabelPosition Position,
    bool ShowCategoryName,
    bool ShowSeriesName,
    bool ShowPercentage,
    ChartDataLabelSeparator Separator,
    ChartDataLabelNumberFormat NumberFormat,
    bool ShowCallouts,
    CellColor? FillColor,
    CellColor? BorderColor,
    CellColor? TextColor,
    double BorderThickness,
    double FontSize,
    double Angle)
{
    public ChartLayoutOptions ToOptions() => new(
        ShowDataLabels: ShowDataLabels,
        DataLabelPosition: Position,
        ShowDataLabelCategoryName: ShowCategoryName,
        ShowDataLabelSeriesName: ShowSeriesName,
        ShowDataLabelPercentage: ShowPercentage,
        DataLabelSeparator: Separator,
        DataLabelNumberFormat: NumberFormat,
        ShowDataLabelCallouts: ShowCallouts,
        DataLabelFillColor: FillColor,
        DataLabelBorderColor: BorderColor,
        DataLabelTextColor: TextColor,
        DataLabelBorderThickness: BorderThickness,
        DataLabelFontSize: FontSize,
        DataLabelAngle: Angle);
}

public sealed class ChartDataLabelsDialog : Window
{
    private readonly CheckBox _showBox = new() { Content = "_Show data labels" };
    private readonly CheckBox _categoryBox = new() { Content = "_Category name" };
    private readonly CheckBox _seriesBox = new() { Content = "_Series name" };
    private readonly CheckBox _percentageBox = new() { Content = "_Percentage" };
    private readonly CheckBox _calloutsBox = new() { Content = "Data label _callouts" };
    private readonly ComboBox _positionBox = new();
    private readonly ComboBox _separatorBox = new();
    private readonly ComboBox _numberFormatBox = new();
    private readonly TextBox _fillBox = new();
    private readonly TextBox _borderBox = new();
    private readonly TextBox _textBox = new();
    private readonly TextBox _borderThicknessBox = new();
    private readonly TextBox _fontSizeBox = new();
    private readonly TextBox _angleBox = new();

    public ChartDataLabelsDialogResult Result { get; private set; }

    public ChartDataLabelsDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "Format Data Labels";
        Width = 420;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static ChartDataLabelsDialogResult FromChart(ChartModel chart) => CreateResult(
        chart.ShowDataLabels,
        chart.DataLabelPosition,
        chart.ShowDataLabelCategoryName,
        chart.ShowDataLabelSeriesName,
        chart.ShowDataLabelPercentage,
        chart.DataLabelSeparator,
        chart.DataLabelNumberFormat,
        chart.ShowDataLabelCallouts,
        chart.DataLabelFillColor,
        chart.DataLabelBorderColor,
        chart.DataLabelTextColor,
        chart.DataLabelBorderThickness,
        chart.DataLabelFontSize,
        chart.DataLabelAngle);

    public static ChartDataLabelsDialogResult CreateResult(
        bool showDataLabels,
        ChartDataLabelPosition position,
        bool showCategoryName,
        bool showSeriesName,
        bool showPercentage,
        ChartDataLabelSeparator separator,
        ChartDataLabelNumberFormat numberFormat,
        bool showCallouts,
        CellColor? fillColor,
        CellColor? borderColor,
        CellColor? textColor,
        double borderThickness,
        double fontSize,
        double angle) =>
        new(showDataLabels, position, showCategoryName, showSeriesName, showPercentage, separator, numberFormat,
            showCallouts, fillColor, borderColor, textColor, borderThickness, fontSize, angle);

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _showBox);
            ChartDialogHelpers.AddCombo(stack, "_Position", _positionBox, Enum.GetValues<ChartDataLabelPosition>());
            ChartDialogHelpers.AddCheck(stack, _categoryBox);
            ChartDialogHelpers.AddCheck(stack, _seriesBox);
            ChartDialogHelpers.AddCheck(stack, _percentageBox);
            ChartDialogHelpers.AddCombo(stack, "_Separator", _separatorBox, Enum.GetValues<ChartDataLabelSeparator>());
            ChartDialogHelpers.AddCombo(stack, "Number _format", _numberFormatBox, Enum.GetValues<ChartDataLabelNumberFormat>());
            ChartDialogHelpers.AddCheck(stack, _calloutsBox);
            root.Children.Add(CreateGroupBox("Label Options", stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddColorText(stack, "_Fill color", _fillBox);
            ChartDialogHelpers.AddColorText(stack, "_Border color", _borderBox);
            ChartDialogHelpers.AddColorText(stack, "_Text color", _textBox);
            ChartDialogHelpers.AddNumericText(stack, "_Border thickness", _borderThicknessBox, "Enter a border width in points.");
            ChartDialogHelpers.AddNumericText(stack, "_Font size", _fontSizeBox, "Enter a font size in points.");
            ChartDialogHelpers.AddNumericText(stack, "Text _angle", _angleBox, "Enter degrees from -90 to 90.");
            root.Children.Add(CreateGroupBox("Fill & Line", stack));
        }
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartDataLabelsDialogResult result)
    {
        _showBox.IsChecked = result.ShowDataLabels;
        _positionBox.SelectedItem = result.Position;
        _categoryBox.IsChecked = result.ShowCategoryName;
        _seriesBox.IsChecked = result.ShowSeriesName;
        _percentageBox.IsChecked = result.ShowPercentage;
        _separatorBox.SelectedItem = result.Separator;
        _numberFormatBox.SelectedItem = result.NumberFormat;
        _calloutsBox.IsChecked = result.ShowCallouts;
        _fillBox.Text = ChartDialogHelpers.FormatColor(result.FillColor);
        _borderBox.Text = ChartDialogHelpers.FormatColor(result.BorderColor);
        _textBox.Text = ChartDialogHelpers.FormatColor(result.TextColor);
        _borderThicknessBox.Text = result.BorderThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _fontSizeBox.Text = result.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _angleBox.Text = result.Angle.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Accept()
    {
        if (!TryReadOptionalColor(_fillBox, out var fillColor))
        {
            ShowInvalidInputWarning("Enter a color as #RRGGBB or none.", _fillBox);
            return;
        }

        if (!TryReadOptionalColor(_borderBox, out var borderColor))
        {
            ShowInvalidInputWarning("Enter a color as #RRGGBB or none.", _borderBox);
            return;
        }

        if (!TryReadOptionalColor(_textBox, out var textColor))
        {
            ShowInvalidInputWarning("Enter a color as #RRGGBB or none.", _textBox);
            return;
        }

        if (!TryReadClampedDouble(_borderThicknessBox, min: 0, max: 10, out var borderThickness))
        {
            ShowInvalidInputWarning("Enter a data label border width from 0 to 10 points.", _borderThicknessBox);
            return;
        }

        if (!TryReadClampedDouble(_fontSizeBox, min: 6, max: 72, out var fontSize))
        {
            ShowInvalidInputWarning("Enter a data label font size from 6 to 72 points.", _fontSizeBox);
            return;
        }

        if (!TryReadClampedDouble(_angleBox, min: -90, max: 90, out var angle))
        {
            ShowInvalidInputWarning("Enter a data label angle from -90 to 90 degrees.", _angleBox);
            return;
        }

        Result = CreateResult(
            _showBox.IsChecked == true,
            ChartDialogHelpers.Selected(_positionBox, ChartDataLabelPosition.BestFit),
            _categoryBox.IsChecked == true,
            _seriesBox.IsChecked == true,
            _percentageBox.IsChecked == true,
            ChartDialogHelpers.Selected(_separatorBox, ChartDataLabelSeparator.Comma),
            ChartDialogHelpers.Selected(_numberFormatBox, ChartDataLabelNumberFormat.General),
            _calloutsBox.IsChecked == true,
            fillColor,
            borderColor,
            textColor,
            borderThickness,
            fontSize,
            angle);
        DialogResult = true;
    }

    private static bool TryReadOptionalColor(TextBox textBox, out CellColor? color) =>
        ColorInputParser.TryParseOptionalHexColor(textBox.Text, out color);

    private static bool TryReadClampedDouble(TextBox textBox, double min, double max, out double value)
    {
        value = 0;
        return double.TryParse(
                textBox.Text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value)
            && double.IsFinite(value)
            && value >= min
            && value <= max;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        MessageBox.Show(
            this,
            message,
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
        return true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _showBox.Focus();
        Keyboard.Focus(_showBox);
    }
}
