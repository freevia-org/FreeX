using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

internal static class RibbonWpfPopupAdapter
{
    public static void Configure(
        ContextMenu contextMenu,
        FrameworkElement anchor,
        FrameworkElement resourceHost)
    {
        var contract = RibbonPopupInteractionContract.CollapsedGroup;
        var chrome = RibbonVisualMetrics.PopupChrome;
        contextMenu.PlacementTarget = anchor;
        contextMenu.Placement = contract.RepositionAtScreenEdge
            ? PlacementMode.Custom
            : contract.Placement switch
            {
                RibbonPopupPlacement.BelowAnchor => PlacementMode.Bottom,
                RibbonPopupPlacement.AboveAnchor => PlacementMode.Top,
                _ => PlacementMode.Bottom,
            };
        contextMenu.MinWidth = chrome.MinWidth;
        contextMenu.MaxWidth = chrome.MaxWidth;
        contextMenu.Padding = ToThickness(chrome.PopupPadding);
        contextMenu.Background = FindBrush(resourceHost, "ThemeRibbonSurfaceBrush", "FreeXRibbonSurfaceBrush", Brushes.White);
        contextMenu.BorderBrush = FindBrush(resourceHost, "ThemeRibbonBorderBrush", "FreeXBorderBrush", Brushes.Gray);
        contextMenu.BorderThickness = new Thickness(chrome.BorderThickness);
        contextMenu.Foreground = FindBrush(resourceHost, "ThemeNeutralTextBrush", "FreeXTextBrush", Brushes.Black);
        contextMenu.Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            Direction = 270,
            ShadowDepth = chrome.ShadowDepth,
            BlurRadius = chrome.ShadowBlurRadius,
            Opacity = chrome.ShadowOpacity,
        };
        contextMenu.SnapsToDevicePixels = true;

        var topLevelItems = contextMenu.Items.OfType<MenuItem>().ToArray();
        foreach (var item in topLevelItems)
            ConfigureMenuItem(item, parent: null, topLevelItems, contextMenu, contract, chrome);

        if (contract.RepositionAtScreenEdge)
        {
            contextMenu.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
            {
                var screenAnchorPixels = anchor.PointToScreen(new Point(0, 0));
                var transformFromDevice = PresentationSource.FromVisual(anchor)?.CompositionTarget?.TransformFromDevice
                    ?? Matrix.Identity;
                var screenAnchor = transformFromDevice.Transform(screenAnchorPixels);
                var result = RibbonPopupPlacementPlanner.Plan(
                    new RibbonPopupRect(screenAnchor.X, screenAnchor.Y, targetSize.Width, targetSize.Height),
                    new RibbonPopupRect(0, 0, popupSize.Width, popupSize.Height),
                    ResolveWorkArea(screenAnchorPixels, anchor),
                    contract);
                return
                [
                    new CustomPopupPlacement(
                        new Point(result.X - screenAnchor.X, result.Y - screenAnchor.Y),
                        PopupPrimaryAxis.None),
                ];
            };
        }

