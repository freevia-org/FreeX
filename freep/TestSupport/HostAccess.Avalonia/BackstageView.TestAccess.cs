using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FreeP.App.Avalonia.Backstage;

internal sealed partial class BackstageView
{
    internal bool ApplyCustomPrintRangeForTests(string rangeText)
    {
        if (_customRangeInput is null || _customRangeApplyButton is null)
            return false;

        _customRangeInput.Text = rangeText;
        _customRangeApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }

    internal IReadOnlyList<(string AutomationId, bool IsEnabled)> PrintActionsForTests =>
        _printActionButtons
            .Select(action => (action.AutomationId, action.Button.IsEnabled))
            .ToArray();

    internal bool InvokePrintActionForTests(string automationId)
    {
        var action = _printActionButtons.FirstOrDefault(
            candidate => candidate.AutomationId == automationId);
        if (action.Button is null)
            return false;

        action.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }
}
