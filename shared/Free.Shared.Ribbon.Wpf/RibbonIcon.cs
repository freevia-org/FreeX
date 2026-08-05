using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// A vector ribbon glyph. Draws either the geometry for an explicit <see cref="Kind"/> or, when a
/// <see cref="CommandName"/> is supplied, the glyph the host resolver maps that command id to (falling
/// back to <see cref="Kind"/>, then the generic glyph). Ported from FreeX's app-neutral RibbonIcon.
/// </summary>
public class RibbonIcon : Viewbox
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(
            nameof(Kind),
            typeof(RibbonCommandIconKind),
            typeof(RibbonIcon),
            new PropertyMetadata(RibbonCommandIconKind.Generic, OnVisualPropertyChanged));

    public static readonly DependencyProperty CommandNameProperty =
        DependencyProperty.Register(
            nameof(CommandName),
            typeof(string),
            typeof(RibbonIcon),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(
            nameof(IconSize),
            typeof(double),
            typeof(RibbonIcon),
            new PropertyMetadata(14d, OnVisualPropertyChanged));

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(
            nameof(Foreground),
            typeof(Brush),
            typeof(RibbonIcon),
            new PropertyMetadata(Brushes.Black, OnVisualPropertyChanged));

    public RibbonCommandIconKind Kind
    {
        get => (RibbonCommandIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string CommandName
    {
        get => (string)GetValue(CommandNameProperty);
        set => SetValue(CommandNameProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    // The vector child is built once (lazily on first load) and rebuilt only when a visual property
    // actually changes — NOT on every Loaded. A WPF TabControl re-raises Loaded each time a tab is
    // re-selected, so rebuilding on Loaded re-tessellated every ribbon glyph on every tab switch, which
    // made tab changes visibly laggy. Building once keeps switching instant.
    private bool _built;

    public RibbonIcon()
    {
        Stretch = Stretch.Uniform;
        Width = IconSize;
        Height = IconSize;
        SnapsToDevicePixels = true;
        Loaded += (_, _) => { if (!_built) Rebuild(); };
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RibbonIcon icon)
            icon.Rebuild();
    }

    private void Rebuild()
    {
        _built = true;
        Width = IconSize;
        Height = IconSize;
        var brush = Foreground ?? Brushes.Black;
        var fallback = new RibbonCommandIcon(Kind);
        Child = string.IsNullOrWhiteSpace(CommandName)
            ? RibbonIconFactory.CreateIcon(fallback, IconSize, brush)
            : RibbonIconFactory.CreateCommandIcon(CommandName, fallback, IconSize, brush);
    }
}
