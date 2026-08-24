using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// Native Home-tab Quick Styles strip for Avalonia. It uses the shared previewable style commands,
/// so hover, cancellation, and commit have the same document transaction as the WPF gallery.
/// </summary>
internal static class DocumentStylesGallery
{
    private static readonly (string Name, string Id)[] Entries =
    [
        ("Normal", "Normal"), ("No Spacing", "NoSpacing"), ("Heading 1", "Heading1"),
        ("Heading 2", "Heading2"), ("Heading 3", "Heading3"), ("Title", "Title"),
        ("Subtitle", "Subtitle"), ("Quote", "Quote"),
    ];

    public static Control Build(DocumentView editor, IRibbonCommandRegistry registry)
    {
        BuiltInStyles.EnsureSeeded(editor.Document, "Normal");
        BuiltInStyles.EnsureSeeded(editor.Document, "NoSpacing");

        var root = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 1, 2, 0),
        };
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var entry in Entries)
            swatches.Children.Add(BuildStyleButton(editor, registry, entry.Name, entry.Id));

        root.Children.Add(new Border
        {
            Height = 52,
            Width = 180,
            Background = Brushes.White,
            BorderBrush = Brush("#D0D0D0"),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = swatches,
            },
        });
        root.Children.Add(BuildMoreButton(editor, registry));
        return root;
    }

    private static Button BuildMoreButton(DocumentView editor, IRibbonCommandRegistry registry)
    {
        var button = new Button
        {
            Content = "▾",
            Width = 20,
            Height = 52,
            Margin = new Thickness(2, 0, 0, 0),
        };
        ToolTip.SetTip(button, "More Styles");
        AutomationProperties.SetName(button, "More Styles");
        var flyout = new MenuFlyout();
        foreach (var entry in Entries)
            flyout.Items.Add(BuildMenuItem(editor, registry, entry.Name, entry.Id));
        flyout.Items.Add(new Separator());
        AddAction(flyout, registry, "Clear Style", "freew.style-clear");
        AddAction(flyout, registry, "New Style…", "freew.new-style");
        AddAction(flyout, registry, "Manage Styles…", "freew.manage-styles");
        button.Click += (_, _) => flyout.ShowAt(button);
        return button;
    }

    private static MenuItem BuildMenuItem(DocumentView editor, IRibbonCommandRegistry registry, string name, string id)
    {
        var commandId = new RibbonCommandId(FormattingGalleryRibbonWorkflow.StyleCommandId(id));
        var item = new MenuItem { Header = name };
        item.PointerEntered += (_, _) => InvokePreview(commandId, registry, preview => preview.BeginPreview(RibbonCommandContext.Empty));
        item.PointerExited += (_, _) => InvokePreview(commandId, registry, preview => preview.CancelPreview());
        item.Click += (_, _) => Execute(commandId, registry);
        return item;
    }

    private static void AddAction(MenuFlyout flyout, IRibbonCommandRegistry registry, string label, string command)
    {
        var id = new RibbonCommandId(command);
        if (!registry.TryGet(id, out var registered) || registered is null)
            return;
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => registered.Execute(RibbonCommandContext.Empty);
        flyout.Items.Add(item);
    }

    private static Button BuildStyleButton(
        DocumentView editor,
        IRibbonCommandRegistry registry,
        string name,
        string id)
    {
        var run = ResolveRun(editor.Document, id);
        var label = new TextBlock
        {
            Text = name,
            FontFamily = new FontFamily(run.FontFamily ?? "Calibri"),
            FontSize = Math.Min((run.FontSizePt ?? 11) * 96d / 72d, 16),
            FontWeight = run.Bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = run.Italic ? global::Avalonia.Media.FontStyle.Italic : global::Avalonia.Media.FontStyle.Normal,
            Foreground = Brush(run.ColorHex ?? "#000000"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (run.Underline)
            label.TextDecorations = TextDecorations.Underline;

        var commandId = new RibbonCommandId(FormattingGalleryRibbonWorkflow.StyleCommandId(id));
        var button = new Button
        {
            Content = label,
            Height = 50,
            MinWidth = 64,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, name);
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
        button.Click += (_, _) => Execute(commandId, registry);
        return button;
    }

    private static void Execute(RibbonCommandId id, IRibbonCommandRegistry registry)
    {
        if (registry.TryGet(id, out var command) && command is not null)
            command.Execute(RibbonCommandContext.Empty);
    }

    private static void InvokePreview(RibbonCommandId id, IRibbonCommandRegistry registry, Action<IRibbonPreviewCommand> action)
    {
        if (registry.TryGet(id, out var command) && command is IRibbonPreviewCommand preview)
            action(preview);
    }

    private static RunFormatting ResolveRun(TextDocument document, string id)
    {
        var result = document.DefaultRun;
        var chain = new Stack<DocumentStyle>();
        var seen = new HashSet<string>();
        string? currentId = id;
        while (currentId is not null && seen.Add(currentId) && document.Styles.TryGetValue(currentId, out var style))
        {
            chain.Push(style);
            currentId = style.BasedOnStyleId;
        }
        while (chain.TryPop(out var style))
        {
            result = result with
            {
                Bold = style.Run.Bold || result.Bold,
                Italic = style.Run.Italic || result.Italic,
                Underline = style.Run.Underline || result.Underline,
                FontFamily = style.Run.FontFamily ?? result.FontFamily,
                FontSizePt = style.Run.FontSizePt ?? result.FontSizePt,
                ColorHex = style.Run.ColorHex ?? result.ColorHex,
            };
        }
        return result;
    }

    private static IBrush Brush(string hex)
    {
        try { return new SolidColorBrush(Color.Parse(hex)); }
        catch (FormatException) { return Brushes.Black; }
    }
}
