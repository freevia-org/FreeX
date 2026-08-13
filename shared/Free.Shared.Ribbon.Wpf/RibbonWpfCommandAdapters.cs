using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Bridges a declarative command id to an existing WPF ribbon control's click event.
/// </summary>
public sealed class WpfControlRibbonCommand : IRibbonCommand
{
    private readonly Control _source;

    public WpfControlRibbonCommand(Control source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public void Execute(RibbonCommandContext context)
    {
        switch (_source)
        {
            case MenuItem menuItem:
                menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                break;
            case ButtonBase button:
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                break;
        }
    }
}
