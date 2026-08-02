using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

/// <summary>
/// Avalonia's icon picker. It owns the same category/search/selection lifecycle as WPF. The selected
/// SVG is returned as a shared selection record; rasterization remains a host-owned follow-up because the
/// WPF SharpVectors rasterizer is intentionally not a cross-platform dependency.
/// </summary>
internal sealed class IconPickerDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle ChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;
    private readonly IReadOnlyList<IconPickerEntry> _entries;
    private readonly ComboBox _category;
    private readonly TextBox _search;
    private readonly WrapPanel _tiles;
    private readonly TextBlock _status;
    private readonly Dictionary<string, DrawingImage?> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private IconPickerEntry? _selected;

    private const int ThumbSize = 54;
    private const int IconSize = 38;
    // WPF draws the shared SVG viewport into the thumbnail bitmap. The Avalonia drawing host keeps
    // the source geometry at its natural viewport scale, so this explicit authority scale keeps the
    // painted stroke envelope aligned without changing the 54px tile hit target.
    private const double SvgViewportScale = IconSize / 32d;
    private const int TilesPerRow = 8;
    private const double DialogWidth = TilesPerRow * (ThumbSize + 4) + 32;

    private IconPickerDialog()
    {
        Title = "Insert Icon";
        Width = DialogWidth;
        Height = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _entries = LoadEntries();

        _category = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 14, 0) };
        _category.ItemsSource = new[] { IconPickerDialogPlanner.AllCategoriesLabel }
            .Concat(IconPickerDialogPlanner.Categories(_entries)).ToArray();
        _category.SelectedIndex = 0;
        _search = new TextBox { Width = 160 };
        _tiles = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _status = new TextBlock
        {
            Foreground = Brushes.Gray,
            FontStyle = FontStyle.Italic,
            FontFamily = ChromeStyle.FontFamily,
            FontSize = 12,
            Margin = new Thickness(0, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(_category, ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_search, ChromeStyle);
        _category.SelectionChanged += (_, _) => Refresh();
        _search.TextChanged += (_, _) => Refresh();

        var filter = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        filter.Children.Add(new TextBlock
        {
            Text = "Category:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        filter.Children.Add(_category);
        filter.Children.Add(new TextBlock
        {
            Text = "Search:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        filter.Children.Add(_search);

        var scroll = new ScrollViewer
        {
            Content = _tiles,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
        };
        var ok = Button("OK", Accept, isDefault: true);
        var cancel = Button("Cancel", () => Close(null), isCancel: true);
        var actions = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], style: ChromeStyle);

        var bottom = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(actions, Dock.Right);
        bottom.Children.Add(actions);
        bottom.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(filter, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(filter);
        root.Children.Add(bottom);
        root.Children.Add(scroll);
        Content = root;
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
            var tile = new Border
            {
                Child = CreateThumbnail(entry),
                Width = ThumbSize,
                Height = ThumbSize,
                Margin = new Thickness(2),
                Padding = new Thickness(4),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = entry,
            };
            ToolTip.SetTip(tile, $"{entry.Name}\n({entry.Category})");
            tile.PointerReleased += (_, args) =>
            {
                if (args.InitialPressMouseButton == MouseButton.Left)
                    Select(entry, tile);
            };
            tile.PointerPressed += (_, args) =>
            {
                if (args.GetCurrentPoint(tile).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed
                    && args.ClickCount == 2)
                {
                    _selected = entry;
                    Accept();
                    args.Handled = true;
                }
            };
            _tiles.Children.Add(tile);
        }
        _status.Text = entries.Count == 0 ? "No icons match." : $"{entries.Count} icons";
    }

    private Control CreateThumbnail(IconPickerEntry entry)
    {
        if (!_thumbnails.TryGetValue(entry.Path, out var drawing))
        {
            try
            {
                drawing = SvgIconRasterizer.LoadFile(entry.Path);
            }
            catch
            {
                drawing = null;
            }
            _thumbnails[entry.Path] = drawing;
        }

        return drawing is null
            ? new Border { Width = IconSize, Height = IconSize, Background = Brushes.LightGray }
            : new Image
            {
                Source = drawing,
                Width = IconSize,
                Height = IconSize,
                Stretch = Stretch.Uniform,
                RenderTransform = new ScaleTransform(SvgViewportScale, SvgViewportScale),
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            };
    }

    private void Select(IconPickerEntry entry, Border tile)
    {
        _selected = entry;
        foreach (var existing in _tiles.Children.OfType<Border>())
        {
            existing.BorderBrush = Brushes.Transparent;
            existing.Background = Brushes.Transparent;
        }
        tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7));
        tile.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x78, 0xD7));
    }

    private async void Accept()
    {
        if (_selected is not null)
        {
            Close(IconPickerDialogPlanner.Select(_selected));
            return;
        }
        await AvaloniaUserMessageDialog.ShowWarningAsync(this, "Select an icon first.", "Insert Icon");
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
