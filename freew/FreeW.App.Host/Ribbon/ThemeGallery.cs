using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation;
using FreeW.App.Presentation.ContextMenus;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word-style galleries for the Design tab: a Themes gallery (one swatch per built-in
/// <see cref="DocumentTheme"/> — Office / Slate / Berlin / Ion) and a Colors gallery (each
/// theme's palette as a small colour strip). Hovering a theme swatch live-previews it on the document
/// via <see cref="DocumentView.PreviewTheme"/>; leaving reverts via <see cref="DocumentView.EndThemePreview"/>;
/// clicking commits through <see cref="DocumentView.ApplyTheme"/>. Hovering a Colors swatch previews
/// the same theme (the palette is the theme's colors), so the two galleries stay coherent. Hosted as
/// app-side custom content — no shared <c>RibbonGallery</c> render involved.
/// </summary>
internal static class ThemeGallery
{
    public static FrameworkElement BuildDocumentFormatting(DocumentView editor)
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        host.Children.Add(BuildThemes(editor));
        host.Children.Add(BuildStyleSets(editor));
        host.Children.Add(BuildColours(editor));
        host.Children.Add(BuildFonts(editor));
        host.Children.Add(BuildParagraphSpacingMenu(editor));
        host.Children.Add(BuildEffectsMenu(editor));
        return host;
    }

    /// <summary>Build the Themes gallery: a labelled horizontal strip of theme thumbnail swatches.</summary>
    public static FrameworkElement BuildThemes(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var theme in DocumentTheme.Catalog)
            strip.Children.Add(BuildThemeSwatch(editor, theme));
        return WithLabel(FreeWUiTextCatalog.Themes, strip);
    }

    /// <summary>Build Word's Style Sets gallery: typography thumbnails that rewrite built-in styles.</summary>
    public static FrameworkElement BuildStyleSets(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var styleSet in DocumentStyleSet.Catalog)
            strip.Children.Add(BuildStyleSetSwatch(editor, styleSet));
        return WithLabel(FreeWUiTextCatalog.StyleSets, strip);
    }

    /// <summary>Build Word's Colors gallery: each theme's palette rendered as a colour strip.</summary>
    public static FrameworkElement BuildColours(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var theme in DocumentTheme.Catalog)
            strip.Children.Add(BuildColourSwatch(editor, theme));
        return WithLabel(FreeWUiTextCatalog.Colors, strip);
    }

    /// <summary>Build Word's Fonts gallery: heading/body font-pair thumbnails.</summary>
    public static FrameworkElement BuildFonts(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var fontSet in DocumentFontSet.Catalog)
            strip.Children.Add(BuildFontSwatch(editor, fontSet));
        return WithLabel(FreeWUiTextCatalog.Fonts, strip);
    }

    /// <summary>Build Word's Paragraph Spacing gallery: line/spacing preset thumbnails.</summary>
    public static FrameworkElement BuildParagraphSpacing(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var spacingSet in DocumentParagraphSpacingSet.Catalog)
            strip.Children.Add(BuildParagraphSpacingSwatch(editor, spacingSet));
        return WithLabel(FreeWUiTextCatalog.ParagraphSpacing, strip);
    }

    /// <summary>Build Word's Effects gallery: DrawingML format-scheme thumbnails.</summary>
    public static FrameworkElement BuildEffects(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var effectSet in DocumentEffectSet.Catalog)
            strip.Children.Add(BuildEffectSwatch(editor, effectSet));
        return WithLabel(FreeWUiTextCatalog.Effects, strip);
    }

    private static FrameworkElement BuildParagraphSpacingMenu(DocumentView editor)
    {
        var button = new Button
        {
            Margin = new Thickness(4, 17, 4, 3),
            Padding = new Thickness(6, 3, 6, 3),
            MinWidth = 86,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = FreeWUiTextCatalog.ParagraphSpacing
        };
        AutomationProperties.SetName(button, FreeWUiTextCatalog.ParagraphSpacing);

        var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        var glyph = new StackPanel { Width = 34, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.Children.Add(Bar("#2F5496", 28, 2.5));
        glyph.Children.Add(new Border { Height = 3 });
        glyph.Children.Add(Bar("#808080", 22, 2.5));
        glyph.Children.Add(new Border { Height = 3 });
        glyph.Children.Add(Bar("#A0A0A0", 26, 2.5));
        stack.Children.Add(glyph);
        stack.Children.Add(new TextBlock
        {
            Text = FreeWUiTextCatalog.ParagraphSpacingCompact,
            FontSize = 11,
            TextAlignment = System.Windows.TextAlignment.Center,
            LineHeight = 12,
            Margin = new Thickness(0, 2, 0, 0)
        });
        button.Content = stack;

        var hover = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB));
        button.MouseEnter += (_, _) =>
        {
            button.Background = hover;
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A));
        };
        button.MouseLeave += (_, _) =>
        {
            if (button.ContextMenu is null || !button.ContextMenu.IsOpen)
            {
                button.Background = Brushes.Transparent;
                button.BorderBrush = Brushes.Transparent;
            }
        };

        var menu = new ContextMenu();
        foreach (var planned in FreeWContextMenuPlanner.BuildParagraphSpacing().Items)
        {
            if (planned.CommandId is not { } commandId
                || !FreeWContextMenuPlanner.TryParseIndex(commandId, FreeWContextMenuPlanner.ParagraphSpacingPrefix, out var index)
                || index >= DocumentParagraphSpacingSet.Catalog.Count)
                continue;
            var spacingSet = DocumentParagraphSpacingSet.Catalog[index];
            var item = new MenuItem { Header = planned.Header, Tag = spacingSet, IsEnabled = planned.IsEnabled };
            item.MouseEnter += (_, _) => editor.PreviewParagraphSpacingSet(spacingSet);
            item.MouseLeave += (_, _) => editor.EndParagraphSpacingSetPreview();
            item.Click += (_, _) =>
            {
                editor.EndParagraphSpacingSetPreview();
                editor.ApplyParagraphSpacingSet(spacingSet);
            };
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) =>
        {
            editor.EndParagraphSpacingSetPreview();
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        };
        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        };
        return button;
    }

    private static FrameworkElement BuildEffectsMenu(DocumentView editor)
    {
        var button = new Button
        {
            Margin = new Thickness(4, 17, 4, 3),
            Padding = new Thickness(6, 3, 6, 3),
            MinWidth = 70,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = FreeWUiTextCatalog.Effects
        };
        AutomationProperties.SetName(button, FreeWUiTextCatalog.Effects);

        var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        var tile = new Border
        {
            Width = 34,
            Height = 26,
            Background = BrushFor("#FFFFFF"),
            BorderBrush = BrushFor("#2F5496"),
            BorderThickness = new Thickness(1.4),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 4,
                ShadowDepth = 1,
                Opacity = 0.25,
                Color = Colors.Black
            },
            Child = new TextBlock
            {
                Text = "Fx",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFor("#2F5496"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        stack.Children.Add(tile);
        stack.Children.Add(new TextBlock
        {
            Text = FreeWUiTextCatalog.Effects,
            FontSize = 11,
            TextAlignment = System.Windows.TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        });
        button.Content = stack;

        var hover = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB));
        button.MouseEnter += (_, _) =>
        {
            button.Background = hover;
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A));
        };
        button.MouseLeave += (_, _) =>
        {
            if (button.ContextMenu is null || !button.ContextMenu.IsOpen)
            {
                button.Background = Brushes.Transparent;
                button.BorderBrush = Brushes.Transparent;
            }
        };

        var menu = new ContextMenu();
        foreach (var planned in FreeWContextMenuPlanner.BuildEffects().Items)
        {
            if (planned.CommandId is not { } commandId
                || !FreeWContextMenuPlanner.TryParseIndex(commandId, FreeWContextMenuPlanner.EffectsPrefix, out var index)
                || index >= DocumentEffectSet.Catalog.Count)
                continue;
            var effectSet = DocumentEffectSet.Catalog[index];
            var item = new MenuItem { Header = planned.Header, Tag = effectSet, IsEnabled = planned.IsEnabled };
            item.MouseEnter += (_, _) => editor.PreviewEffectSet(effectSet);
            item.MouseLeave += (_, _) => editor.EndEffectSetPreview();
            item.Click += (_, _) =>
            {
                editor.EndEffectSetPreview();
                editor.ApplyEffectSet(effectSet);
            };
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) =>
        {
            editor.EndEffectSetPreview();
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        };
        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        };
        return button;
    }

    private static FrameworkElement WithLabel(string label, FrameworkElement content)
    {
        var host = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(host, label);
        host.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 0, 1)
        });
        host.Children.Add(content);
        return host;
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

    // A color swatch: the theme's three palette colors as adjacent cells over the theme name.
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

        return WrapAsColourButton(editor, theme, thumb, FreeWUiTextCatalog.ThemeColorsAutomationName(theme.Name));
    }

    private static FrameworkElement BuildStyleSetSwatch(DocumentView editor, DocumentStyleSet styleSet)
    {
        var thumb = new StackPanel { Margin = new Thickness(4, 3, 4, 3), Width = 58 };

        var page = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1),
            Height = 40,
            Padding = new Thickness(4),
            SnapsToDevicePixels = true
        };
        var sample = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sample.Children.Add(new TextBlock
        {
            Text = "Aa",
            FontFamily = new FontFamily(styleSet.HeadingFont),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = BrushFor(styleSet.AccentColorHex),
            LineHeight = 14
        });
        sample.Children.Add(Bar("#666666", 32, 2.5));
        sample.Children.Add(Bar("#A0A0A0", 24, 2.5));
        page.Child = sample;

        thumb.Children.Add(page);
        thumb.Children.Add(new TextBlock
        {
            Text = styleSet.Name,
            FontSize = 11,
            TextAlignment = System.Windows.TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        });

        return WrapAsStyleSetButton(editor, styleSet, thumb, FreeWUiTextCatalog.StyleSetAutomationName(styleSet.Name));
    }

    private static FrameworkElement BuildFontSwatch(DocumentView editor, DocumentFontSet fontSet)
    {
        var thumb = new StackPanel { Margin = new Thickness(4, 3, 4, 3), Width = 66 };

        var page = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1),
            Height = 40,
            Padding = new Thickness(4),
            SnapsToDevicePixels = true
        };

        var sample = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sample.Children.Add(new TextBlock
        {
            Text = FreeWUiTextCatalog.FontSampleHeading,
            FontFamily = new FontFamily(fontSet.HeadingFont),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = BrushFor("#2F5496"),
            LineHeight = 11
        });
        sample.Children.Add(new TextBlock
        {
            Text = FreeWUiTextCatalog.FontSampleBody,
            FontFamily = new FontFamily(fontSet.BodyFont),
            FontSize = 9,
            Foreground = Brushes.DimGray,
            LineHeight = 9
        });
        page.Child = sample;

        thumb.Children.Add(page);
        thumb.Children.Add(new TextBlock
        {
            Text = fontSet.Name,
            FontSize = 11,
            TextAlignment = System.Windows.TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        });

        return WrapAsFontSetButton(editor, fontSet, thumb, FreeWUiTextCatalog.FontSetAutomationName(fontSet.Name));
    }

    private static FrameworkElement BuildParagraphSpacingSwatch(DocumentView editor, DocumentParagraphSpacingSet spacingSet)
    {
        var thumb = new StackPanel { Margin = new Thickness(4, 3, 4, 3), Width = 72 };

        var page = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1),
            Height = 40,
            Padding = new Thickness(5, 4, 5, 4),
            SnapsToDevicePixels = true
        };

        var sample = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var gap = Math.Max(1.0, spacingSet.SpaceAfterPt / 3.0 + spacingSet.LineSpacing - 1.0);
        sample.Children.Add(Bar("#2F5496", 34, 2.5));
        sample.Children.Add(new Border { Height = gap });
        sample.Children.Add(Bar("#808080", 26, 2.5));
        sample.Children.Add(new Border { Height = gap });
        sample.Children.Add(Bar("#A0A0A0", 30, 2.5));
        page.Child = sample;

        thumb.Children.Add(page);
        thumb.Children.Add(new TextBlock
        {
            Text = spacingSet.Name,
            FontSize = 10.5,
            TextAlignment = System.Windows.TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 11,
            Margin = new Thickness(0, 2, 0, 0)
        });

        return WrapAsParagraphSpacingSetButton(
            editor,
            spacingSet,
            thumb,
            FreeWUiTextCatalog.ParagraphSpacingAutomationName(spacingSet.Name));
    }

    private static FrameworkElement BuildEffectSwatch(DocumentView editor, DocumentEffectSet effectSet)
    {
        var thumb = new StackPanel { Margin = new Thickness(4, 3, 4, 3), Width = 58 };

        var shape = new Border
        {
            Background = Brushes.White,
            BorderBrush = BrushFor("#2F5496"),
            BorderThickness = new Thickness(effectSet.LineWidthEmu / 6350.0),
            Height = 40,
            CornerRadius = new CornerRadius(effectSet.SoftEdges ? 4 : 0),
            SnapsToDevicePixels = true,
            Effect = effectSet.OuterShadow
                ? new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = effectSet.SoftEdges ? 7 : 4,
                    ShadowDepth = effectSet.SoftEdges ? 2 : 1,
                    Opacity = effectSet.SoftEdges ? 0.35 : 0.24,
                    Color = Colors.Black
                }
                : null,
            Child = new TextBlock
            {
                Text = "Fx",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFor("#2F5496"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        thumb.Children.Add(shape);
        thumb.Children.Add(new TextBlock
        {
            Text = effectSet.Name,
            FontSize = 10.5,
            TextAlignment = System.Windows.TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 11,
            Margin = new Thickness(0, 2, 0, 0)
        });

        return WrapAsEffectSetButton(editor, effectSet, thumb, FreeWUiTextCatalog.EffectSetAutomationName(effectSet.Name));
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

    private static FrameworkElement WrapAsColourButton(DocumentView editor, DocumentTheme theme, FrameworkElement content, string tip)
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
            editor.PreviewThemeColors(theme);
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
            editor.ApplyThemeColors(theme);
        };
        return button;
    }

    private static FrameworkElement WrapAsStyleSetButton(DocumentView editor, DocumentStyleSet styleSet, FrameworkElement content, string tip)
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
            editor.PreviewStyleSet(styleSet);
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            editor.EndStyleSetPreview();
        };
        button.Click += (_, _) =>
        {
            editor.EndStyleSetPreview();
            editor.ApplyStyleSet(styleSet);
        };
        return button;
    }

    private static FrameworkElement WrapAsFontSetButton(DocumentView editor, DocumentFontSet fontSet, FrameworkElement content, string tip)
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
            editor.PreviewFontSet(fontSet);
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            editor.EndFontSetPreview();
        };
        button.Click += (_, _) =>
        {
            editor.EndFontSetPreview();
            editor.ApplyFontSet(fontSet);
        };
        return button;
    }

    private static FrameworkElement WrapAsParagraphSpacingSetButton(DocumentView editor, DocumentParagraphSpacingSet spacingSet, FrameworkElement content, string tip)
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
            editor.PreviewParagraphSpacingSet(spacingSet);
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            editor.EndParagraphSpacingSetPreview();
        };
        button.Click += (_, _) =>
        {
            editor.EndParagraphSpacingSetPreview();
            editor.ApplyParagraphSpacingSet(spacingSet);
        };
        return button;
    }

    private static FrameworkElement WrapAsEffectSetButton(DocumentView editor, DocumentEffectSet effectSet, FrameworkElement content, string tip)
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
            editor.PreviewEffectSet(effectSet);
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            editor.EndEffectSetPreview();
        };
        button.Click += (_, _) =>
        {
            editor.EndEffectSetPreview();
            editor.ApplyEffectSet(effectSet);
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
