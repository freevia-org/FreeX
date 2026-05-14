using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Freexcel.App.Host;

/// <summary>
/// Attached properties that build an Excel-style two-line tooltip (bold title + grey description)
/// on any FrameworkElement. Usage: local:RibbonTooltip.Title="Bold"
///                                  local:RibbonTooltip.Description="Makes the selected text bold."
/// </summary>
public static class RibbonTooltip
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.RegisterAttached("Title", typeof(string), typeof(RibbonTooltip),
            new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.RegisterAttached("Description", typeof(string), typeof(RibbonTooltip),
            new PropertyMetadata(null, OnChanged));

    public static void SetTitle(DependencyObject o, string v) => o.SetValue(TitleProperty, v);
    public static string? GetTitle(DependencyObject o) => (string?)o.GetValue(TitleProperty);

    public static void SetDescription(DependencyObject o, string v) => o.SetValue(DescriptionProperty, v);
    public static string? GetDescription(DependencyObject o) => (string?)o.GetValue(DescriptionProperty);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;

        var title = GetTitle(fe);
        var desc  = GetDescription(fe);

        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(desc))
        {
            fe.ClearValue(FrameworkElement.ToolTipProperty);
            return;
        }

        var panel = new StackPanel { MaxWidth = 270 };

        if (!string.IsNullOrEmpty(title))
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, string.IsNullOrEmpty(desc) ? 0 : 3)
            });

        if (!string.IsNullOrEmpty(desc))
            panel.Children.Add(new TextBlock
            {
                Text = desc,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5,
                Foreground = Brushes.DimGray
            });

        fe.ToolTip = new ToolTip { Content = panel };
    }
}
