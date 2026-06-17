using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;

namespace FreeW.App.Host;

/// <summary>
/// A Word-style KeyTips overlay over the rendered ribbon. Pressing <c>Alt</c> shows small letter badges
/// over each ribbon tab; pressing a tab's letter activates that tab and shows badges over its controls;
/// pressing a control's letter invokes it. <c>Esc</c> steps back a level, <c>Alt</c> or a second tap
/// dismisses. The overlay walks the rendered ribbon's visual tree (it depends only on the rendered WPF
/// controls and the metadata the shared renderer stamps on them), so it lives entirely app-side and
/// needs no model or renderer changes.
/// </summary>
internal sealed class KeyTipsOverlay
{
    private readonly Window _window;
    private readonly TabControl _ribbonTabs;
    private readonly Panel _overlayHost;
    private readonly Canvas _canvas = new() { IsHitTestVisible = false };

    private enum Stage { Hidden, Tabs, Controls }
    private Stage _stage = Stage.Hidden;

    // Active badges at the current stage: keytip letters -> the target element to activate.
    private readonly Dictionary<string, FrameworkElement> _targets = new(StringComparer.OrdinalIgnoreCase);

    private KeyTipsOverlay(Window window, TabControl ribbonTabs, Panel overlayHost)
    {
        _window = window;
        _ribbonTabs = ribbonTabs;
        _overlayHost = overlayHost;
    }

    /// <summary>
    /// Install the KeyTips overlay on <paramref name="window"/> for the ribbon <paramref name="ribbonTabs"/>.
    /// <paramref name="overlayHost"/> is a panel that spans the window client area (the shell grid) over
    /// which the badge canvas is drawn.
    /// </summary>
    public static void Install(Window window, TabControl ribbonTabs, Panel overlayHost)
    {
        var overlay = new KeyTipsOverlay(window, ribbonTabs, overlayHost);
        overlay._overlayHost.Children.Add(overlay._canvas);
        Panel.SetZIndex(overlay._canvas, 10_000);

        // Alt down (the system key) toggles the overlay; we mark it handled so the menu bar doesn't claim it.
        window.PreviewKeyDown += overlay.OnPreviewKeyDown;
        // A real click anywhere or losing focus dismisses any open overlay.
        window.PreviewMouseDown += (_, _) => overlay.Hide();
        window.Deactivated += (_, _) => overlay.Hide();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.LeftAlt || key == Key.RightAlt)
        {
            if (_stage == Stage.Hidden)
                ShowTabs();
            else
                Hide();
            e.Handled = true;
            return;
        }

        if (_stage == Stage.Hidden)
            return;

        if (key == Key.Escape)
        {
            if (_stage == Stage.Controls)
                ShowTabs();
            else
                Hide();
            e.Handled = true;
            return;
        }

        // A letter/digit selects the matching badge at the current stage.
        var letter = KeyToLetter(key);
        if (letter is null)
            return;

