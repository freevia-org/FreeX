using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;

namespace FreeP.App.Avalonia;

/// <summary>Avalonia-native preview strip for the common PowerPoint transition commands.</summary>
internal static class PresentationTransitionGallery
{
    private static readonly (string CommandId, string LabelKey, TransitionPreview Preview)[] Entries =
    [
        ("freep.transition.none", "Ribbon_Command_TransitionNone_Label", TransitionPreview.None),
        ("freep.transition.fade", "Ribbon_Command_TransitionFade_Label", TransitionPreview.Fade),
        ("freep.transition.push", "Ribbon_Command_TransitionPush_Label", TransitionPreview.Push),
        ("freep.transition.wipe", "Ribbon_Command_TransitionWipe_Label", TransitionPreview.Wipe),
        ("freep.transition.split", "Ribbon_Command_TransitionSplit_Label", TransitionPreview.Split),
        ("freep.transition.reveal", "Ribbon_Command_TransitionReveal_Label", TransitionPreview.Reveal),
        ("freep.transition.cut", "Ribbon_Command_TransitionCut_Label", TransitionPreview.Cut),
        ("freep.transition.random-bars", "Ribbon_Command_TransitionRandomBars_Label", TransitionPreview.RandomBars),
    ];

    public static Control Build(IRibbonCommandRegistry registry)
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

    private static Control BuildButton(
        (string CommandId, string LabelKey, TransitionPreview Preview) entry,
        IRibbonCommandRegistry registry)
    {
        var label = UiText.Get(entry.LabelKey);
        var content = new Grid { Width = 54, Height = 51 };
        content.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        content.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        content.Children.Add(BuildPreview(entry.Preview));
        var caption = new TextBlock
        {
            Text = label,
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
            Margin = new Thickness(1, 0),
        };
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
        button.PointerEntered += (_, _) => SetHover(button, true);
        button.PointerExited += (_, _) => SetHover(button, false);
        button.Click += (_, _) =>
        {
            if (registry.TryGet(new RibbonCommandId(entry.CommandId), out var command) && command is not null)
                command.Execute(RibbonCommandContext.Empty);
        };
        return button;
    }

    private static Control BuildPreview(TransitionPreview preview)
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
            case TransitionPreview.None: incoming.Margin = new Thickness(5); break;
            case TransitionPreview.Fade: incoming.Opacity = 0.62; incoming.Margin = new Thickness(8, 6); break;
            case TransitionPreview.Push: incoming.Width = 31; incoming.HorizontalAlignment = HorizontalAlignment.Right; break;
            case TransitionPreview.Wipe: incoming.Width = 25; incoming.HorizontalAlignment = HorizontalAlignment.Left; break;
            case TransitionPreview.Split: incoming.Width = 18; incoming.HorizontalAlignment = HorizontalAlignment.Center; break;
            case TransitionPreview.Reveal: incoming.Height = 18; incoming.VerticalAlignment = VerticalAlignment.Bottom; break;
            case TransitionPreview.Cut: incoming.Margin = new Thickness(3); break;
            case TransitionPreview.RandomBars:
                incoming.IsVisible = false;
                for (var index = 0; index < 5; index++)
                {
                    surface.Children.Add(new Rectangle
                    {
                        Fill = new SolidColorBrush(Color.FromRgb(0xB7, 0xB7, 0xB7)),
                        Width = 5,
                        Height = index % 2 == 0 ? 24 : 17,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5 + index * 9, 0, 0, 0),
                    });
                }
                break;
        }

        surface.Children.Add(incoming);
        frame.Child = surface;
        return frame;
    }

    private static void SetHover(Button button, bool hovering)
    {
        button.Background = hovering ? new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB)) : Brushes.Transparent;
        button.BorderBrush = hovering ? new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)) : Brushes.Transparent;
    }

    private enum TransitionPreview { None, Fade, Push, Wipe, Split, Reveal, Cut, RandomBars }
}
