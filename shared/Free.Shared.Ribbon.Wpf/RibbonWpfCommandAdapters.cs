using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Invokes a WPF host handler method through the neutral ribbon command registry.
/// </summary>
public sealed class WpfReflectiveRibbonCommand : IRibbonCommand
{
    private readonly object _target;
    private readonly MethodInfo _method;
    private readonly object? _fallbackSender;

    public WpfReflectiveRibbonCommand(object target, MethodInfo method, object? fallbackSender = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _method = method ?? throw new ArgumentNullException(nameof(method));
        _fallbackSender = fallbackSender;
    }

    public void Execute(RibbonCommandContext context)
    {
        var sender = (context.Parameters.TryGetValue(RibbonWpfRenderer.SenderKey, out var wpfSender)
                ? wpfSender
                : null)
            ?? _fallbackSender ?? _target;

        var args = _method.GetParameters().Length == 0
            ? Array.Empty<object?>()
            : new[] { sender, new RoutedEventArgs() };

        try
        {
            _method.Invoke(_target, args);
        }
        catch (TargetInvocationException ex)
        {
            Debug.WriteLine($"Ribbon command '{_method.Name}' threw: {ex.InnerException}");
        }
    }
}

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
