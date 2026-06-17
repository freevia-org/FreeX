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
/// </summary>
public static class RibbonTabControlFactory
{
    // The ribbon surface (selected-tab fill) and accent/hover washes, matching the FreeX palette exactly
    // (FreeXRibbonSurfaceBrush #FFFFFF / FreeXAccentBrush #0F6D8C / FreeXAccentSoftBrush #E6F6FA /
    // FreeXBorderBrush #DADCE0) so FreeW's ribbon reads identically to FreeX.
    private static readonly Brush SurfaceBrush = Freeze(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush AccentBrush = Freeze(Color.FromRgb(0x0F, 0x6D, 0x8C));
    private static readonly Brush AccentSoftBrush = Freeze(Color.FromRgb(0xE6, 0xF6, 0xFA));
    private static readonly Brush BorderBrush = Freeze(Color.FromRgb(0xDA, 0xDC, 0xE0));
    private static readonly Brush TextBrush = Freeze(Color.FromRgb(0x1A, 0x1A, 0x1A));

    /// <summary>
    /// Creates a ribbon <see cref="TabControl"/> with the flat Word/FreeX tab style applied. Tabs added
    /// by the caller pick up the look automatically. Keytip/gallery wiring is unaffected — this only
    /// styles the tab headers and the strip.
    /// </summary>
    public static TabControl Create()
    {
        var tabs = new TabControl
        {
            Background = SurfaceBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 116
        };

        // Flat tab headers via the item-container style; the default TabControl template's TabPanel lays
        // them out horizontally already, so we keep it (a custom ItemsPanel isn't needed for the look).
        tabs.ItemContainerStyle = BuildTabItemStyle();
        return tabs;
    }

    // The flat tab item: transparent, borderless; hover = soft accent wash; selected = white ribbon
    // surface fill plus a 3px accent underline. Comfortable padding and the FreeX 12pt sizing.
    private static Style BuildTabItemStyle()
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 6, 12, 6)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, true));
        style.Setters.Add(new Setter(KeyboardNavigation.IsTabStopProperty, true));
        style.Setters.Add(new Setter(Control.TemplateProperty, BuildTabItemTemplate()));
        return style;
    }

    private static ControlTemplate BuildTabItemTemplate()
    {
        // Border "TabBorder": transparent fill, a 3px (transparent until selected) bottom accent strip,
        // header content vertically centred. Hover/selected handled by template triggers below.
        var border = new FrameworkElementFactory(typeof(Border), "TabBorder");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 3));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 1, 0));
        border.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(TabItem)) { VisualTree = border };

        // Selected: white ribbon surface fill + accent underline (the active-tab look).
        var selected = new Trigger { Property = Selector.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BorderBrushProperty, AccentBrush, "TabBorder"));
        selected.Setters.Add(new Setter(Border.BackgroundProperty, SurfaceBrush, "TabBorder"));
        template.Triggers.Add(selected);

        // Hover (unselected): soft accent wash.
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, AccentSoftBrush, "TabBorder"));
        template.Triggers.Add(hover);

        // Selected + hover keeps the surface fill (don't let the soft wash override the active fill).
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
