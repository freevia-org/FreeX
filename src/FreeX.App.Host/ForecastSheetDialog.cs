using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host;

public sealed record ForecastSheetDialogResult(uint Periods);

public sealed class ForecastSheetDialog : Window
{
    private readonly TextBox _periodsBox = new();

    public ForecastSheetDialogResult Result { get; private set; } = new(3);

    public ForecastSheetDialog(uint periods = 3)
    {
        Result = new ForecastSheetDialogResult(periods);
        Title = "Forecast Sheet";
        Width = 320;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _periodsBox.Text = periods.ToString(CultureInfo.InvariantCulture);
        AutomationProperties.SetName(_periodsBox, "Forecast periods");
        Content = ObjectSizeDialog.CreateSingleInputContent("Forecast _periods:", _periodsBox, Accept, acceptContent: "_Create");
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusInvalidPeriodsInput();
    }

    private void FocusInvalidPeriodsInput()
    {
        DialogFocus.FocusAndSelect(_periodsBox);
    }

    public static bool TryCreateResult(string input, out ForecastSheetDialogResult result, out string? error)
    {
        result = new ForecastSheetDialogResult(3);
        error = null;
        if (!ForecastSheetInputParser.TryParsePeriods(input, out var periods))
        {
            error = "Enter a positive whole number of forecast periods.";
            return false;
        }

        result = new ForecastSheetDialogResult(periods);
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_periodsBox.Text, out var result, out var error))
        {
            DialogMessageHelper.ShowWarning(this, error ?? "Enter a positive whole number of forecast periods.", Title);
            FocusInvalidPeriodsInput();
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
