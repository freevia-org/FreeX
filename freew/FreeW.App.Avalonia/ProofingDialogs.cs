using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Avalonia;

internal sealed class ProofingLanguageDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle ChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;
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

        var plan = ProofingLanguageDialogPlanner.Build(currentTag);
        var items = plan.Choices.Select(choice => new ComboBoxItem
        {
            Content = choice.DisplayText,
            Tag = choice.Tag,
        }).ToList();

        _languages.ItemsSource = items;
        _languages.SelectedItem = items[plan.SelectedIndex];
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

internal sealed class ThesaurusDialog : FreeWDialogWindow
{
    public string? SelectedReplacement { get; private set; }

    public ThesaurusDialog(string word, ThesaurusEntry? entry)
        : this(ThesaurusPresentationPlanner.Build(word, entry))
    {
    }

    public ThesaurusDialog(ThesaurusDisplayPlan plan)
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
            Text = plan.HeadingText,
            FontWeight = FontWeight.SemiBold,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
        });

        if (!plan.HasSynonyms)
        {
            body.Children.Add(new TextBlock
            {
                Text = plan.StatusText,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            foreach (var sense in plan.Senses)
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

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([close], new Thickness(16, 10, 16, 14));
        DockPanel.SetDock(buttons, Dock.Bottom);

        Content = new DockPanel
        {
            LastChildFill = true,
            Children = { buttons, scroll },
        };
    }

    public static async Task<string?> ShowAsync(Window owner, string word, ThesaurusEntry? entry) =>
        await ShowAsync(owner, ThesaurusPresentationPlanner.Build(word, entry));

    public static async Task<string?> ShowAsync(Window owner, ThesaurusDisplayPlan plan)
    {
        var dialog = new ThesaurusDialog(plan);
        await dialog.ShowDialog(owner);
        return dialog.SelectedReplacement;
    }

    private Control BuildSense(ThesaurusSenseRow sense)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = sense.DisplayLabel,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x32, 0x4D)),
        });

        foreach (var action in sense.Actions)
            panel.Children.Add(BuildActionRow(action));

        return panel;
    }

    private Control BuildActionRow(ThesaurusActionRow action)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var synonym = new TextBlock
        {
            Text = action.DisplayText,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(synonym, 0);

        var replace = new Button
        {
            Content = "Replace",
            MinWidth = 78,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        replace.Click += (_, _) =>
        {
            SelectedReplacement = action.DisplayText;
            Close();
        };
        Grid.SetColumn(replace, 1);

        grid.Children.Add(synonym);
        grid.Children.Add(replace);
        return grid;
    }
}
