using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
            var definition = FreeXRibbon.Build();

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

            if (Environment.GetEnvironmentVariable("FREEX_RIBBON_DECLARATIVE_CAPTURE") == "1")
                Dispatcher.BeginInvoke(new Action(CaptureDeclarativeRibbon), DispatcherPriority.ContextIdle);
        }
        catch (Exception ex)
        {
            // A preview-mode swap must never take down startup.
            System.Diagnostics.Debug.WriteLine($"Declarative ribbon swap failed: {ex}");
        }
    }

    /// <summary>Renders the live (swapped) ribbon tab strip to a PNG and exits — capture-mode proof.</summary>
    private void CaptureDeclarativeRibbon()
    {
        try
        {
            if (RibbonTabs is null)
                return;

            RibbonTabs.UpdateLayout();
            var width = (int)Math.Ceiling(RibbonTabs.ActualWidth);
            var height = (int)Math.Ceiling(RibbonTabs.ActualHeight);
            if (width <= 0 || height <= 0)
                return;

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(RibbonTabs);

            var outputPath = Path.Combine(FindScreenshotDirectory(), "home_live.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(outputPath);
            encoder.Save(stream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Declarative ribbon capture failed: {ex}");
        }
        finally
        {
            Application.Current?.Shutdown();
        }
    }

    private static string FindScreenshotDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FreeX.slnx")))
            dir = dir.Parent;

        var root = dir?.FullName ?? AppContext.BaseDirectory;
        return Path.Combine(root, "screenshots", "ribbon-declarative");
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
