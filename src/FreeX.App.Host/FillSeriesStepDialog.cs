using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

public enum FillSeriesDirection
{
    Rows,
    Columns
}

public enum FillSeriesType
{
    Linear,
    Growth,
    Date,
    AutoFill
}

public enum FillSeriesDateUnit
{
    Day,
    Weekday,
    Month,
    Year
}

public sealed record FillSeriesStepDialogResult(
    double Step,
    FillSeriesDirection SeriesIn = FillSeriesDirection.Columns,
    FillSeriesType Type = FillSeriesType.Linear,
    FillSeriesDateUnit DateUnit = FillSeriesDateUnit.Day,
    double? StopValue = null);

public sealed class FillSeriesStepDialog : Window
{
    private readonly TextBox _stepBox = new();
    private readonly TextBox _stopBox = new();
    private readonly RadioButton _rowsButton = new() { Content = UiText.Get("FillSeriesStep_Rows"), GroupName = "SeriesIn" };
    private readonly RadioButton _columnsButton = new() { Content = UiText.Get("FillSeriesStep_Columns"), GroupName = "SeriesIn", IsChecked = true };
    private readonly RadioButton _linearButton = new() { Content = UiText.Get("FillSeriesStep_Linear"), GroupName = "SeriesType", IsChecked = true };
    private readonly RadioButton _growthButton = new() { Content = UiText.Get("FillSeriesStep_Growth"), GroupName = "SeriesType" };
    private readonly RadioButton _dateButton = new() { Content = UiText.Get("FillSeriesStep_Date"), GroupName = "SeriesType" };
    private readonly RadioButton _autoFillButton = new() { Content = UiText.Get("FillSeriesStep_AutoFill"), GroupName = "SeriesType" };
    private readonly RadioButton _dayButton = new() { Content = UiText.Get("FillSeriesStep_Day"), GroupName = "DateUnit", IsChecked = true };
    private readonly RadioButton _weekdayButton = new() { Content = UiText.Get("FillSeriesStep_Weekday"), GroupName = "DateUnit" };
    private readonly RadioButton _monthButton = new() { Content = UiText.Get("FillSeriesStep_Month"), GroupName = "DateUnit" };
    private readonly RadioButton _yearButton = new() { Content = UiText.Get("FillSeriesStep_Year"), GroupName = "DateUnit" };

    public FillSeriesStepDialogResult Result { get; private set; } = new(1);

    public FillSeriesStepDialog(double step = 1)
    {
        Result = new FillSeriesStepDialogResult(step);
        Title = UiText.Get("FillSeriesStep_Title");
        Width = 380;
        Height = 340;
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
        _linearButton.Checked += (_, _) => UpdateDateUnitAvailability();
        _growthButton.Checked += (_, _) => UpdateDateUnitAvailability();
        _dateButton.Checked += (_, _) => UpdateDateUnitAvailability();
        _autoFillButton.Checked += (_, _) => UpdateDateUnitAvailability();
        Content = CreateSeriesContent();
        UpdateDateUnitAvailability();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _columnsButton.Focus();
        Keyboard.Focus(_columnsButton);
    }

    private void FocusInvalidStepInput()
    {
        DialogFocus.FocusAndSelect(_stepBox);
    }

    private void FocusInvalidStopInput()
    {
        DialogFocus.FocusAndSelect(_stopBox);
    }

    private void UpdateDateUnitAvailability()
    {
        var isDateSeries = _dateButton.IsChecked == true;
        _dayButton.IsEnabled = isDateSeries;
        _weekdayButton.IsEnabled = isDateSeries;
        _monthButton.IsEnabled = isDateSeries;
        _yearButton.IsEnabled = isDateSeries;
    }

    public static bool TryCreateResult(string? input, out FillSeriesStepDialogResult result, out string? error)
    {
        result = new FillSeriesStepDialogResult(1);
        error = null;
        if (input is null || !FillSeriesPlanner.TryParseStep(input, out var step))
        {
            error = UiText.Get("FillSeriesStep_InvalidStepMessage");
            return false;
        }

        result = new FillSeriesStepDialogResult(step);
        return true;
    }

    public static FillSeriesStepDialogResult CreateResult(
        FillSeriesDirection seriesIn,
        FillSeriesType type,
        FillSeriesDateUnit dateUnit,
        string? stepText,
        string? stopText)
    {
        var step = FillSeriesPlanner.TryParseStep(stepText ?? "", out var parsedStep)
            ? parsedStep
            : 1;
        var stopValue = TryParseOptionalFiniteDouble(stopText, out var parsedStop)
            ? parsedStop
            : (double?)null;

        return new FillSeriesStepDialogResult(step, seriesIn, type, dateUnit, stopValue);
    }

    public static bool TryCreateResult(
        FillSeriesDirection seriesIn,
        FillSeriesType type,
        FillSeriesDateUnit dateUnit,
        string? stepText,
        string? stopText,
        out FillSeriesStepDialogResult result,
        out string? error)
    {
        result = new FillSeriesStepDialogResult(1, seriesIn, type, dateUnit);
        if (!TryCreateResult(stepText, out var stepResult, out error))
            return false;

        double? stopValue = null;
        if (!string.IsNullOrWhiteSpace(stopText))
        {
            if (!TryParseOptionalFiniteDouble(stopText, out var parsedStop))
            {
                error = UiText.Get("FillSeriesStep_InvalidStopMessage");
                return false;
            }

            stopValue = parsedStop;
        }

        result = new FillSeriesStepDialogResult(stepResult.Step, seriesIn, type, dateUnit, stopValue);
        return true;
    }

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
        stack.Children.Add(DialogButtonRowFactory.Create(Accept, 72));
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
                out var result,
                out var error))
        {
            DialogMessageHelper.ShowWarning(this, error ?? UiText.Get("FillSeriesStep_InvalidStepMessage"), Title);
            if (string.Equals(error, UiText.Get("FillSeriesStep_InvalidStopMessage"), StringComparison.Ordinal))
                FocusInvalidStopInput();
            else
                FocusInvalidStepInput();
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

    private static bool TryParseOptionalFiniteDouble(string? input, out double value)
    {
        value = 0;
        return !string.IsNullOrWhiteSpace(input) &&
               double.TryParse(input.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value);
    }

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
}
