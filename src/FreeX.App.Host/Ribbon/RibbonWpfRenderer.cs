using System.Windows;
using Free.Shared.Ribbon;

namespace FreeX.App.Host;

/// <summary>
/// FreeX-specific entry point for the shared WPF ribbon renderer. FreeX keeps its shell-side dropdown
/// zone wiring, so the shared renderer runs in host-managed dropdown mode.
/// </summary>
public static class RibbonWpfRenderer
{
    public const string SenderKey = Free.Shared.Ribbon.Wpf.RibbonWpfRenderer.SenderKey;

    public static FrameworkElement BuildTabContent(
        RibbonTab tab,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry = null,
        IRibbonStateStore? stateStore = null) =>
        Free.Shared.Ribbon.Wpf.RibbonWpfRenderer.BuildTabContent(
            tab,
            resourceHost,
            registry,
            stateStore,
            Free.Shared.Ribbon.Wpf.RibbonWpfRendererOptions.FreeXHost);
}
