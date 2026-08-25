using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Free.Shared.Ribbon.Avalonia;

/// <summary>
/// Theme-owned interaction colors for the sister-app title-bar QAT. The defaults preserve the
/// transparent neutral toolbar used by hosts that do not provide a title-bar palette.
/// </summary>
public sealed record SisterQuickAccessToolbarVisualOptions
{
    public IBrush HoverBackground { get; init; } = Brushes.Transparent;
    public IBrush PressedBackground { get; init; } = Brushes.Transparent;
    public IBrush InteractionBorder { get; init; } = Brushes.Transparent;
    public double DisabledOpacity { get; init; } = 0.5d;
}

/// <summary>
/// Renders the neutral sister-app QAT contract with Avalonia controls and the shared ribbon icon source.
/// </summary>
public static class SisterQuickAccessToolbarBuilder
{
    public static IReadOnlyList<Button> Render(
        Panel host,
        SisterQuickAccessToolbarActions actions,
        IBrush? foreground = null,
        SisterQuickAccessToolbarVisualOptions? visuals = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(actions);
        visuals ??= new SisterQuickAccessToolbarVisualOptions();

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
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Content = AvaloniaRibbonIcons.BuildMonochrome(
                    command.IconKind,
                    16,
                    command.CommandId,
                    foreground ?? Brushes.White),
            };

            button.Styles.Add(new Style(selector => selector.OfType<Button>().Class(":pointerover"))
            {
                Setters =
                {
                    new Setter(Button.BackgroundProperty, visuals.HoverBackground),
                    new Setter(Button.BorderBrushProperty, visuals.InteractionBorder),
                },
            });
            button.Styles.Add(new Style(selector => selector.OfType<Button>().Class(":pressed"))
            {
                Setters =
                {
                    new Setter(Button.BackgroundProperty, visuals.PressedBackground),
                    new Setter(Button.BorderBrushProperty, visuals.InteractionBorder),
                },
            });
            button.Styles.Add(new Style(selector => selector.OfType<Button>().Class(":focus"))
            {
                Setters = { new Setter(Button.BorderBrushProperty, visuals.InteractionBorder) },
            });
            button.Styles.Add(new Style(selector => selector.OfType<Button>().Class(":disabled"))
            {
                Setters =
                {
                    new Setter(Button.BackgroundProperty, Brushes.Transparent),
                    new Setter(Button.BorderBrushProperty, Brushes.Transparent),
                    new Setter(global::Avalonia.Visual.OpacityProperty, visuals.DisabledOpacity),
                },
            });

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
