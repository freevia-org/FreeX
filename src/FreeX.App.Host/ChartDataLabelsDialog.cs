using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogInputParser;
using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

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
    private readonly CheckBox _showBox = new() { Content = UiText.Get("ChartDataLabels_ShowDataLabels") };
    private readonly CheckBox _categoryBox = new() { Content = UiText.Get("ChartDataLabels_CategoryName") };
    private readonly CheckBox _seriesBox = new() { Content = UiText.Get("ChartDataLabels_SeriesName") };
    private readonly CheckBox _percentageBox = new() { Content = UiText.Get("ChartDataLabels_Percentage") };
    private readonly CheckBox _calloutsBox = new() { Content = UiText.Get("ChartDataLabels_Callouts") };
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
        Title = UiText.Get("ChartDataLabels_Title");
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
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartDataLabels_PositionLabel"), _positionBox, Enum.GetValues<ChartDataLabelPosition>());
            ChartDialogHelpers.AddCheck(stack, _categoryBox);
            ChartDialogHelpers.AddCheck(stack, _seriesBox);
            ChartDialogHelpers.AddCheck(stack, _percentageBox);
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartDataLabels_SeparatorLabel"), _separatorBox, Enum.GetValues<ChartDataLabelSeparator>());
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartDataLabels_NumberFormatLabel"), _numberFormatBox, Enum.GetValues<ChartDataLabelNumberFormat>());
            ChartDialogHelpers.AddCheck(stack, _calloutsBox);
            root.Children.Add(CreateGroupBox(UiText.Get("ChartDataLabels_LabelOptionsGroup"), stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartDataLabels_FillColorLabel"), _fillBox);
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartDataLabels_BorderColorLabel"), _borderBox);
            ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartDataLabels_TextColorLabel"), _textBox);
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartDataLabels_BorderThicknessLabel"), _borderThicknessBox, UiText.Get("ChartDataLabels_BorderThicknessHelpText"));
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartDataLabels_FontSizeLabel"), _fontSizeBox, UiText.Get("ChartDataLabels_FontSizeHelpText"));
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartDataLabels_TextAngleLabel"), _angleBox, UiText.Get("ChartDataLabels_TextAngleHelpText"));
            root.Children.Add(CreateGroupBox(UiText.Get("ChartDialog_FillLineGroup"), stack));
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
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _fillBox);
            return;
        }

        if (!TryReadOptionalColor(_borderBox, out var borderColor))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _borderBox);
            return;
        }

        if (!TryReadOptionalColor(_textBox, out var textColor))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _textBox);
            return;
        }

        if (!TryReadClampedDouble(_borderThicknessBox, min: 0, max: 10, out var borderThickness))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDataLabels_InvalidBorderThicknessMessage"), _borderThicknessBox);
            return;
        }

        if (!TryReadClampedDouble(_fontSizeBox, min: 6, max: 72, out var fontSize))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDataLabels_InvalidFontSizeMessage"), _fontSizeBox);
            return;
        }

        if (!TryReadClampedDouble(_angleBox, min: -90, max: 90, out var angle))
        {
            ShowInvalidInputWarning(UiText.Get("ChartDataLabels_InvalidAngleMessage"), _angleBox);
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

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
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
