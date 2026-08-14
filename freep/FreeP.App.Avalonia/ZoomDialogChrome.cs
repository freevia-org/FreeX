using Avalonia.Automation;
using Avalonia.Controls;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal static class ZoomDialogChrome
{
    private static readonly AvaloniaCompactDialogChromeStyle Style =
        AvaloniaCompactDialogChrome.WindowsStyle;

    internal static void Apply(Window window) =>
        AvaloniaCompactDialogChrome.ApplyWindow(window, Style);

    internal static void Apply(
        Window window,
        PresentationDialogSurfacePlan<ZoomTargetDialogField, ZoomTargetDialogAction> surface)
    {
        Apply(window);
        AutomationProperties.SetName(window, surface.AccessibleName);
        AutomationProperties.SetAutomationId(window, surface.AutomationId);
    }

    internal static void ApplyField(
        Control control,
        PresentationDialogFieldPlan<ZoomTargetDialogField> field)
    {
        AutomationProperties.SetName(control, field.AccessibleName);
        AutomationProperties.SetAutomationId(control, field.AutomationId);
        if (!string.IsNullOrWhiteSpace(field.HelpText))
            AutomationProperties.SetHelpText(control, field.HelpText);
    }

    internal static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, Style, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }

    internal static Button MakeButton(
        PresentationDialogActionPlan<ZoomTargetDialogAction> plan,
        Action action,
        bool isEnabled = true)
    {
        var button = new Button
        {
            Content = plan.Label,
            IsDefault = plan.IsDefault,
            IsCancel = plan.IsCancel,
            IsEnabled = isEnabled,
            MinWidth = 80,
        };
        AutomationProperties.SetName(button, plan.AccessibleName);
        AutomationProperties.SetAutomationId(button, plan.AutomationId);
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            Style,
            minWidth: 80,
            isDefault: plan.IsDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
