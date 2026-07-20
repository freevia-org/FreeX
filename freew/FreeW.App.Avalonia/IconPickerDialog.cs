using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

/// <summary>
/// Avalonia's icon picker. It owns the same category/search/selection lifecycle as WPF. The selected
/// SVG is returned as a shared selection record; rasterization remains a host-owned follow-up because the
/// WPF SharpVectors rasterizer is intentionally not a cross-platform dependency.
/// </summary>
internal sealed class IconPickerDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle ChromeStyle = new(FontFamily.Default);
    private readonly IReadOnlyList<IconPickerEntry> _entries;
    private readonly ComboBox _category;
    private readonly TextBox _search;
    private readonly WrapPanel _tiles;
    private readonly TextBlock _status;
    private IconPickerEntry? _selected;

    private IconPickerDialog()
    {
        Title = "Insert Icon";
        Width = 620;
        Height = 500;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _entries = LoadEntries();

        _category = new ComboBox { MinWidth = 130 };
        _category.ItemsSource = new[] { IconPickerDialogPlanner.AllCategoriesLabel }
            .Concat(IconPickerDialogPlanner.Categories(_entries)).ToArray();
        _category.SelectedIndex = 0;
        _search = new TextBox { MinWidth = 180 };
        _tiles = new WrapPanel { Orientation = Orientation.Horizontal };
        _status = new TextBlock { Margin = new Thickness(0, 6, 0, 0) };
        AvaloniaCompactDialogChrome.ApplyComboBox(_category, ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_search, ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, ChromeStyle);
        _category.SelectionChanged += (_, _) => Refresh();
        _search.TextChanged += (_, _) => Refresh();

        var filter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        filter.Children.Add(new TextBlock { Text = "Category:", VerticalAlignment = VerticalAlignment.Center });
        filter.Children.Add(_category);
        filter.Children.Add(new TextBlock { Text = "Search:", VerticalAlignment = VerticalAlignment.Center });
        filter.Children.Add(_search);

        var scroll = new ScrollViewer
        {
            Content = _tiles,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.LightGray,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var ok = Button("OK", Accept, isDefault: true);
        var cancel = Button("Cancel", () => Close(null), isCancel: true);
        Content = new StackPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                filter,
                scroll,
                _status,
                AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 8, 0, 0)),
            },
        };
        Refresh();
        Opened += (_, _) => _search.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            Close(null);
            e.Handled = true;
        };
    }

    public static Task<IconPickerSelection?> ShowAsync(Window owner) =>
        new IconPickerDialog().ShowDialog<IconPickerSelection?>(owner);

    private void Refresh()
    {
        _selected = null;
        _tiles.Children.Clear();
        var entries = IconPickerDialogPlanner.Filter(
            _entries,
            _category.SelectedItem as string,
            _search.Text);
        foreach (var entry in entries)
        {
            var tile = new Button
            {
                Content = new StackPanel
                {
                    Width = 82,
                    Children =
                    {
                        new TextBlock { Text = entry.Name, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                        new TextBlock { Text = entry.Category, FontSize = 10, Opacity = 0.7, TextAlignment = TextAlignment.Center },
                    },
                },
                Width = 92,
                Height = 60,
                Margin = new Thickness(3),
                Tag = entry,
            };
            ToolTip.SetTip(tile, entry.Path);
            tile.Click += (_, _) => Select(entry, tile);
            _tiles.Children.Add(tile);
        }
        _status.Text = entries.Count == 0 ? "No icons match." : $"{entries.Count} icons";
    }

    private void Select(IconPickerEntry entry, Button tile)
    {
        _selected = entry;
        foreach (var button in _tiles.Children.OfType<Button>())
            button.Background = Brushes.Transparent;
        tile.Background = Brushes.LightBlue;
    }

    private void Accept()
    {
        if (_selected is not null)
        {
            Close(IconPickerDialogPlanner.Select(_selected));
            return;
        }
        _status.Text = "Select an icon first.";
    }

    private static Button Button(string text, Action action, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, ChromeStyle, minWidth: 72, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }

    private static IReadOnlyList<IconPickerEntry> LoadEntries()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Resources", "ContentIconsSvg");
        if (!Directory.Exists(root))
            return [];

        return Directory.EnumerateDirectories(root)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .SelectMany(categoryPath => Directory.EnumerateFiles(categoryPath, "*.svg")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    var category = TitleCase(Path.GetFileName(categoryPath));
                    var name = TitleCase(Path.GetFileNameWithoutExtension(path).Replace('-', ' '));
                    return new IconPickerEntry(name, category, $"{name} {category}".ToLowerInvariant(), path);
                }))
            .ToArray();
    }

    private static string TitleCase(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));
}
