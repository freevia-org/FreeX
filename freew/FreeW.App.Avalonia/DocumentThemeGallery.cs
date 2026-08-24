using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// Native Avalonia realization of FreeW's Design-tab theme strip. The document mutation and
/// live-preview behavior remain in the shared ribbon commands; this class supplies the Word-style
/// page thumbnails that the generic Avalonia dropdown cannot express.
/// </summary>
internal static class DocumentThemeGallery
{
    public static Control Build(IRibbonCommandRegistry registry)
    {
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 1, 2, 0),
        };

        foreach (var theme in DocumentTheme.Catalog)
            strip.Children.Add(BuildThemeButton(theme, registry));

        return strip;
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
