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
        Title = UiText.Get("ForecastSheet_Title");
        Width = 320;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _periodsBox.Text = periods.ToString(CultureInfo.InvariantCulture);
        AutomationProperties.SetName(_periodsBox, UiText.Get("ForecastSheet_PeriodsAutomationName"));
        AutomationProperties.SetAutomationId(_periodsBox, "ForecastPeriodsBox");
        AutomationProperties.SetHelpText(_periodsBox, UiText.Get("ForecastSheet_PeriodsHelpText"));
        Content = ObjectSizeDialog.CreateSingleInputContent(
            UiText.Get("ForecastSheet_PeriodsLabel"),
            _periodsBox,
            Accept,
            acceptContent: UiText.Get("ForecastSheet_CreateButton"));
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
            error = UiText.Get("ForecastSheet_InvalidPeriodsMessage");
            return false;
        }

        result = new ForecastSheetDialogResult(periods);
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_periodsBox.Text, out var result, out var error))
        {
            DialogMessageHelper.ShowWarning(this, error ?? UiText.Get("ForecastSheet_InvalidPeriodsMessage"), Title);
            FocusInvalidPeriodsInput();
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
