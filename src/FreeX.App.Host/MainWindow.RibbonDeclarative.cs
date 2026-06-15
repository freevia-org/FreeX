using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeX.Ribbon;

namespace FreeX.App.Host;

public partial class MainWindow
{
    /// <summary>
    /// Opt-in (env <c>FREEX_RIBBON_DECLARATIVE=1</c>) swap of the hand-authored XAML ribbon for the
    /// declarative <see cref="FreeXRibbonDefinition"/> rendered via <see cref="RibbonWpfRenderer"/>.
    /// Commands bridge to the existing handlers: every original ribbon control is captured by its
    /// <c>CommandName</c> before replacement, and the rendered button raises the original control's
    /// Click so existing behavior runs unchanged. Default (flag unset) keeps the live XAML ribbon,
    /// so this never regresses keytips/adaptive/state-sync in shipping builds.
    /// </summary>
    private void TryApplyDeclarativeRibbon()
    {
        if (Environment.GetEnvironmentVariable("FREEX_RIBBON_DECLARATIVE") != "1")
            return;
        if (RibbonTabs is null)
            return;

        try
        {
            var registry = BuildDeclarativeRibbonRegistry();
            var definition = FreeXRibbonDefinition.Build();

            foreach (var item in RibbonTabs.Items)
            {
                if (item is not TabItem tabItem)
                    continue;
                if (!RibbonMetadata.TryGetCatalogId(tabItem, out var catalogId))
                    continue;
                if (definition.FindTab(catalogId) is not { } definitionTab)
                    continue;

                tabItem.Content = RibbonWpfRenderer.BuildTabContent(definitionTab, this, registry);
            }
        }
        catch (Exception ex)
        {
            // A preview-mode swap must never take down startup.
            System.Diagnostics.Debug.WriteLine($"Declarative ribbon swap failed: {ex}");
        }
    }

    private RibbonCommandRegistry BuildDeclarativeRibbonRegistry()
    {
        var registry = new RibbonCommandRegistry();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in RibbonTabs!.Items)
        {
            if (item is not TabItem { Content: DependencyObject content })
                continue;

            foreach (var element in EnumerateLogicalTree(content))
            {
                if (element is Control control &&
                    RibbonMetadata.TryGetCommandName(control, out var name) &&
                    seen.Add(name))
                {
                    registry.Register(name, new RibbonHandlerCommand(control));
                }
            }
        }

        return registry;
    }

    private static IEnumerable<DependencyObject> EnumerateLogicalTree(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject node)
                continue;

            yield return node;
            foreach (var descendant in EnumerateLogicalTree(node))
                yield return descendant;
        }
    }

    /// <summary>Bridges a declarative command id to an existing ribbon control's Click handler.</summary>
    private sealed class RibbonHandlerCommand : IRibbonCommand
    {
        private readonly Control _source;

        public RibbonHandlerCommand(Control source) => _source = source;

        public void Execute(RibbonCommandContext context)
        {
            if (_source is ButtonBase button)
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
    }
}
