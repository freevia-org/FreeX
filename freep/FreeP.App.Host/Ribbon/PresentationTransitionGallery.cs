using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Free.Shared.Ribbon;

namespace FreeP.App.Host;

/// <summary>
/// WPF preview strip for the common Transitions commands. The full transition catalog remains
/// available through the adjacent More menu; this surface mirrors PowerPoint's visible first row.
/// </summary>
internal static class PresentationTransitionGallery
{
    private static readonly (string CommandId, string Label, TransitionPreview Preview)[] Entries =
    [
        ("freep.transition.none", "None", TransitionPreview.None),
        ("freep.transition.fade", "Fade", TransitionPreview.Fade),
        ("freep.transition.push", "Push", TransitionPreview.Push),
        ("freep.transition.wipe", "Wipe", TransitionPreview.Wipe),
        ("freep.transition.split", "Split", TransitionPreview.Split),
        ("freep.transition.reveal", "Reveal", TransitionPreview.Reveal),
        ("freep.transition.cut", "Cut", TransitionPreview.Cut),
        ("freep.transition.random-bars", "Random Bars", TransitionPreview.RandomBars),
    ];

    public static FrameworkElement Build(IRibbonCommandRegistry registry)
    {
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 1, 2, 0),
        };

        foreach (var entry in Entries)
            strip.Children.Add(BuildButton(entry, registry));

        return strip;
    }

    private static FrameworkElement BuildButton((string CommandId, string Label, TransitionPreview Preview) entry, IRibbonCommandRegistry registry)
    {
        var content = new Grid { Width = 54, Height = 51 };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        content.Children.Add(BuildPreview(entry.Preview));
        var caption = new TextBlock
        {
            Text = entry.Label,
            FontSize = 9,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(1, 1, 1, 0),
        };
        Grid.SetRow(caption, 1);
        content.Children.Add(caption);

        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
            Margin = new Thickness(1, 0, 1, 0),
            ToolTip = entry.Label,
        };
        AutomationProperties.SetName(button, entry.Label);
        button.MouseEnter += (_, _) =>
        {
            button.Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A));
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        };
        button.Click += (_, _) =>
        {
            if (registry.TryGet(new RibbonCommandId(entry.CommandId), out var command) && command is not null)
                command.Execute(RibbonCommandContext.Empty);
        };
        return button;
    }

    private static FrameworkElement BuildPreview(TransitionPreview preview)
    {
        var frame = new Border
        {
            Width = 50,
            Height = 30,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0x9C, 0x9C)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
        };
        var surface = new Grid { ClipToBounds = true };
        surface.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0xE9, 0xE9, 0xE9)) });
        var incoming = new Border { Background = new SolidColorBrush(Color.FromRgb(0xB7, 0xB7, 0xB7)) };

        switch (preview)
        {
            case TransitionPreview.None:
                incoming.Margin = new Thickness(5, 5, 5, 5);
                break;
            case TransitionPreview.Fade:
                incoming.Opacity = 0.62;
                incoming.Margin = new Thickness(8, 6, 8, 6);
                break;
            case TransitionPreview.Push:
                incoming.Width = 31;
                incoming.HorizontalAlignment = HorizontalAlignment.Right;
                break;
            case TransitionPreview.Wipe:
                incoming.Width = 25;
                incoming.HorizontalAlignment = HorizontalAlignment.Left;
                break;
            case TransitionPreview.Split:
                incoming.Width = 18;
                incoming.HorizontalAlignment = HorizontalAlignment.Center;
                break;
            case TransitionPreview.Reveal:
                incoming.Height = 18;
                incoming.VerticalAlignment = VerticalAlignment.Bottom;
                break;
            case TransitionPreview.Cut:
                incoming.Margin = new Thickness(3, 3, 3, 3);
                break;
            case TransitionPreview.RandomBars:
                incoming.Visibility = Visibility.Collapsed;
                for (var index = 0; index < 5; index++)
                {
                    var bar = new Rectangle
                    {
                        Fill = new SolidColorBrush(Color.FromRgb(0xB7, 0xB7, 0xB7)),
                        Width = 5,
                        Height = index % 2 == 0 ? 24 : 17,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5 + index * 9, 0, 0, 0),
                    };
                    surface.Children.Add(bar);
                }
                break;
        }

        surface.Children.Add(incoming);
        frame.Child = surface;
        return frame;
    }

    private enum TransitionPreview
    {
        None,
        Fade,
        Push,
        Wipe,
        Split,
        Reveal,
        Cut,
        RandomBars,
    }
}
