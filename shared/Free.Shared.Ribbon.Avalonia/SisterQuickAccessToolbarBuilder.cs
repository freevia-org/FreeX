using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Free.Shared.Ribbon.Avalonia;

/// <summary>
/// Renders the neutral sister-app QAT contract with Avalonia controls and the shared ribbon icon source.
/// </summary>
public static class SisterQuickAccessToolbarBuilder
{
    public static IReadOnlyList<Button> Render(
        Panel host,
        SisterQuickAccessToolbarActions actions,
        IBrush? foreground = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(actions);

        var buttons = new List<Button>();
        foreach (var command in SisterQuickAccessToolbarCatalog.DefaultCommands)
        {
            var button = new Button
            {
                Width = 26,
                Height = 22,
                Margin = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Content = AvaloniaRibbonIcons.BuildMonochrome(
                    command.IconKind,
                    16,
                    command.CommandId,
                    foreground ?? Brushes.White),
            };

            AutomationProperties.SetAutomationId(button, command.CommandId);
            AutomationProperties.SetName(button, command.Tooltip);
            ToolTip.SetTip(button, command.Tooltip);
            button.Click += (_, _) =>
            {
                // Avalonia has no dispatcher unhandled-exception hook: an exception escaping this
                // handler kills the process. Report it and leave the shell running instead.
                try
                {
                    SisterQuickAccessToolbarCatalog.Execute(actions, command.CommandId);
                }
                catch (Exception ex)
                {
                    RibbonCommandFaultReporter.Report(ex, command.CommandId);
                }
            };

            host.Children.Add(button);
            buttons.Add(button);
        }

        return buttons;
    }
}
