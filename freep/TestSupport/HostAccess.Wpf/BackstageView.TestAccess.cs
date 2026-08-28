using System.Windows;
using System.Windows.Controls.Primitives;

namespace FreeP.App.Host.Backstage;

internal sealed partial class BackstageView
{
    internal bool TryActivateEntryForTests(string label) => _backstage.Frame.TryActivateEntry(label);

    internal bool ApplyCustomPrintRangeForTests(string rangeText)
    {
        if (_customRangeInput is null || _customRangeApplyButton is null)
            return false;

        _customRangeInput.Text = rangeText;
        _customRangeApplyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return true;
    }
}
