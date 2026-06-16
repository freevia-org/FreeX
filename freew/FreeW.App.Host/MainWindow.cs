using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
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

        var document = TextDocument.CreateEmpty();

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

        var ribbon = BuildRibbon(FreeWRibbon.Build());
        DockPanel.SetDock(ribbon, Dock.Top);
        root.Children.Add(ribbon);

        var status = new StatusBar();
        status.Items.Add(new StatusBarItem { Content = $"Data folder: {ResolveDataFolderLabel()}" });
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        var editor = new RichTextBox
        {
            Margin = new Thickness(40, 24, 40, 24),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(48),
            FontFamily = new FontFamily("Calibri"),
            FontSize = 16,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        editor.Document = new FlowDocument(new System.Windows.Documents.Paragraph(
            new System.Windows.Documents.Run(document.PlainText.Length == 0 ? "Start typing your document…" : document.PlainText)));
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

    // --- Minimal ribbon renderer over the shared RibbonDefinition model ---

    private static UIElement BuildRibbon(RibbonDefinition definition)
    {
        var tabs = new TabControl
        {
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            MinHeight = 116
        };

        foreach (var tab in definition.Tabs)
            tabs.Items.Add(new TabItem { Header = tab.Header, Content = BuildTab(tab) });

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

    private static UIElement BuildTab(RibbonTab tab)
    {
        var lane = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 6, 6, 4)
        };

        foreach (var group in tab.Groups)
            lane.Children.Add(BuildGroup(group));

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = lane
        };
    }

    private static UIElement BuildGroup(RibbonGroup group)
    {
        var controls = new WrapPanel { MaxWidth = 220, Margin = new Thickness(4, 2, 4, 2) };
        foreach (var control in group.Controls)
        {
            var element = BuildControl(control);
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

    private static UIElement? BuildControl(RibbonControl control) => control switch
    {
        RibbonSeparator or RibbonRowBreak => null,
        RibbonToggleButton toggle => new ToggleButton
        {
            Content = toggle.Label,
            Margin = new Thickness(2),
            Padding = new Thickness(8, 4, 8, 4),
            MinWidth = 60
        },
        _ => new Button
        {
            Content = control.Label,
            Margin = new Thickness(2),
            Padding = new Thickness(8, 4, 8, 4),
            MinWidth = 60
        }
    };
}
