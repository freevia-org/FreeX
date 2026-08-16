using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.FillSeries;

namespace FreeX.App.Host;

public sealed class FillSeriesStepDialog : Window
{
    private readonly TextBox _stepBox = new();
    private readonly TextBox _stopBox = new();
    private readonly RadioButton _rowsButton = new() { Content = UiText.Get("FillSeriesStep_Rows"), GroupName = "SeriesIn" };
    private readonly RadioButton _columnsButton = new() { Content = UiText.Get("FillSeriesStep_Columns"), GroupName = "SeriesIn", IsChecked = FillSeriesPlanner.DefaultOptions.SeriesIn == FillSeriesDirection.Columns };
    private readonly RadioButton _linearButton = new() { Content = UiText.Get("FillSeriesStep_Linear"), GroupName = "SeriesType", IsChecked = FillSeriesPlanner.DefaultOptions.Type == FillSeriesType.Linear };
    private readonly RadioButton _growthButton = new() { Content = UiText.Get("FillSeriesStep_Growth"), GroupName = "SeriesType" };
    private readonly RadioButton _dateButton = new() { Content = UiText.Get("FillSeriesStep_Date"), GroupName = "SeriesType" };
    private readonly RadioButton _autoFillButton = new() { Content = UiText.Get("FillSeriesStep_AutoFill"), GroupName = "SeriesType" };
    private readonly RadioButton _dayButton = new() { Content = UiText.Get("FillSeriesStep_Day"), GroupName = "DateUnit", IsChecked = FillSeriesPlanner.DefaultOptions.DateUnit == FillSeriesDateUnit.Day };
    private readonly RadioButton _weekdayButton = new() { Content = UiText.Get("FillSeriesStep_Weekday"), GroupName = "DateUnit" };
    private readonly RadioButton _monthButton = new() { Content = UiText.Get("FillSeriesStep_Month"), GroupName = "DateUnit" };
    private readonly RadioButton _yearButton = new() { Content = UiText.Get("FillSeriesStep_Year"), GroupName = "DateUnit" };
    private readonly CheckBox _trendBox = new() { Content = UiText.Get("FillSeriesStep_Trend") };

    public FillSeriesOptions Result { get; private set; } = FillSeriesPlanner.DefaultOptions;

    public FillSeriesStepDialog(double step = 1)
    {
        Result = FillSeriesPlanner.CreateDefaultOptions(step);
        Title = UiText.Get("FillSeriesStep_Title");
        Width = 380;
        Height = 386;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _stepBox.Text = step.ToString(CultureInfo.InvariantCulture);
        _stopBox.Text = "";
        AutomationProperties.SetName(_stepBox, UiText.Get("FillSeriesStep_StepValueAutomationName"));
        AutomationProperties.SetAutomationId(_stepBox, "FillSeriesStepValueBox");
        AutomationProperties.SetHelpText(_stepBox, UiText.Get("FillSeriesStep_StepValueHelpText"));
        AutomationProperties.SetName(_stopBox, UiText.Get("FillSeriesStep_StopValueAutomationName"));
        AutomationProperties.SetAutomationId(_stopBox, "FillSeriesStopValueBox");
        AutomationProperties.SetHelpText(_stopBox, UiText.Get("FillSeriesStep_StopValueHelpText"));
        AutomationProperties.SetAutomationId(_trendBox, "FillSeriesTrendCheckBox");
        _linearButton.Checked += (_, _) => UpdateDateUnitAvailability();
        _growthButton.Checked += (_, _) => UpdateDateUnitAvailability();
        _dateButton.Checked += (_, _) => UpdateDateUnitAvailability();
        _autoFillButton.Checked += (_, _) => UpdateDateUnitAvailability();
        _linearButton.Checked += (_, _) => UpdateTrendAvailability();
        _growthButton.Checked += (_, _) => UpdateTrendAvailability();
        _dateButton.Checked += (_, _) => UpdateTrendAvailability();
        _autoFillButton.Checked += (_, _) => UpdateTrendAvailability();
        _trendBox.Checked += (_, _) => UpdateTrendAvailability();
        _trendBox.Unchecked += (_, _) => UpdateTrendAvailability();
        Content = CreateSeriesContent();
        UpdateDateUnitAvailability();
        UpdateTrendAvailability();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
            ApplyAutomationNames();
    }

    private void FocusInitialKeyboardTarget()
    {
        _columnsButton.Focus();
        Keyboard.Focus(_columnsButton);
    }

    private void UpdateDateUnitAvailability()
    {
        var isDateSeries = FillSeriesPlanner.IsDateUnitEnabled(SelectedSeriesType());
        _dayButton.IsEnabled = isDateSeries;
        _weekdayButton.IsEnabled = isDateSeries;
        _monthButton.IsEnabled = isDateSeries;
        _yearButton.IsEnabled = isDateSeries;
    }

    private void UpdateTrendAvailability()
    {
        var isTrendEligible = FillSeriesPlanner.IsTrendEnabled(SelectedSeriesType());
        _trendBox.IsEnabled = isTrendEligible;
        if (!isTrendEligible)
            _trendBox.IsChecked = false;

        // Excel's Step value plays no part in Trend mode -- the box is disabled while Trend is checked.
        _stepBox.IsEnabled = !(isTrendEligible && _trendBox.IsChecked == true);
    }

