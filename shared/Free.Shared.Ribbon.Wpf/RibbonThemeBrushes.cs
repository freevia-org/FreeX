using System.Windows;
using System.Windows.Media;
using Free.Shared.AppServices;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// r279: resolves the shared WPF ribbon's themed brushes under the RUNNING app's resource-key
/// prefix instead of FreeX's.
///
/// <para>Every themed brush is generated per app by <c>WpfThemeApplier.BuildResources(theme,
/// keyPrefix)</c>, so FreeX's ribbon surface is <c>FreeXRibbonSurfaceBrush</c> and FreeP's is
/// <c>FreePRibbonSurfaceBrush</c> -- FreeP's own startup test pins that prefix. The shared renderer
/// asked for the FreeX key by name, so in FreeW's and FreeP's WPF hosts every one of these lookups
/// missed and silently painted the hardcoded light-theme fallback, whatever theme was active.</para>
///
/// <para>The FreeX key is still tried second. That keeps the FreeX host and any host that merged a
/// FreeX-named dictionary rendering exactly as before, so this can add the sister apps without
/// changing the app the keys were named for.</para>
/// </summary>
internal static class RibbonThemeBrushes
{
    /// <summary>
    /// Resolves <c>{ambient-prefix}{role}Brush</c>, then <c>FreeX{role}Brush</c>, then the fallback.
    /// </summary>
    public static Brush Resolve(FrameworkElement resourceHost, string role, Brush fallback)
    {
        ArgumentNullException.ThrowIfNull(resourceHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(fallback);

        var prefix = AppProduct.Current.ProductDirectoryName;
        if (!string.IsNullOrWhiteSpace(prefix)
            && resourceHost.TryFindResource($"{prefix}{role}Brush") is Brush themed)
        {
            return themed;
        }

        return resourceHost.TryFindResource($"FreeX{role}Brush") as Brush ?? fallback;
    }
}
