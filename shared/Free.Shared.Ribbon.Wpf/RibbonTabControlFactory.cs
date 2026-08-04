using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Builds the flat Word/FreeX-style ribbon <see cref="TabControl"/> so any app on the shared ribbon
/// gets the same tab chrome automatically. The tab headers carry NO border and a transparent
/// background; hovering fills them with a soft accent wash; the selected tab is filled with the white
/// ribbon surface and carries a coloured accent underline. This mirrors FreeX's <c>TabItem</c> style in
/// <c>MainWindowResources.xaml</c> (flat, accent underline when selected) but authored in code so the
/// shared library stays code-only and app-neutral — an app just calls <see cref="Create"/> instead of
/// <c>new TabControl()</c> and the renderer fills each tab's body as before.
///
/// The styles are attached to the returned control's local resources (keyed on the TabControl/TabItem
/// target types) so they apply to every <see cref="TabItem"/> the app adds without per-item wiring,
/// while leaving the host's other resource lookups (button styles, surface brushes) untouched.
///
/// WS-G round 9: accent tab-underline and hover-wash now consume <c>ThemeAccentBrush</c> /
/// <c>ThemeAccentSoftBrush</c> from the application resource dictionary so each app's ribbon tab
/// strip adopts its brand accent automatically.  Fallback values (FreeX teal) are used when the
/// theme keys are not registered, keeping FreeX/FreeW byte-identical since their accent values
/// haven't changed.
/// </summary>
public static class RibbonTabControlFactory
{
    // Neutral structural brushes — identical across all three brand themes.
    private static readonly Brush SurfaceBrush = Freeze(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush BorderBrush   = Freeze(Color.FromRgb(0xDA, 0xDC, 0xE0));
    private static readonly Brush TextBrush     = Freeze(Color.FromRgb(0x1A, 0x1A, 0x1A));

    // Fallback accent values (FreeX/FreeW teal).  Resolved from Application resources at Create()
    // time so each app can override them by applying its theme before the TabControl is constructed.
    private static readonly Brush FallbackAccentBrush     = Freeze(Color.FromRgb(0x0F, 0x6D, 0x8C));
    private static readonly Brush FallbackAccentSoftBrush = Freeze(Color.FromRgb(0xE6, 0xF6, 0xFA));

    /// <summary>
    /// Creates a ribbon <see cref="TabControl"/> with the flat Word/FreeX tab style applied. Tabs added
    /// by the caller pick up the look automatically. Keytip/gallery wiring is unaffected — this only
    /// styles the tab headers and the strip.
    /// </summary>
    /// <remarks>
    /// WS-G round 9: accent brushes are resolved from <see cref="System.Windows.Application.Current"/>
    /// resources at construction time (<c>ThemeAccentBrush</c> / <c>ThemeAccentSoftBrush</c>).
    /// Call <c>WpfThemeApplier.Apply()</c> before constructing the TabControl so the correct
    /// per-app brand accent is in effect.  Fallback literals (FreeX teal) apply when the keys
    /// are absent.
    /// </remarks>
    public static TabControl Create()
    {
        var tabs = new TabControl
        {
            Background = SurfaceBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = RibbonVisualMetrics.TabContentMinHeight + RibbonTabChromeMetrics.HeaderHeight
        };

        // Resolve accent brushes from Application.Current.Resources (set by WpfThemeApplier) so
        // each app's tab strip adopts its brand accent.  Fall back to the FreeX teal literals when
        // the keys are not registered (tests / design-time hosts that never call Apply).
        var accentBrush     = ResolveTokenBrush("ThemeAccentBrush",     FallbackAccentBrush);
        var accentSoftBrush = ResolveTokenBrush("ThemeAccentSoftBrush", FallbackAccentSoftBrush);

        // Flat tab headers via the item-container style; the default TabControl template's TabPanel lays
        // them out horizontally already, so we keep it (a custom ItemsPanel isn't needed for the look).
        tabs.ItemContainerStyle = BuildTabItemStyle(accentBrush, accentSoftBrush);
        return tabs;
    }

    // Looks up a brush from Application.Current.Resources by key; returns the fallback when the
    // key is absent or Application.Current is null (unit-test / design-time context).
    private static Brush ResolveTokenBrush(string key, Brush fallback)
    {
        try
        {
            if (System.Windows.Application.Current?.Resources[key] is Brush brush)
                return brush;
        }
        catch { /* design-time / headless context — swallow and use fallback */ }
        return fallback;
    }

    // The flat tab item: transparent, borderless; hover = soft accent wash; selected = white ribbon
    // surface fill plus a 3px accent underline. Comfortable padding and the FreeX 12pt sizing.
    private static Style BuildTabItemStyle(Brush accentBrush, Brush accentSoftBrush)
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(
            RibbonTabChromeMetrics.HeaderHorizontalPadding,
            RibbonTabChromeMetrics.HeaderVerticalPadding,
            RibbonTabChromeMetrics.HeaderHorizontalPadding,
            RibbonTabChromeMetrics.HeaderVerticalPadding)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, RibbonTabChromeMetrics.FontSize));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, true));
        style.Setters.Add(new Setter(KeyboardNavigation.IsTabStopProperty, true));
        style.Setters.Add(new Setter(Control.TemplateProperty, BuildTabItemTemplate(accentBrush, accentSoftBrush)));
        return style;
    }

    private static ControlTemplate BuildTabItemTemplate(Brush accentBrush, Brush accentSoftBrush)
    {
        // Border "TabBorder": transparent fill, a 3px (transparent until selected) bottom accent strip,
        // header content vertically centred. Hover/selected handled by template triggers below.
        var border = new FrameworkElementFactory(typeof(Border), "TabBorder");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(
            0, 0, 0, RibbonTabChromeMetrics.SelectedUnderlineThickness));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(
            0, 0, RibbonTabChromeMetrics.InterTabGap, 0));
        border.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(TabItem)) { VisualTree = border };

        // Selected: white ribbon-surface fill + a 3px accent underline (exactly FreeX's TabItem style —
        // no card fill, no accent/bold text; the underline is the active-tab indicator).
        var selected = new Trigger { Property = Selector.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BorderBrushProperty, accentBrush, "TabBorder"));
        selected.Setters.Add(new Setter(Border.BackgroundProperty, SurfaceBrush, "TabBorder"));
        template.Triggers.Add(selected);

        // Hover (unselected): soft accent wash.
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, accentSoftBrush, "TabBorder"));
        template.Triggers.Add(hover);

        // Selected + hover keeps the white surface fill (don't let the soft wash override the active fill).
        var selectedHover = new MultiTrigger();
        selectedHover.Conditions.Add(new Condition(Selector.IsSelectedProperty, true));
        selectedHover.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
        selectedHover.Setters.Add(new Setter(Border.BackgroundProperty, SurfaceBrush, "TabBorder"));
        template.Triggers.Add(selectedHover);

        return template;
    }

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
