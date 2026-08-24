using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// Native Avalonia realization of FreeW's Design-tab theme strip. The document mutation and
/// live-preview behavior remain in the shared ribbon commands; this class supplies the Word-style
/// page thumbnails that the generic Avalonia dropdown cannot express.
/// </summary>
internal static class DocumentThemeGallery
{
    private const int VisibleStyleCount = 8;

    public static Control Build(IRibbonCommandRegistry registry)
    {
        // Word keeps Themes as a compact chooser; the broad preview lane belongs
        // to Document Formatting's style-set gallery.
        return BuildMenuButton("Themes", "Aa", DocumentTheme.Catalog.Select(theme =>
            (theme.Name, new RibbonCommandId($"freew.theme.{theme.Name.ToLowerInvariant()}"))), registry);
    }

    public static Control BuildDocumentFormatting(IRibbonCommandRegistry registry)
    {
        var root = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var styleSet in DocumentStyleSet.Catalog.Take(VisibleStyleCount))
            root.Children.Add(BuildStyleSetButton(styleSet, registry));
        root.Children.Add(BuildMenuButton("More", "▾", DocumentStyleSet.Catalog.Select(styleSet =>
            (styleSet.Name, new RibbonCommandId(DesignRibbonWorkflow.StyleSetCommandId(styleSet.Name)))), registry, "More Style Sets"));
        root.Children.Add(BuildMenuButton("Colors", "", DocumentTheme.Catalog.Select(theme =>
            (theme.Name, new RibbonCommandId($"freew.theme-colors.{theme.Name.ToLowerInvariant()}"))), registry));
        root.Children.Add(BuildMenuButton("Fonts", "", DocumentFontSet.Catalog.Select(fontSet =>
            (fontSet.Name, new RibbonCommandId($"freew.theme-fonts.{fontSet.Name.ToLowerInvariant()}"))), registry));
        root.Children.Add(BuildMenuButton("Paragraph\nSpacing", "", DocumentParagraphSpacingSet.Catalog.Select(spacing =>
            (spacing.Name, new RibbonCommandId(DesignRibbonWorkflow.ParagraphSpacingCommandId(spacing.Name)))), registry));
        root.Children.Add(BuildMenuButton("Effects", "Fx", DocumentEffectSet.Catalog.Select((effect, index) =>
            (effect.Name, new RibbonCommandId(FreeWContextMenuPlanner.EffectsPrefix + index))), registry));
        return root;
    }

    private static Button BuildStyleSetButton(DocumentStyleSet styleSet, IRibbonCommandRegistry registry)
    {
        var preview = new StackPanel { Width = 58, Margin = new Thickness(3, 1) };
        preview.Children.Add(new TextBlock { Text = "Aa", FontWeight = FontWeight.Bold, Foreground = Brush(styleSet.AccentColorHex), FontSize = 13 });
        preview.Children.Add(Bar("#606060", 40, 2));
        preview.Children.Add(Bar("#808080", 30, 2));
        preview.Children.Add(new TextBlock { Text = styleSet.Name, FontSize = 9, TextAlignment = global::Avalonia.Media.TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        return BuildCommandButton(preview, styleSet.Name, new RibbonCommandId(DesignRibbonWorkflow.StyleSetCommandId(styleSet.Name)), registry);
    }

    private static Button BuildMenuButton(
        string label,
        string glyph,
        IEnumerable<(string Label, RibbonCommandId CommandId)> entries,
        IRibbonCommandRegistry registry,
        string? automationName = null)
    {
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        if (!string.IsNullOrEmpty(glyph))
            content.Children.Add(new TextBlock { Text = glyph, FontSize = glyph == "Aa" ? 17 : 12, TextAlignment = global::Avalonia.Media.TextAlignment.Center });
        content.Children.Add(new TextBlock { Text = label + "\n▾", FontSize = 10, TextAlignment = global::Avalonia.Media.TextAlignment.Center });
        var button = new Button { Content = content, Height = 52, MinWidth = 54, Padding = new Thickness(2) };
        ToolTip.SetTip(button, automationName ?? label);
        AutomationProperties.SetName(button, automationName ?? label);
        var flyout = new MenuFlyout();
        foreach (var (entryLabel, commandId) in entries)
            flyout.Items.Add(BuildMenuItem(entryLabel, commandId, registry));
        button.Click += (_, _) => flyout.ShowAt(button);
        return button;
    }

    private static MenuItem BuildMenuItem(string label, RibbonCommandId commandId, IRibbonCommandRegistry registry)
    {
        var item = new MenuItem { Header = label };
        item.PointerEntered += (_, _) => InvokePreview(commandId, registry, preview => preview.BeginPreview(RibbonCommandContext.Empty));
        item.PointerExited += (_, _) => InvokePreview(commandId, registry, preview => preview.CancelPreview());
        item.Click += (_, _) =>
        {
            InvokePreview(commandId, registry, preview => preview.CancelPreview());
            if (registry.TryGet(commandId, out var command) && command is not null)
                command.Execute(RibbonCommandContext.Empty);
        };
        return item;
    }

    private static Button BuildCommandButton(Control content, string automationName, RibbonCommandId commandId, IRibbonCommandRegistry registry)
    {
        var button = new Button { Content = content, Height = 52, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), Padding = new Thickness(1) };
        ToolTip.SetTip(button, automationName);
        AutomationProperties.SetName(button, automationName);
        button.PointerEntered += (_, _) => { button.Background = Brush("#EAF1FB"); button.BorderBrush = Brush("#2B579A"); InvokePreview(commandId, registry, preview => preview.BeginPreview(RibbonCommandContext.Empty)); };
        button.PointerExited += (_, _) => { button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; InvokePreview(commandId, registry, preview => preview.CancelPreview()); };
        button.Click += (_, _) => { InvokePreview(commandId, registry, preview => preview.CancelPreview()); if (registry.TryGet(commandId, out var command) && command is not null) command.Execute(RibbonCommandContext.Empty); };
        return button;
    }

    private static Control BuildThemeButton(DocumentTheme theme, IRibbonCommandRegistry registry)
    {
        var thumbnail = new StackPanel
        {
            Width = 52,
            Margin = new Thickness(3, 1),
        };
        var page = new Border
        {
            Height = 40,
            Background = Brushes.White,
            BorderBrush = Brush("#C0C0C0"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
        };
        var bars = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        bars.Children.Add(Bar(theme.PrimaryColorHex, 18, 4));
        bars.Children.Add(Bar(theme.HeadingColorHex, 26, 3));
        bars.Children.Add(Bar(theme.HeadingAccentColorHex, 22, 3));
        page.Child = bars;
        thumbnail.Children.Add(page);
        thumbnail.Children.Add(new TextBlock
        {
            Text = theme.Name,
            FontSize = 9,
            TextAlignment = global::Avalonia.Media.TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0),
        });

        var commandId = new RibbonCommandId($"freew.theme.{theme.Name.ToLowerInvariant()}");
        var button = new Button
        {
            Content = thumbnail,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
        };
        ToolTip.SetTip(button, theme.Name);
        AutomationProperties.SetName(button, theme.Name);
        button.PointerEntered += (_, _) =>
        {
            button.Background = Brush("#EAF1FB");
            button.BorderBrush = Brush("#2B579A");
            InvokePreview(commandId, registry, preview => preview.BeginPreview(RibbonCommandContext.Empty));
        };
        button.PointerExited += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            InvokePreview(commandId, registry, preview => preview.CancelPreview());
        };
        button.GotFocus += (_, _) =>
            InvokePreview(commandId, registry, preview => preview.BeginPreview(RibbonCommandContext.Empty));
        button.LostFocus += (_, _) =>
            InvokePreview(commandId, registry, preview => preview.CancelPreview());
        button.Click += (_, _) =>
        {
            InvokePreview(commandId, registry, preview => preview.CancelPreview());
            if (registry.TryGet(commandId, out var command) && command is not null)
                command.Execute(RibbonCommandContext.Empty);
        };
        return button;
    }

    private static void InvokePreview(
        RibbonCommandId commandId,
        IRibbonCommandRegistry registry,
        Action<IRibbonPreviewCommand> invoke)
    {
        if (registry.TryGet(commandId, out var command) && command is IRibbonPreviewCommand preview)
            invoke(preview);
    }

    private static Border Bar(string hex, double width, double height) => new()
    {
        Width = width,
        Height = height,
        Background = Brush(hex),
        Margin = new Thickness(0, 1),
    };

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