        contextMenu.StaysOpen = false;
        contextMenu.Opened += (_, _) =>
        {
            if (!contract.FocusFirstEnabledItemOnOpen)
                return;

            var items = contextMenu.Items.OfType<MenuItem>().ToArray();
            var states = items
                .Select(item => new RibbonPopupFocusItem(item.Focusable, item.IsEnabled))
                .ToArray();
            var index = RibbonPopupInteractionPlanner.FindFirstFocusableItem(states);
            if (index >= 0)
            {
                contextMenu.Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    new Action(() =>
                    {
                        if (contextMenu.IsOpen)
                            Keyboard.Focus(items[index]);
                    }));
            }
        };
        contextMenu.Closed += (_, _) =>
        {
            if (contract.RestoreFocusToAnchorOnClose)
                anchor.Focus();
        };
        contextMenu.AddHandler(
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler((_, args) =>
            {
                if (args.Source is MenuItem)
                    return;

                var dismissal = args.Key switch
                {
                    Key.Escape => RibbonPopupInteractionPlanner.PlanDismissal(
                        RibbonPopupDismissKey.Escape, isNestedSubmenu: false, contract),
                    Key.Left => RibbonPopupInteractionPlanner.PlanDismissal(
                        RibbonPopupDismissKey.Left, isNestedSubmenu: false, contract),
                    _ => RibbonPopupDismissal.None,
                };
                if (dismissal == RibbonPopupDismissal.ClosePopup)
                {
                    contextMenu.IsOpen = false;
                    args.Handled = true;
                }
            }),
            handledEventsToo: true);
    }

    private static void ConfigureMenuItem(
        MenuItem item,
        MenuItem? parent,
        IReadOnlyList<MenuItem> siblings,
        ContextMenu contextMenu,
        RibbonPopupInteractionContract contract,
        RibbonPopupChromeMetrics chrome)
    {
        item.MinHeight = parent is null ? chrome.ItemMinHeight : chrome.Submenu.ItemMinHeight;
        item.Padding = ToThickness(parent is null ? chrome.ItemPadding : chrome.Submenu.ItemPadding);
        item.HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var children = item.Items.OfType<MenuItem>().ToArray();
        foreach (var child in children)
            ConfigureMenuItem(child, item, children, contextMenu, contract, chrome);

        if (children.Length > 0)
        {
            item.ApplyTemplate();
            ConfigureSubmenuPopup(item, contract);
            item.Loaded += (_, _) => ConfigureSubmenuPopup(item, contract);
            item.SubmenuOpened += (_, _) =>
            {
                ConfigureSubmenuPopup(item, contract);
                // Some themes create or connect the popup as part of opening the submenu. Give
                // that template one layout pass before falling back to its native placement.
                item.Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() => ConfigureSubmenuPopup(item, contract)));
            };
        }

        item.AddHandler(
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler((_, args) =>
            {
                if (args.Handled || !ReferenceEquals(args.Source, item))
                    return;

                var dismissal = args.Key switch
                {
                    Key.Escape => RibbonPopupInteractionPlanner.PlanDismissal(
                        RibbonPopupDismissKey.Escape, parent is not null, contract),
                    Key.Left => RibbonPopupInteractionPlanner.PlanDismissal(
                        RibbonPopupDismissKey.Left, parent is not null, contract),
                    _ => RibbonPopupDismissal.None,
                };
                if (dismissal == RibbonPopupDismissal.CloseSubmenu && parent is not null)
                {
                    parent.IsSubmenuOpen = false;
                    if (contract.Submenu.RestoreFocusToParentOnClose)
                        parent.Focus();
                    args.Handled = true;
                    return;
                }

                if (dismissal == RibbonPopupDismissal.ClosePopup)
                {
                    contextMenu.IsOpen = false;
                    args.Handled = true;
                    return;
                }

                if (args.Key == Key.Right &&
                    RibbonPopupInteractionPlanner.PlanNavigation(
                        RibbonPopupNavigationKey.Right, children.Length > 0, contract) == RibbonPopupNavigation.OpenSubmenu)
                {
                    item.IsSubmenuOpen = true;
                    FocusFirstEnabledChild(item, children, contract);
                    args.Handled = true;
                    return;
                }

                if (parent is not null && !contract.Submenu.TraverseEnabledItems ||
                    parent is null && !contract.TraverseEnabledItems ||
                    args.Key is not (Key.Up or Key.Down or Key.Home or Key.End))
                    return;

                var currentIndex = Array.IndexOf(siblings.ToArray(), item);
                if (currentIndex < 0)
                    return;
                var states = siblings
                    .Select(candidate => new RibbonPopupFocusItem(candidate.Focusable, candidate.IsEnabled))
                    .ToArray();
                var targetIndex = args.Key switch
                {
                    Key.Home => RibbonPopupInteractionPlanner.FindFirstFocusableItem(states),
                    Key.End => RibbonPopupInteractionPlanner.FindLastFocusableItem(states),
                    Key.Up => RibbonPopupInteractionPlanner.FindAdjacentFocusableItem(states, currentIndex, -1),
                    Key.Down => RibbonPopupInteractionPlanner.FindAdjacentFocusableItem(states, currentIndex, 1),
                    _ => -1,
                };
                if (targetIndex >= 0 && siblings[targetIndex].Focus())
                    args.Handled = true;
            }),
            handledEventsToo: true);
    }

    private static void ConfigureSubmenuPopup(
        MenuItem item,
        RibbonPopupInteractionContract contract)
    {
        item.ApplyTemplate();
        if (FindTemplatePopup(item) is not Popup popup)
            return;

        popup.PlacementTarget = item;
        popup.Placement = contract.Submenu.RepositionAtScreenEdge
            ? PlacementMode.Custom
            : PlacementMode.Right;
        if (!contract.Submenu.RepositionAtScreenEdge)
            return;

        popup.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        {
            var screenAnchorPixels = item.PointToScreen(new Point(0, 0));
            var transformFromDevice = PresentationSource.FromVisual(item)?.CompositionTarget?.TransformFromDevice
                ?? Matrix.Identity;
            var screenAnchor = transformFromDevice.Transform(screenAnchorPixels);
            var result = RibbonPopupPlacementPlanner.PlanSubmenu(
                new RibbonPopupRect(screenAnchor.X, screenAnchor.Y, targetSize.Width, targetSize.Height),
                new RibbonPopupRect(0, 0, popupSize.Width, popupSize.Height),
                ResolveWorkArea(screenAnchorPixels, item),
                contract.Submenu);
            return
            [
                new CustomPopupPlacement(
                    new Point(result.X - screenAnchor.X, result.Y - screenAnchor.Y),
                    PopupPrimaryAxis.None),
            ];
        };
    }

    private static Popup? FindTemplatePopup(MenuItem item)
    {
        var visited = new HashSet<DependencyObject>();
        return FindTemplatePopup(item, visited);
    }

    private static Popup? FindTemplatePopup(
        DependencyObject current,
        ISet<DependencyObject> visited)
    {
        if (!visited.Add(current))
            return null;
        if (current is Popup popup)
            return popup;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
        {
            var result = FindTemplatePopup(VisualTreeHelper.GetChild(current, index), visited);
            if (result is not null)
                return result;
        }

        return null;
    }

    private static void FocusFirstEnabledChild(
        MenuItem parent,
        IReadOnlyList<MenuItem> children,
        RibbonPopupInteractionContract contract)
    {
        if (!contract.Submenu.FocusFirstEnabledItemOnOpen)
            return;

        var states = children
            .Select(child => new RibbonPopupFocusItem(child.Focusable, child.IsEnabled))
            .ToArray();
        var index = RibbonPopupInteractionPlanner.FindFirstFocusableItem(states);
        if (index < 0)
            return;

        parent.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (parent.IsSubmenuOpen)
                    Keyboard.Focus(children[index]);
            }));
    }

    private static RibbonPopupRect ResolveWorkArea(Point anchorDevicePoint, FrameworkElement anchor)
    {
        var fallback = new RibbonPopupRect(
            SystemParameters.WorkArea.Left,
            SystemParameters.WorkArea.Top,
            SystemParameters.WorkArea.Width,
            SystemParameters.WorkArea.Height);
        var monitor = MonitorFromPoint(
            new Win32Point((int)Math.Round(anchorDevicePoint.X), (int)Math.Round(anchorDevicePoint.Y)),
            MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return fallback;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return fallback;

        var transform = PresentationSource.FromVisual(anchor)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        return RibbonPopupMonitorPlanner.SelectWorkArea(
            new RibbonPopupRect(anchorDevicePoint.X, anchorDevicePoint.Y, 1, 1),
            new[]
            {
                new RibbonPopupMonitorWorkArea(
                    new RibbonPopupRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Width, info.Monitor.Height),
                    NormalizeDeviceRect(info.WorkArea, transform)),
            },
            fallback);
    }

    private static RibbonPopupRect NormalizeDeviceRect(Win32Rect rect, Matrix transform) =>
        RibbonPopupMonitorPlanner.NormalizeFromDevicePixels(
            new RibbonPopupRect(rect.Left, rect.Top, rect.Width, rect.Height),
            new RibbonPopupPoint(0, 0),
            new RibbonPopupPoint(transform.OffsetX, transform.OffsetY),
            scaleX: 1 / transform.M11,
            scaleY: 1 / transform.M22);

    private static Thickness ToThickness(RibbonPopupInsets insets) =>
        new(insets.Left, insets.Top, insets.Right, insets.Bottom);

    private static Brush FindBrush(
        FrameworkElement resourceHost,
        string primaryKey,
        string fallbackKey,
        Brush fallback) =>
        resourceHost.TryFindResource(primaryKey) as Brush ??
        resourceHost.TryFindResource(fallbackKey) as Brush ??
        fallback;

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Win32Point(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Win32Rect Monitor;
        public Win32Rect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Win32Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
