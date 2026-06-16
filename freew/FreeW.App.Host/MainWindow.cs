using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// FreeW main window. Deliberately code-only and minimal: it exists to prove the shared tier is
/// consumable by a second app. The ribbon is built from the shared <see cref="RibbonDefinition"/>
/// model and rendered by a small local renderer; the status bar shows that the shared storage
/// helpers resolve FreeW's own data folder (because Program.Main set AppProduct = "FreeW").
/// </summary>
public sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "FreeW";
        Width = 1040;
        Height = 720;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        var root = new DockPanel();

        var titleBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)),
            Padding = new Thickness(12, 6, 12, 6),
            Child = new TextBlock
            {
                Text = "FreeW — a free word processor (scaffold on the Free.Shared.* tier)",
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            }
        };
        DockPanel.SetDock(titleBar, Dock.Top);
        root.Children.Add(titleBar);

        var editor = new DocumentView { Margin = new Thickness(40, 24, 40, 24) };
        editor.LoadModel(CreateSampleDocument());
        var commands = FreeWRibbonCommands.Build(editor);

        var ribbon = BuildRibbon(FreeWRibbon.Build(), commands);
        DockPanel.SetDock(ribbon, Dock.Top);
        root.Children.Add(ribbon);

        var status = new StatusBar();
        status.Items.Add(new StatusBarItem { Content = $"Data folder: {ResolveDataFolderLabel()}" });
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        root.Children.Add(editor);

        Content = root;
    }

    // Shows that AppProduct = "FreeW" routes the shared storage helpers to FreeW's own folder.
    private static string ResolveDataFolderLabel()
    {
        try
        {
            return AppStoragePathPlanner.GetOptionsFilePath(PlatformApplicationDataPathProvider.LocalInstance);
        }
        catch
        {
            return $"%LOCALAPPDATA%\\{AppProduct.Current.ProductDirectoryName}";
        }
    }

    // A sample document that exercises the model's styles + run/paragraph formatting.
    private static TextDocument CreateSampleDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Paragraphs.Clear();

        doc.Paragraphs.Add(new Paragraph("Welcome to FreeW") { StyleId = "Title" });
        doc.Paragraphs.Add(new Paragraph("A free word processor") { StyleId = "Heading1" });

        var intro = new Paragraph();
        intro.Runs.Add(new Run("This document is rendered from the FreeW model. Formatting like "));
        intro.Runs.Add(new Run("bold", new RunFormatting { Bold = true }));
        intro.Runs.Add(new Run(", "));
        intro.Runs.Add(new Run("italic", new RunFormatting { Italic = true }));
        intro.Runs.Add(new Run(", "));
        intro.Runs.Add(new Run("underline", new RunFormatting { Underline = true }));
        intro.Runs.Add(new Run(" and "));
        intro.Runs.Add(new Run("colour", new RunFormatting { ColorHex = "#C0504D", Bold = true }));
        intro.Runs.Add(new Run(" resolves through styles and document defaults. Edit freely — the surface is a live RichTextBox; CommitToModel() maps your edits back."));
        doc.Paragraphs.Add(intro);

        doc.Paragraphs.Add(new Paragraph("Centered paragraph.")
        {
            Formatting = ParagraphFormatting.Default with { Alignment = FreeW.Core.Model.TextAlignment.Center }
        });

        return doc;
    }

    // --- Minimal ribbon renderer over the shared RibbonDefinition model ---

    private static UIElement BuildRibbon(RibbonDefinition definition, IRibbonCommandRegistry registry)
    {
        var tabs = new TabControl
        {
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            MinHeight = 116
        };

        foreach (var tab in definition.Tabs)
            tabs.Items.Add(new TabItem { Header = tab.Header, Content = BuildTab(tab, registry) });

        if (tabs.Items.Count > 0)
            tabs.SelectedIndex = 0;

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = tabs
        };
    }

    private static UIElement BuildTab(RibbonTab tab, IRibbonCommandRegistry registry)
    {
        var lane = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 6, 6, 4)
        };

        foreach (var group in tab.Groups)
            lane.Children.Add(BuildGroup(group, registry));

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = lane
        };
    }

    private static UIElement BuildGroup(RibbonGroup group, IRibbonCommandRegistry registry)
    {
        var controls = new WrapPanel { MaxWidth = 220, Margin = new Thickness(4, 2, 4, 2) };
        foreach (var control in group.Controls)
        {
            var element = BuildControl(control, registry);
            if (element is not null)
                controls.Children.Add(element);
        }

        var header = new TextBlock
        {
            Text = group.Header,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var stack = new StackPanel();
        stack.Children.Add(controls);
        stack.Children.Add(header);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xE2)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(6, 4, 6, 2),
            Child = stack
        };
    }

    private static UIElement? BuildControl(RibbonControl control, IRibbonCommandRegistry registry)
    {
        if (control is RibbonSeparator or RibbonRowBreak)
            return null;

        var thickness = new Thickness(2);
        var padding = new Thickness(8, 4, 8, 4);
        registry.TryGet(control.CommandId, out var command);

        void Execute() => command?.Execute(RibbonCommandContext.Empty);

        if (control is RibbonToggleButton)
        {
            var toggle = new ToggleButton { Content = control.Label, Margin = thickness, Padding = padding, MinWidth = 60 };
            if (command is IRibbonStatefulCommand stateful)
                toggle.IsChecked = stateful.GetState().IsChecked;
            toggle.Click += (_, _) => Execute();
            toggle.IsEnabled = command is not null;
            return toggle;
        }

        var button = new Button { Content = control.Label, Margin = thickness, Padding = padding, MinWidth = 60 };
        button.Click += (_, _) => Execute();
        button.IsEnabled = command is not null;
        return button;
    }
}
