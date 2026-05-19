using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Freexcel.App.Host;

public sealed class AutomationInvokeButton : Button
{
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new AutomationInvokeButtonPeer(this);

    private sealed class AutomationInvokeButtonPeer(AutomationInvokeButton owner)
        : ButtonAutomationPeer(owner), IInvokeProvider
    {
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

        void IInvokeProvider.Invoke()
        {
            if (!owner.IsEnabled)
                throw new ElementNotEnabledException();

            owner.Dispatcher.BeginInvoke(
                (Action)(() => owner.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, owner))),
                DispatcherPriority.Input);
        }
    }
}