    public static bool TryCreateResult(string? input, out FillSeriesOptions result, out string? error)
    {
        result = FillSeriesPlanner.DefaultOptions;
        error = null;
        if (input is null || !FillSeriesPlanner.TryParseStep(input, CultureInfo.CurrentCulture, out var step))
        {
            error = UiText.Get("FillSeriesStep_InvalidStepMessage");
            return false;
        }

        result = FillSeriesPlanner.CreateDefaultOptions(step);
        return true;
    }

    public static bool TryCreateResult(
        FillSeriesDirection seriesIn,
        FillSeriesType type,
        FillSeriesDateUnit dateUnit,
        string? stepText,
        string? stopText,
        out FillSeriesOptions result,
        out string? error) =>
        TryCreateResult(seriesIn, type, dateUnit, stepText, stopText, out result, out error, out _);

    public static bool TryCreateResult(
        FillSeriesDirection seriesIn,
        FillSeriesType type,
        FillSeriesDateUnit dateUnit,
        string? stepText,
        string? stopText,
        out FillSeriesOptions result,
        out string? error,
        out FillSeriesInputError inputError) =>
        TryCreateResult(seriesIn, type, dateUnit, stepText, stopText, trend: false, out result, out error, out inputError);

    public static bool TryCreateResult(
        FillSeriesDirection seriesIn,
        FillSeriesType type,
        FillSeriesDateUnit dateUnit,
        string? stepText,
        string? stopText,
        bool trend,
        out FillSeriesOptions result,
        out string? error,
        out FillSeriesInputError inputError)
    {
        if (FillSeriesPlanner.TryCreateOptions(
                seriesIn,
                type,
                dateUnit,
                stepText,
                stopText,
                trend,
                CultureInfo.CurrentCulture,
                out result,
                out inputError))
        {
            error = null;
            return true;
        }

        result = FillSeriesPlanner.DefaultOptions with { SeriesIn = seriesIn, Type = type, DateUnit = dateUnit, Trend = trend };
        error = ToErrorMessage(inputError);
        return false;
    }

    private static string? ToErrorMessage(FillSeriesInputError inputError) =>
        inputError == FillSeriesInputError.None
            ? null
            : FillSeriesPlanner
                .DescribeInputError(inputError)
                .Message
                .Resolve(UiText.Get, UiText.Format);

    private UIElement CreateSeriesContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = UiText.Get("FillSeriesStep_SeriesInHeader"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(CreateHorizontalRow(_rowsButton, _columnsButton));
        stack.Children.Add(new TextBlock { Text = UiText.Get("FillSeriesStep_TypeHeader"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
        stack.Children.Add(CreateHorizontalRow(_linearButton, _growthButton, _dateButton, _autoFillButton));
        stack.Children.Add(new TextBlock { Text = UiText.Get("FillSeriesStep_DateUnitHeader"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
        stack.Children.Add(CreateHorizontalRow(_dayButton, _weekdayButton, _monthButton, _yearButton));
        stack.Children.Add(CreateLabeledTextBox(UiText.Get("FillSeriesStep_StepValueLabel"), _stepBox));
        stack.Children.Add(CreateLabeledTextBox(UiText.Get("FillSeriesStep_StopValueLabel"), _stopBox));
        _trendBox.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(_trendBox);
        stack.Children.Add(DialogButtonRowFactory.Create(Accept, 72, rowMargin: new Thickness(0, 16, 0, 0)));
        return stack;
    }

    private void Accept()
    {
        if (!TryCreateResult(
                _rowsButton.IsChecked == true ? FillSeriesDirection.Rows : FillSeriesDirection.Columns,
                SelectedSeriesType(),
                SelectedDateUnit(),
                _stepBox.Text,
                _stopBox.Text,
                _trendBox.IsEnabled && _trendBox.IsChecked == true,
                out var result,
                out var error,
                out var inputError))
        {
            var presentation = FillSeriesPlanner.DescribeInputError(inputError);
            DialogFocus.ShowWarningAndFocus(
                this,
                presentation.Message.Resolve(UiText.Get, UiText.Format),
                Title,
                presentation.FocusTarget == FillSeriesInputFocusTarget.StopValue ? _stopBox : _stepBox);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private FillSeriesType SelectedSeriesType() =>
        _growthButton.IsChecked == true ? FillSeriesType.Growth :
        _dateButton.IsChecked == true ? FillSeriesType.Date :
        _autoFillButton.IsChecked == true ? FillSeriesType.AutoFill :
        FillSeriesType.Linear;

    private FillSeriesDateUnit SelectedDateUnit() =>
        _weekdayButton.IsChecked == true ? FillSeriesDateUnit.Weekday :
        _monthButton.IsChecked == true ? FillSeriesDateUnit.Month :
        _yearButton.IsChecked == true ? FillSeriesDateUnit.Year :
        FillSeriesDateUnit.Day;

    private static StackPanel CreateHorizontalRow(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var child in children)
        {
            if (child is Control control)
                control.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(child);
        }

        return row;
    }

    private static Grid CreateLabeledTextBox(string label, TextBox textBox)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.Children.Add(new Label { Content = label, Target = textBox, Padding = new Thickness(0, 3, 8, 0) });
        textBox.Height = 24;
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(textBox);
        return grid;
    }

    /// <summary>
    /// Screen-reader names for this dialog's controls. Ported from the abandoned
    /// codex/dialog-parity-loop branch, whose paths predate the Freexcel -> FreeX rename.
    /// </summary>
    private void ApplyAutomationNames()
    {
        AutomationProperties.SetName(_stepBox, "Step value");
        AutomationProperties.SetName(_stopBox, "Stop value");
    }
}
