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
            // Capture the original controls (the behavior + state backplane) before replacing them.
            var originals = CollectControlsByName();
            var registry = new RibbonCommandRegistry();
            foreach (var (name, control) in originals)
                registry.Register(name, new RibbonHandlerCommand(control));

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

            // Mirror the original controls' visual state (toggles pressed, combo values) onto the
            // rendered controls, so the declarative ribbon reflects the selection like the XAML one.
            WireDeclarativeStateSync(originals, CollectControlsByName());

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

            if (double.TryParse(Environment.GetEnvironmentVariable("FREEX_RIBBON_DECLARATIVE_WIDTH"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var forcedWidth) &&
                forcedWidth > 0)
            {
                WindowState = WindowState.Normal;
                Width = forcedWidth;
            }

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

    /// <summary>Maps each ribbon control (and menu item) to the first instance found, keyed by CommandName.</summary>
    private Dictionary<string, Control> CollectControlsByName()
    {
        var map = new Dictionary<string, Control>(StringComparer.Ordinal);
        foreach (var item in RibbonTabs!.Items)
        {
            if (item is not TabItem { Content: DependencyObject content })
                continue;

            foreach (var element in EnumerateLogicalTree(content))
            {
                if (element is Control control &&
                    RibbonMetadata.TryGetCommandName(control, out var name) &&
                    !map.ContainsKey(name))
                {
                    map[name] = control;
                }
            }
        }

        return map;
    }

    private static void WireDeclarativeStateSync(
        IReadOnlyDictionary<string, Control> originals,
        IReadOnlyDictionary<string, Control> rendered)
    {
        foreach (var (name, original) in originals)
        {
            if (!rendered.TryGetValue(name, out var target))
                continue;

            if (original is ToggleButton sourceToggle && target is ToggleButton targetToggle)
            {
                void Sync() => targetToggle.IsChecked = sourceToggle.IsChecked;
                sourceToggle.Checked += (_, _) => Sync();
                sourceToggle.Unchecked += (_, _) => Sync();
                sourceToggle.Indeterminate += (_, _) => Sync();
                Sync();
            }
            else if (original is ComboBox sourceCombo && target is ComboBox targetCombo)
            {
                void Sync() => targetCombo.Text = sourceCombo.Text;
                sourceCombo.SelectionChanged += (_, _) => Sync();
                sourceCombo.LostFocus += (_, _) => Sync();
                Sync();
            }
        }
    }

    private static IEnumerable<DependencyObject> EnumerateLogicalTree(DependencyObject root)
    {
        // A control's ContextMenu (its dropdown contents) is not always reached by GetChildren,
        // so descend into it explicitly to register the menu-item commands too.
        if (root is FrameworkElement { ContextMenu: { } contextMenu })
        {
            yield return contextMenu;
            foreach (var descendant in EnumerateLogicalTree(contextMenu))
                yield return descendant;
        }

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
            switch (_source)
            {
                case MenuItem menuItem:
                    menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    break;
                case ButtonBase button:
                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    break;
            }
        }
    }
}
