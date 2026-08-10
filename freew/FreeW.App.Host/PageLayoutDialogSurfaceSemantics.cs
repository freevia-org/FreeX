using System.Windows;
using System.Windows.Automation;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

internal static class PageLayoutDialogSurfaceSemantics
{
    public static void Apply<TField>(Window window, DialogSurfaceSpec<TField> surface)
        where TField : struct, Enum
    {
        AutomationProperties.SetAutomationId(window, surface.AutomationId);
        AutomationProperties.SetName(window, surface.AutomationName);
    }

    public static void Apply<TField>(FrameworkElement element, DialogFieldSurfaceSpec<TField> field)
        where TField : struct, Enum
    {
        AutomationProperties.SetAutomationId(element, field.AutomationId);
        AutomationProperties.SetName(element, field.AutomationName);
    }
}
