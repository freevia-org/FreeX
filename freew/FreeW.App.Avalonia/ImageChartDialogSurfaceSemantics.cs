using Avalonia.Automation;
using Avalonia.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

internal static class ImageChartDialogSurfaceSemantics
{
    public static void Apply<TField>(Window window, DialogSurfaceSpec<TField> surface)
        where TField : struct, Enum
    {
        AutomationProperties.SetAutomationId(window, surface.AutomationId);
        AutomationProperties.SetName(window, surface.AutomationName);
    }

    public static void Apply<TField>(Control control, DialogFieldSurfaceSpec<TField> field)
        where TField : struct, Enum
    {
        AutomationProperties.SetAutomationId(control, field.AutomationId);
        AutomationProperties.SetName(control, field.AutomationName);
    }

    public static void ApplyValidation<TField>(Control control, DialogSurfaceSpec<TField> surface)
        where TField : struct, Enum
    {
        if (surface.ValidationAutomationId is { } automationId)
            AutomationProperties.SetAutomationId(control, automationId);
    }
}
