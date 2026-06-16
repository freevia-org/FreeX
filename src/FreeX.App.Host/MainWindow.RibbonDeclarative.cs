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
        if (RibbonTabs is null)
            return;

        try
        {
            // The ribbon is now declarative. Hidden backplane controls (MainWindow.RibbonBackplane.g.cs)
            // hold state and serve as the 'sender' for handlers. Commands bind NATIVELY: each CommandId
            // invokes its MainWindow handler method directly; the control bridge is only a fallback.
            InitializeRibbonControlBackplane();
            var originals = RibbonBackplaneControls;
            var registry = BuildNativeRibbonRegistry();
            foreach (var (name, control) in originals)
            {
                if (!registry.TryGet(name, out _))
                    registry.Register(name, new RibbonHandlerCommand(control));
            }

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
            var renderedByName = CollectControlsByName();
            WireDeclarativeStateSync(originals, renderedByName);
            RepointBackplaneNamesToRenderedControls(renderedByName);
            WireRenderedMenuOpenedHandlers(renderedByName);

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

    /// <summary>
    /// Builds the native command registry: each CommandId is bound directly to its MainWindow
    /// Click-handler method (via the generated <see cref="FreeXRibbonHandlerMap"/>), so command
    /// execution no longer depends on the XAML control tree.
    /// </summary>
    private RibbonCommandRegistry BuildNativeRibbonRegistry()
    {
        var registry = new RibbonCommandRegistry();
        var type = typeof(MainWindow);
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public;

        foreach (var (name, methodName) in FreeXRibbonHandlerMap.Handlers)
        {
            var method = type.GetMethod(methodName, flags, binder: null,
                types: new[] { typeof(object), typeof(RoutedEventArgs) }, modifiers: null)
                ?? type.GetMethod(methodName, flags, binder: null, types: System.Type.EmptyTypes, modifiers: null);
            if (method is not null)
                registry.Register(name, new ReflectiveHandlerCommand(this, method,
                    RibbonBackplaneControls.GetValueOrDefault(name)));
        }

        return registry;
    }

    /// <summary>Invokes a MainWindow handler method (object,RoutedEventArgs) or parameterless.</summary>
    private sealed class ReflectiveHandlerCommand : IRibbonCommand
    {
        private readonly MainWindow _window;
        private readonly System.Reflection.MethodInfo _method;
        private readonly object? _sender;

        public ReflectiveHandlerCommand(MainWindow window, System.Reflection.MethodInfo method, object? sender)
        {
            _window = window;
            _method = method;
            _sender = sender;
        }

        public void Execute(RibbonCommandContext context)
        {
            // Many toggle handlers read their checked state off the BACKPLANE field by name (e.g.
            // BoldButton_Click reads BoldButton.IsChecked; ViewGridlinesChk_Changed reads its sender,
            // which is the backplane chk). A real click flips IsChecked before raising the event, so
            // mirror that here on the backplane toggle so field-reading handlers observe the new state.
            // (The rendered toggle was already flipped by the keytip path; the backplane is a separate
            // control, so this is not a double-flip.)
            if (_sender is ToggleButton backplaneToggle)
                backplaneToggle.IsChecked = backplaneToggle.IsChecked != true;

            // For sender-reading handlers (MenuItem.Tag/Header), prefer the actual clicked WPF element
            // the renderer supplies; otherwise use the backplane control, then the window.
            var sender = (context.Parameters.TryGetValue(RibbonWpfRenderer.SenderKey, out var wpfSender)
                    ? wpfSender
                    : null)
                ?? _sender ?? _window;

            var args = _method.GetParameters().Length == 0
                ? System.Array.Empty<object?>()
                : new object?[] { sender, new RoutedEventArgs() };
            try
            {
                _method.Invoke(_window, args);
            }
            catch (System.Reflection.TargetInvocationException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ribbon command '{_method.Name}' threw: {ex.InnerException}");
            }
        }
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

            // Some ribbon buttons own a context menu that is built imperatively in code (e.g. the Draw
            // "Shapes" gallery in InitializeInsertShapeGalleryContextMenu), not from the declarative
            // menu model. Share that live menu onto the rendered button so a keytip opens the same
            // gallery (the keytip path resets PlacementTarget when it opens the menu).
            if (original is ButtonBase { ContextMenu: { } sourceMenu } &&
                target is ButtonBase targetButton && targetButton.ContextMenu is null)
            {
                targetButton.ContextMenu = sourceMenu;
            }

            // Mirror enablement and help text from the backplane control (which the app updates for
            // context, e.g. multi-window commands disabled with an explanatory description in a
            // lone-window host) onto the rendered control. Names are re-pointed to the rendered control,
            // so the rendered one must carry both — a context-disabled command then exposes no keytip
            // and reports the same help text. Help text is updated alongside IsEnabled by the app, so
            // refreshing it on IsEnabledChanged keeps it live.
            void SyncState()
            {
                target.IsEnabled = original.IsEnabled;
                var help = System.Windows.Automation.AutomationProperties.GetHelpText(original);
                if (!string.IsNullOrEmpty(help))
                    System.Windows.Automation.AutomationProperties.SetHelpText(target, help);
            }
            original.IsEnabledChanged += (_, _) => SyncState();
            SyncState();

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

    /// <summary>
    /// Wires the on-open refresh for rendered dropdowns whose menu reflects live state (e.g. Arrange
    /// All check-marks the current window arrangement). The declarative menu model is static, so the
    /// host attaches the same Opened handler the original XAML used.
    /// </summary>
    private void WireRenderedMenuOpenedHandlers(IReadOnlyDictionary<string, Control> rendered)
    {
        if (rendered.TryGetValue("Arrange All", out var arrangeAll) &&
            arrangeAll is ButtonBase { ContextMenu: { } arrangeMenu })
        {
            arrangeMenu.Opened += ArrangeAllContextMenu_Opened;
        }
    }

    /// <summary>
    /// Re-points each backplane control's original x:Name to the visible rendered control so that
    /// <see cref="FrameworkElement.FindName"/> resolves the on-screen control (e.g. opening
    /// NumberFormatBox's dropdown via keytip is observable). Handlers keep using the backplane C#
    /// fields directly, so their state holders are unaffected — only name-based lookups move.
    /// </summary>
    private void RepointBackplaneNamesToRenderedControls(IReadOnlyDictionary<string, Control> rendered)
    {
        foreach (var (commandName, xName) in RibbonBackplaneControlNames)
        {
            if (!rendered.TryGetValue(commandName, out var target))
                continue;

            try
            {
                UnregisterName(xName);
                RegisterName(xName, target);
            }
            catch (System.ArgumentException)
            {
                // Name not currently registered (or already re-pointed) — leave the existing binding.
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
