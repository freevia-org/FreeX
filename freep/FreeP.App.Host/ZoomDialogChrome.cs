using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal static class ZoomDialogChrome
{
    internal static void Apply(
        Window window,
        PresentationDialogSurfacePlan<ZoomTargetDialogField, ZoomTargetDialogAction> surface)
    {
        AutomationProperties.SetName(window, surface.AccessibleName);
        AutomationProperties.SetAutomationId(window, surface.AutomationId);
    }

    internal static void ApplyField(
        DependencyObject control,
        PresentationDialogFieldPlan<ZoomTargetDialogField> field)
    {
        AutomationProperties.SetName(control, field.AccessibleName);
        AutomationProperties.SetAutomationId(control, field.AutomationId);
        if (!string.IsNullOrWhiteSpace(field.HelpText))
            AutomationProperties.SetHelpText(control, field.HelpText);
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
            MinWidth = 75,
        };
        AutomationProperties.SetName(button, plan.AccessibleName);
        AutomationProperties.SetAutomationId(button, plan.AutomationId);
        button.Click += (_, _) => action();
        return button;
    }
}
