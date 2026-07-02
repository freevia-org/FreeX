using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Avalonia;

internal sealed class ProofingLanguageDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle ChromeStyle = new(FontFamily.Default);
    private readonly ComboBox _languages = new() { MinWidth = 260 };

    public string? SelectedTag { get; private set; }

    public ProofingLanguageDialog(string? currentTag)
    {
        Title = "Set Proofing Language";
        Width = 390;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var items = new List<ComboBoxItem>
        {
            new() { Content = "(None - clear language)", Tag = string.Empty },
        };
        items.AddRange(ProofingLanguageCatalog.CommonLanguages.Select(choice => new ComboBoxItem
        {
            Content = $"{choice.Label} ({choice.Tag})",
            Tag = choice.Tag,
        }));

        _languages.ItemsSource = items;
        _languages.SelectedItem = items.FirstOrDefault(item =>
            string.Equals(item.Tag as string, currentTag ?? string.Empty, StringComparison.OrdinalIgnoreCase)) ?? items[0];
        AvaloniaCompactDialogChrome.ApplyComboBox(_languages, ChromeStyle);

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, "Language:", _languages);

        var buttons = InsertDialogLayout.OkCancelRow(
            ok: () =>
            {
                SelectedTag = (_languages.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
                Close();
            },
            cancel: Close);

        Content = new StackPanel
        {
            Children = { grid, buttons },
        };
    }

    public static async Task<string?> ChooseAsync(Window owner, string? currentTag)
    {
        var dialog = new ProofingLanguageDialog(currentTag);
        await dialog.ShowDialog(owner);
        return dialog.SelectedTag;
    }
}

internal sealed class ThesaurusDialog : Window
{
    public ThesaurusDialog(string word, ThesaurusEntry? entry)
    {
        Title = "Thesaurus";
        Width = 440;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        ShowInTaskbar = false;

        var body = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            Text = word,
            FontWeight = FontWeight.SemiBold,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
        });

        if (entry is null)
        {
            body.Children.Add(new TextBlock
            {
                Text = "No synonyms found for this word.",
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            foreach (var sense in entry.Senses)
                body.Children.Add(BuildSense(sense));
        }

        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var close = new Button
        {
            Content = "Close",
            MinWidth = 78,
            IsDefault = true,
            IsCancel = true,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 10, 16, 14),
            Children = { close },
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        Content = new DockPanel
        {
            LastChildFill = true,
            Children = { buttons, scroll },
        };
    }

    public static Task ShowAsync(Window owner, string word, ThesaurusEntry? entry) =>
        new ThesaurusDialog(word, entry).ShowDialog(owner);

    private static Control BuildSense(ThesaurusSense sense)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = FormatSenseLabel(sense.Label),
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x32, 0x4D)),
        });

        var synonyms = string.Join(", ", sense.Synonyms.Select(s => s.Replace('_', ' ')));
        panel.Children.Add(new TextBlock
        {
            Text = synonyms,
            TextWrapping = TextWrapping.Wrap,
        });

        return panel;
    }

    private static string FormatSenseLabel(string label) =>
        label.Replace('_', ' ');
}
