using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word-style galleries for the Design tab: a Themes gallery (one swatch per built-in
/// <see cref="DocumentTheme"/> — Office / Slate / Berlin / Ion) and a theme-colours gallery (each
/// theme's palette as a small colour strip). Hovering a theme swatch live-previews it on the document
/// via <see cref="DocumentView.PreviewTheme"/>; leaving reverts via <see cref="DocumentView.EndThemePreview"/>;
/// clicking commits through <see cref="DocumentView.ApplyTheme"/>. Hovering a colours swatch previews
/// the same theme (the palette is the theme's colours), so the two galleries stay coherent. Hosted as
/// app-side custom content — no shared <c>RibbonGallery</c> render involved.
/// </summary>
internal static class ThemeGallery
{
    /// <summary>Build the Themes gallery: a horizontal strip of theme thumbnail swatches.</summary>
    public static FrameworkElement BuildThemes(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var theme in DocumentTheme.Catalog)
            strip.Children.Add(BuildThemeSwatch(editor, theme));
        return strip;
    }

    /// <summary>Build the theme-colours gallery: each theme's palette rendered as a colour strip.</summary>
    public static FrameworkElement BuildColours(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var theme in DocumentTheme.Catalog)
            strip.Children.Add(BuildColourSwatch(editor, theme));
        return strip;
    }

    // A theme thumbnail: a small page-like preview (title + two heading bars in the theme palette) over
    // the theme name. Hover previews; leave reverts; click commits.
    private static FrameworkElement BuildThemeSwatch(DocumentView editor, DocumentTheme theme)
    {
        var thumb = new StackPanel { Margin = new Thickness(4, 3, 4, 3), Width = 52 };

        var page = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1),
            Height = 40,
            Padding = new Thickness(4),
            SnapsToDevicePixels = true
        };
        var bars = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        bars.Children.Add(Bar(theme.PrimaryColorHex, 18, 3.5));
        bars.Children.Add(Bar(theme.HeadingColorHex, 26, 2.5));
        bars.Children.Add(Bar(theme.HeadingAccentColorHex, 22, 2.5));
        page.Child = bars;

        var caption = new TextBlock
        {
            Text = theme.Name,
            FontSize = 11,
            TextAlignment = System.Windows.TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };

        thumb.Children.Add(page);
        thumb.Children.Add(caption);

        return WrapAsButton(editor, theme, thumb, theme.Name);
    }

    // A colour swatch: the theme's three palette colours as adjacent cells over the theme name.
    private static FrameworkElement BuildColourSwatch(DocumentView editor, DocumentTheme theme)
    {
        var thumb = new StackPanel { Margin = new Thickness(4, 3, 4, 3), Width = 52 };

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var hex in new[] { theme.PrimaryColorHex, theme.HeadingColorHex, theme.HeadingAccentColorHex })
            row.Children.Add(new Border { Background = BrushFor(hex), Width = 14, Height = 24, BorderBrush = Brushes.White, BorderThickness = new Thickness(0.5) });

        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1),
            Child = row,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        thumb.Children.Add(border);
        thumb.Children.Add(new TextBlock { Text = theme.Name, FontSize = 11, TextAlignment = System.Windows.TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });

        return WrapAsButton(editor, theme, thumb, theme.Name + " colours");
    }

    // Wrap a thumbnail in a borderless button that previews on hover, reverts on leave, commits on click.
    private static FrameworkElement WrapAsButton(DocumentView editor, DocumentTheme theme, FrameworkElement content, string tip)
    {
        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = tip
        };

        var hover = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB));
        button.MouseEnter += (_, _) =>
        {
            button.Background = hover;
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A));
            editor.PreviewTheme(theme);
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            editor.EndThemePreview();
        };
        button.Click += (_, _) =>
        {
            editor.EndThemePreview();
            editor.ApplyTheme(theme);
        };
        return button;
    }

    private static FrameworkElement Bar(string hex, double width, double height) => new Border
    {
        Background = BrushFor(hex),
        Width = width,
        Height = height,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 1, 0, 1)
    };

    private static Brush BrushFor(string? hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Brushes.Gray;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.Gray; }
    }
}
