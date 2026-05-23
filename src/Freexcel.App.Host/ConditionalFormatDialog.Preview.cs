using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Freexcel.App.Host;

public partial class ConditionalFormatDialog
{
    private static void AddVisualPreview(Panel panel, string ruleType)
    {
        panel.Children.Add(new TextBlock
        {
            Text = "Preview:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 6)
        });

        panel.Children.Add(ruleType switch
        {
            "Color Scale" => BuildColorScalePreview(),
            "Icon Set" => BuildIconSetPreview(),
            _ => BuildDataBarPreview()
        });
    }

    private static Border BuildDataBarPreview() =>
        new()
        {
            Name = "DataBarPreview",
            Height = 28,
            Margin = new Thickness(0, 0, 0, 12),
            BorderBrush = Brushes.DarkGray,
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                Children =
                {
                    new Border
                    {
                        Width = 150,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                        Background = new LinearGradientBrush(Color.FromRgb(91, 155, 213), Color.FromRgb(189, 215, 238), 0)
                    },
                    new TextBlock
                    {
                        Text = "123",
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    }
                }
            }
        };

    private static Border BuildColorScalePreview() =>
        new()
        {
            Name = "ColorScalePreview",
            Height = 28,
            Margin = new Thickness(0, 0, 0, 12),
            BorderBrush = Brushes.DarkGray,
            BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(99, 190, 123), 0),
                    new(Color.FromRgb(255, 235, 132), 0.5),
                    new(Color.FromRgb(248, 105, 107), 1)
                })
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            }
        };

    private static StackPanel BuildIconSetPreview() =>
        new()
        {
            Name = "IconSetPreview",
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                new TextBlock { Text = "\u25b2", Foreground = Brushes.Green, FontSize = 18, Margin = new Thickness(0, 0, 10, 0) },
                new TextBlock { Text = "\u25b6", Foreground = Brushes.Goldenrod, FontSize = 18, Margin = new Thickness(0, 0, 10, 0) },
                new TextBlock { Text = "\u25bc", Foreground = Brushes.Red, FontSize = 18 }
            }
        };
}
