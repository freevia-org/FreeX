using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

public sealed record ChartDataLabelsDialogResult(
    bool ShowDataLabels,
    ChartDataLabelPosition Position,
    bool ShowValue,
    bool ShowLegendKey,
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
    public ChartDataLabelsInput ToInput() =>
        new(
            ShowDataLabels,
            Position,
            ShowValue,
            ShowCategoryName,
            ShowSeriesName,
            ShowPercentage,
            ShowLegendKey,
            Separator,
            NumberFormat,
            ShowCallouts,
            FillColor,
            BorderColor,
            TextColor,
            BorderThickness,
            FontSize,
            Angle);

    public ChartLayoutOptions ToOptions() => ChartDataLabelsPlanner.Plan(ToInput());
}

public sealed class ChartDataLabelsDialog : Window
{
    private readonly CheckBox _showBox = new() { Content = UiText.Get("ChartDataLabels_ShowDataLabels") };
    private readonly CheckBox _valueBox = new() { Content = UiText.Get("ChartDataLabels_Value") };
    private readonly CheckBox _legendKeyBox = new() { Content = UiText.Get("ChartDataLabels_LegendKey") };
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

    public static ChartDataLabelsDialogResult FromChart(ChartModel chart)
    {
        var input = ChartDataLabelsPlanner.Read(chart);
        return CreateResult(
            input.ShowDataLabels,
            input.Position,
            input.ShowValue,
            input.ShowLegendKey,
            input.ShowCategoryName,
            input.ShowSeriesName,
            input.ShowPercentage,
            input.Separator ?? ChartDataLabelSeparator.Comma,
            input.NumberFormat ?? ChartDataLabelNumberFormat.General,
            input.ShowCallouts ?? false,
            input.FillColor,
            input.BorderColor,
            input.TextColor,
            input.BorderThickness ?? 0,
            input.FontSize ?? 11,
            input.Angle ?? 0);
    }

    public static ChartDataLabelsDialogResult CreateResult(
        bool showDataLabels,
        ChartDataLabelPosition position,
        bool showValue,
        bool showLegendKey,
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
        double angle)
    {
        var input = ChartDataLabelsPlanner.Normalize(new ChartDataLabelsInput(
            showDataLabels,
            position,
            showValue,
            showCategoryName,
            showSeriesName,
            showPercentage,
            showLegendKey,
            separator,
            numberFormat,
            showCallouts,
            fillColor,
            borderColor,
            textColor,
            borderThickness,
            fontSize,
            angle));
        return new(
            input.ShowDataLabels,
            input.Position,
            input.ShowValue,
            input.ShowLegendKey,
            input.ShowCategoryName,
            input.ShowSeriesName,
            input.ShowPercentage,
            input.Separator ?? ChartDataLabelSeparator.Comma,
            input.NumberFormat ?? ChartDataLabelNumberFormat.General,
            input.ShowCallouts ?? false,
            input.FillColor,
            input.BorderColor,
            input.TextColor,
            input.BorderThickness ?? 0,
            input.FontSize ?? 11,
            input.Angle ?? 0);
    }

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _showBox);
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartDataLabels_PositionLabel"), _positionBox, ChartDataLabelsPlanner.GetPositionChoices().Select(choice => choice.Position));
            ChartDialogHelpers.AddCheck(stack, _valueBox);
            ChartDialogHelpers.AddCheck(stack, _legendKeyBox);
            ChartDialogHelpers.AddCheck(stack, _categoryBox);
            ChartDialogHelpers.AddCheck(stack, _seriesBox);
            ChartDialogHelpers.AddCheck(stack, _percentageBox);
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartDataLabels_SeparatorLabel"), _separatorBox, ChartDataLabelsPlanner.GetSeparatorChoices());
            ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartDataLabels_NumberFormatLabel"), _numberFormatBox, ChartDataLabelsPlanner.GetNumberFormatChoices());
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
        _valueBox.IsChecked = result.ShowValue;
        _legendKeyBox.IsChecked = result.ShowLegendKey;
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
        if (!ChartDataLabelsPlanner.TryParseDialogInput(
                _showBox.IsChecked == true,
                SelectedPosition(),
                _valueBox.IsChecked == true,
                _legendKeyBox.IsChecked == true,
                _categoryBox.IsChecked == true,
                _seriesBox.IsChecked == true,
                _percentageBox.IsChecked == true,
                SelectedSeparator(),
                SelectedNumberFormat(),
                _calloutsBox.IsChecked == true,
                _fillBox.Text,
                _borderBox.Text,
                _textBox.Text,
                _borderThicknessBox.Text,
                _fontSizeBox.Text,
                _angleBox.Text,
                out var input,
                out var issue))
        {
            ShowPlannerParseWarning(issue);
            return;
        }

        Result = CreateResult(
            input.ShowDataLabels,
            input.Position,
            input.ShowValue,
            input.ShowLegendKey,
            input.ShowCategoryName,
            input.ShowSeriesName,
            input.ShowPercentage,
            input.Separator ?? ChartDataLabelSeparator.Comma,
            input.NumberFormat ?? ChartDataLabelNumberFormat.General,
            input.ShowCallouts ?? false,
            input.FillColor,
            input.BorderColor,
            input.TextColor,
            input.BorderThickness ?? 0,
            input.FontSize ?? 11,
            input.Angle ?? 0);
        DialogResult = true;
    }

    private ChartDataLabelPosition? SelectedPosition() =>
        _positionBox.SelectedItem is ChartDataLabelPosition value ? value : null;

    private ChartDataLabelSeparator? SelectedSeparator() =>
        _separatorBox.SelectedItem is ChartDataLabelSeparator value ? value : null;

    private ChartDataLabelNumberFormat? SelectedNumberFormat() =>
        _numberFormatBox.SelectedItem is ChartDataLabelNumberFormat value ? value : null;

    private void ShowPlannerParseWarning(ChartDataLabelsParseIssue issue)
    {
        var (message, target) = issue switch
        {
            ChartDataLabelsParseIssue.BorderColor => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _borderBox),
            ChartDataLabelsParseIssue.TextColor => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _textBox),
            ChartDataLabelsParseIssue.BorderThickness => (UiText.Get("ChartDataLabels_InvalidBorderThicknessMessage"), _borderThicknessBox),
            ChartDataLabelsParseIssue.FontSize => (UiText.Get("ChartDataLabels_InvalidFontSizeMessage"), _fontSizeBox),
            ChartDataLabelsParseIssue.Angle => (UiText.Get("ChartDataLabels_InvalidAngleMessage"), _angleBox),
            _ => (UiText.Get("ChartDialog_InvalidOptionalColorMessage"), _fillBox),
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

    private void FocusInitialKeyboardTarget()
    {
        _showBox.Focus();
        Keyboard.Focus(_showBox);
    }
}
