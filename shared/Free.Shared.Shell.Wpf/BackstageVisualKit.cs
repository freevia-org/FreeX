using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// The small code-built visual helpers shared by the sister apps' Office-style Backstage panes
/// (Heading / SubHeading / Field / TemplateTile / LinkButton / Scroll / Or). The look is identical across
/// FreeP and FreeW; the only per-app differences — the in-content link accent and the New-tile aspect
/// (portrait for FreeW's document, landscape for FreeP's slide) — are supplied at construction. No file IO
/// or app model is referenced here; callers wire the click callbacks.
/// </summary>
public sealed class BackstageVisualKit
{
    private static readonly Brush HeadingBrush = Freeze(ToColor(BackstageVisualContract.Theme.PrimaryText));
    private static readonly Brush MutedBrush = Freeze(ToColor(BackstageVisualContract.Theme.SecondaryText));
    private static readonly Brush TileBorderBrush = Freeze(Color.FromRgb(0xD0, 0xD7, 0xE5));
    private static readonly Brush TileInnerBorderBrush = Freeze(Color.FromRgb(0xE2, 0xE6, 0xEF));

    private readonly Brush _linkBrush;
    private readonly double _tileWidth;
    private readonly double _tileHeight;

    /// <summary>
    /// Builds the kit for one app's backstage.
    /// </summary>
    /// <param name="linkColor">The in-content link accent (FreeP brick / FreeW teal).</param>
    /// <param name="tileWidth">The New-tile preview width (FreeW 150 / FreeP 190).</param>
    /// <param name="tileHeight">The New-tile preview height (FreeW 190 / FreeP 150).</param>
    public BackstageVisualKit(Color linkColor, double tileWidth, double tileHeight)
    {
        _linkBrush = Freeze(linkColor);
        _tileWidth = tileWidth;
        _tileHeight = tileHeight;
    }

    /// <summary>The shared heading text colour (#333333), exposed for ad-hoc pane text.</summary>
    public Brush Heading => HeadingBrush;

    /// <summary>The shared muted/secondary text colour (#707070), exposed for ad-hoc pane text.</summary>
    public Brush Muted => MutedBrush;

    /// <summary>This app's in-content link accent.</summary>
    public Brush Link => _linkBrush;

    /// <summary>A large light pane title.</summary>
    public TextBlock HeadingText(string text) => new()
    {
        Text = text,
        FontSize = BackstageVisualContract.Pane.HeadingFontSize,
        FontWeight = FontWeights.Light,
        Foreground = HeadingBrush,
        Margin = ToThickness(BackstageVisualContract.Pane.HeadingMargin)
    };

    /// <summary>A semibold section sub-heading.</summary>
    public TextBlock SubHeading(string text) => new()
    {
        Text = text,
        FontSize = BackstageVisualContract.Pane.SectionHeaderFontSize,
        FontWeight = FontWeights.SemiBold,
        Foreground = HeadingBrush,
        Margin = ToThickness(BackstageVisualContract.Pane.SectionHeaderMargin)
    };

    /// <summary>A labelled value row (fixed-width label + wrapping value).</summary>
    public UIElement Field(string label, string value)
    {
        var grid = new Grid { Margin = ToThickness(BackstageVisualContract.Pane.DetailGridMargin) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BackstageVisualContract.Pane.DetailLabelColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var name = new TextBlock
        {
            Text = label,
            Foreground = MutedBrush,
            FontSize = BackstageVisualContract.Pane.DetailFontSize
        };
        Grid.SetColumn(name, 0);
        grid.Children.Add(name);

        var content = new TextBlock
        {
            Text = value,
            Foreground = HeadingBrush,
            FontSize = BackstageVisualContract.Pane.DetailFontSize,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    /// <summary>An Office-style New tile: a bordered card with a blank "page" preview and a caption.</summary>
    public UIElement TemplateTile(string caption, Action onClick)
    {
        var preview = new Border
        {
            Width = _tileWidth,
            Height = _tileHeight,
            Background = Brushes.White,
            BorderBrush = TileBorderBrush,
            BorderThickness = new Thickness(1),
            Child = new Border
            {
                Margin = new Thickness(18),
                Background = Brushes.White,
                BorderBrush = TileInnerBorderBrush,
                BorderThickness = new Thickness(1)
            }
        };

        var stack = new StackPanel { Margin = new Thickness(0, 0, 18, 0), Cursor = Cursors.Hand };
        stack.Children.Add(preview);
        stack.Children.Add(new TextBlock
        {
            Text = caption,
            Foreground = HeadingBrush,
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.MouseLeftButtonUp += (_, _) => onClick();
        return stack;
    }

    /// <summary>A flat, link-coloured text button.</summary>
    public Button LinkButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Foreground = _linkBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand,
            FocusVisualStyle = null
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Wraps a pane body in a vertical-only scroll viewer.</summary>
    public ScrollViewer Scroll(UIElement child) => new()
    {
        Content = child,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    /// <summary>Returns the value, or an em-dash placeholder when blank.</summary>
    public static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value!;

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color ToColor(BackstageVisualColor color) => Color.FromRgb(color.Red, color.Green, color.Blue);

    private static Thickness ToThickness(BackstageVisualThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
}
