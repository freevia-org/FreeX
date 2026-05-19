using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Freexcel.App.Host;

public sealed class RibbonIcon : Viewbox
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(
            nameof(Kind),
            typeof(RibbonCommandIconKind),
            typeof(RibbonIcon),
            new PropertyMetadata(RibbonCommandIconKind.Generic, OnVisualPropertyChanged));

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

    public RibbonIcon()
    {
        Stretch = Stretch.Uniform;
        Width = IconSize;
        Height = IconSize;
        SnapsToDevicePixels = true;
        Loaded += (_, _) => Rebuild();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RibbonIcon icon)
            icon.Rebuild();
    }

    private void Rebuild()
    {
        Width = IconSize;
        Height = IconSize;
        Child = RibbonIconFactory.CreateIcon(
            new RibbonCommandIcon(Kind),
            IconSize,
            Foreground ?? Brushes.Black);
    }
}
