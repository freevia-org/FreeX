using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// The single, app-neutral source of truth for Word-style contextual ribbon tabs: tabs that stay hidden
/// until a selection context is active (a picture is selected, the caret is in a table, …) and that hide
/// again when the context clears — reverting the active tab to a default so the ribbon body never goes
/// blank. This mirrors the imperative show/hide FreeX hand-codes per object type, but driven entirely by
/// the shared <see cref="RibbonTab"/>/<see cref="RibbonTabContext"/> declarations, so each app only has to
/// DECLARE its contextual tabs (id + activation key + colour) and feed the controller the active context.
/// </summary>
public sealed class RibbonContextualTabController
{
    private readonly TabControl _tabs;
    private readonly int _defaultTabIndex;
    private readonly List<Entry> _entries = new();

    private readonly record struct Entry(TabItem Tab, string ActivationKey);

    /// <param name="tabs">The ribbon tab control whose contextual tabs are managed.</param>
    /// <param name="defaultTabIndex">
    /// The tab to fall back to when the active contextual tab is hidden (Word reverts to Home). Defaults to
    /// 1 — index 0 being the Word-style File/Backstage pill.
    /// </param>
    public RibbonContextualTabController(TabControl tabs, int defaultTabIndex = 1)
    {
        _tabs = tabs;
        _defaultTabIndex = defaultTabIndex;
    }

    /// <summary>
    /// Registers a contextual tab. It starts hidden and is shown only while <paramref name="activationKey"/>
    /// is active. The tab header is tinted with the context colour (a Word touch) when one is supplied.
    /// </summary>
    public void Register(TabItem tab, string activationKey, RibbonContextColor color = RibbonContextColor.None)
    {
        tab.Visibility = Visibility.Collapsed;
        if (ToBrush(color) is { } brush)
            tab.Foreground = brush;
        _entries.Add(new Entry(tab, activationKey));
    }

    /// <summary>
    /// Applies the active context: shows every registered tab whose activation key is active and hides the
    /// rest. If the currently-selected tab is one being hidden, selection reverts to the default tab.
    /// </summary>
    public void Apply(RibbonContextState state)
    {
        var revert = false;
        foreach (var entry in _entries)
        {
            var visible = state.IsActive(entry.ActivationKey);
            entry.Tab.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible && ReferenceEquals(_tabs.SelectedItem, entry.Tab))
                revert = true;
        }

        if (revert && _defaultTabIndex >= 0 && _defaultTabIndex < _tabs.Items.Count)
            _tabs.SelectedIndex = _defaultTabIndex;
    }

    // The Office contextual-tab accent palette (header tint only; the body stays the normal ribbon surface).
    private static Brush? ToBrush(RibbonContextColor color)
    {
        Color? c = color switch
        {
            RibbonContextColor.Green => Color.FromRgb(0x21, 0x7A, 0x3C),
            RibbonContextColor.Orange => Color.FromRgb(0xC5, 0x6A, 0x11),
            RibbonContextColor.Purple => Color.FromRgb(0x68, 0x39, 0xB6),
            RibbonContextColor.Blue => Color.FromRgb(0x1F, 0x5C, 0xA8),
            RibbonContextColor.Red => Color.FromRgb(0xB3, 0x29, 0x29),
            RibbonContextColor.Teal => Color.FromRgb(0x0F, 0x6D, 0x8C),
            _ => null
        };
        if (c is null)
            return null;
        var brush = new SolidColorBrush(c.Value);
        brush.Freeze();
        return brush;
    }
}