        if (_targets.TryGetValue(letter, out var target))
        {
            e.Handled = true;
            if (_stage == Stage.Tabs)
                ActivateTab(target);
            else
                ActivateControl(target);
        }
    }

    // --- Stage transitions ----------------------------------------------------------------------

    private void ShowTabs()
    {
        _stage = Stage.Tabs;
        _targets.Clear();
        _canvas.Children.Clear();

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _ribbonTabs.Items.OfType<TabItem>())
        {
            var header = item.Header?.ToString() ?? "";
            var keyTip = DeriveKeyTip(header, used);
            _targets[keyTip] = item;
            // Badge anchored to the tab header; if the tab isn't realized yet, fall back to the strip.
            if (Locate(item) is { } rect)
                AddBadge(keyTip, rect.Left + 4, rect.Bottom - 4);
        }
    }

    private void ActivateTab(FrameworkElement target)
    {
        if (target is TabItem item)
        {
            item.IsSelected = true;
            // Let the tab content realize before measuring control positions.
            _window.Dispatcher.BeginInvoke(new Action(ShowControls), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void ShowControls()
    {
        _stage = Stage.Controls;
        _targets.Clear();
        _canvas.Children.Clear();

        var content = (_ribbonTabs.SelectedItem as TabItem)?.Content as DependencyObject;
        if (content is null)
            return;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in CommandableElements(content))
        {
            var label = LabelOf(element);
            if (string.IsNullOrEmpty(label))
                continue;
            var keyTip = !string.IsNullOrEmpty(RibbonTooltip.GetKeyTip(element))
                ? RibbonTooltip.GetKeyTip(element)!.ToUpperInvariant()
                : DeriveKeyTip(label, used);
            if (!used.Add(keyTip) && _targets.ContainsKey(keyTip))
                continue;
            _targets[keyTip] = element;
            if (Locate(element) is { } rect)
                AddBadge(keyTip, rect.Left + rect.Width / 2 - 7, rect.Top + 2);
        }
    }

    private void ActivateControl(FrameworkElement target)
    {
        Hide();
        switch (target)
        {
            case ButtonBase button:
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                break;
            case ComboBox combo:
                combo.Focus();
                combo.IsDropDownOpen = true;
                break;
            default:
                target.Focus();
                break;
        }
    }

    private void Hide()
    {
        if (_stage == Stage.Hidden)
            return;
        _stage = Stage.Hidden;
        _targets.Clear();
        _canvas.Children.Clear();
    }

    // --- Visual-tree helpers --------------------------------------------------------------------

    // Every commandable rendered control in the tab content: buttons, toggles and combo boxes the shared
    // renderer stamped with a command name. Separators / labels are skipped (no command name).
    private static IEnumerable<FrameworkElement> CommandableElements(DependencyObject root)
    {
        foreach (var child in Descendants(root))
        {
            if (child is not FrameworkElement fe)
                continue;
            var isCommandable = fe is ButtonBase or ComboBox;
            if (!isCommandable)
                continue;
            // Skip the inner parts of templated controls (e.g. a ComboBox's toggle button) by requiring a
            // command name OR being a top-level Button/ComboBox the renderer produced.
            if (string.IsNullOrEmpty(RibbonMetadata.GetCommandName(fe)) && fe is not ComboBox && fe is not Button)
                continue;
            yield return fe;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }

    // A control's human-readable label: the tooltip title the renderer defaults to the control's label,
    // else the command name's trailing segment.
    private static string LabelOf(FrameworkElement element)
    {
        var title = RibbonTooltip.GetTitle(element);
        if (!string.IsNullOrEmpty(title))
            return title;
        var command = RibbonMetadata.GetCommandName(element);
        if (!string.IsNullOrEmpty(command))
        {
            var dot = command.LastIndexOf('.');
            return dot >= 0 ? command[(dot + 1)..] : command;
        }
        return "";
    }

    // The bounding rectangle of a rendered element in the overlay host's coordinate space, or null when
    // it isn't currently realized/visible.
    private Rect? Locate(FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return null;
        try
        {
            var topLeft = element.TransformToVisual(_overlayHost).Transform(new Point(0, 0));
            return new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            // Not in the same visual tree (e.g. element not yet connected) — skip its badge.
            return null;
        }
    }

    private void AddBadge(string text, double x, double y)
    {
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(3, 0, 3, 0),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            }
        };
        Canvas.SetLeft(badge, Math.Max(0, x));
        Canvas.SetTop(badge, Math.Max(0, y));
        _canvas.Children.Add(badge);
    }

    // Derive a unique single-letter keytip from a label (first letter, then later letters, then digits),
    // deduped against the letters already used at this stage.
    private static string DeriveKeyTip(string label, HashSet<string> used)
    {
        var letters = label.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray();
        foreach (var c in letters)
        {
            var candidate = c.ToString();
            if (used.Add(candidate))
                return candidate;
        }
        for (var c = 'A'; c <= 'Z'; c++)
        {
            var candidate = c.ToString();
            if (used.Add(candidate))
                return candidate;
        }
        return "?";
    }

    private static string? KeyToLetter(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
            return ((char)('A' + (key - Key.A))).ToString();
        if (key >= Key.D0 && key <= Key.D9)
            return ((char)('0' + (key - Key.D0))).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return ((char)('0' + (key - Key.NumPad0))).ToString();
        return null;
    }
}
