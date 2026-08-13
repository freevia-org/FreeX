using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

/// <summary>
/// Avalonia renderer for the shared icon-picker session. The selected SVG is returned as a shared
/// selection record; rasterization remains a host-owned follow-up because the WPF SharpVectors rasterizer
/// is intentionally not a cross-platform dependency.
/// </summary>
internal sealed class IconPickerDialog : FreeWDialogWindow
{
    private static readonly IconPickerSurfaceSpec Surface = IconPickerDialogPlanner.Surface;
    private static readonly AvaloniaCompactDialogChromeStyle ChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;
    private readonly IconPickerDialogSession _session;
    private readonly ComboBox _category;
    private readonly TextBox _search;
    private readonly WrapPanel _tiles;
    private readonly TextBlock _status;
    private readonly Dictionary<string, DrawingImage?> _thumbnails = new(StringComparer.OrdinalIgnoreCase);

    private IconPickerDialog()
    {
        Title = Surface.Title;
        Width = Surface.DialogWidth;
        Height = Surface.DialogHeight;
        MinHeight = Surface.MinDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _session = new IconPickerDialogSession(
            IconPickerCatalog.LoadFromBaseDirectory(AppContext.BaseDirectory));

        var categoryField = Surface.Field(IconPickerFieldKind.Category);
        var searchField = Surface.Field(IconPickerFieldKind.Search);
        _category = new ComboBox
        {
            Width = categoryField.Width,
            Margin = new Thickness(0, 0, Surface.CategoryTrailingMargin, 0)
        };
        AutomationProperties.SetAutomationId(_category, categoryField.AutomationId);
        _category.ItemsSource = new[] { IconPickerDialogPlanner.AllCategoriesLabel }
            .Concat(_session.Categories).ToArray();
        _category.SelectedIndex = 0;
        _search = new TextBox { Width = searchField.Width };
        AutomationProperties.SetAutomationId(_search, searchField.AutomationId);
        _tiles = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(_tiles, Surface.TilesAutomationId);
        _status = new TextBlock
        {
            Foreground = Brushes.Gray,
            FontStyle = FontStyle.Italic,
            FontFamily = ChromeStyle.FontFamily,
            FontSize = 12,
            Margin = new Thickness(0, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(_status, Surface.StatusAutomationId);
        AvaloniaCompactDialogChrome.ApplyComboBox(_category, ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_search, ChromeStyle);
        _category.SelectionChanged += (_, _) => Refresh();
        _search.TextChanged += (_, _) => Refresh();

        var filter = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, Surface.FilterBottomMargin),
        };
        filter.Children.Add(new TextBlock
        {
            Text = categoryField.Label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        filter.Children.Add(_category);
        filter.Children.Add(new TextBlock
        {
            Text = searchField.Label,
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
        var actions = AvaloniaCompactDialogChrome.CreateOkCancelRow(
            Accept,
            () => Close(null),
            buttonWidth: Surface.ActionButtonWidth,
            margin: new Thickness(0),
            style: ChromeStyle);

        var bottom = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(actions, Dock.Right);
        bottom.Children.Add(actions);
        bottom.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(Surface.RootMargin) };
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
        _tiles.Children.Clear();
        var state = _session.ApplyFilter(
            _category.SelectedItem as string,
            _search.Text);
        foreach (var entry in state.VisibleEntries)
        {
            var tile = new Border
            {
                Child = CreateThumbnail(entry),
                Width = Surface.TileSize,
                Height = Surface.TileSize,
                Margin = new Thickness(2),
                Padding = new Thickness(4),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = entry,
            };
            AutomationProperties.SetAutomationId(tile, IconPickerDialogPlanner.TileAutomationId(entry));
            AutomationProperties.SetName(tile, entry.Name);
            ToolTip.SetTip(tile, IconPickerDialogPlanner.ToolTipFor(entry));
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
                    _session.Select(entry);
                    Accept();
                    args.Handled = true;
                }
            };
            _tiles.Children.Add(tile);
        }
        _status.Text = state.StatusText;
    }

    private Control CreateThumbnail(IconPickerEntry entry)
    {
        if (!_thumbnails.TryGetValue(entry.Path, out var drawing))
        {
            try
            {
                drawing = SvgIconRasterizer.LoadFileToPaintedBounds(entry.Path);
            }
            catch
            {
                drawing = null;
            }
            _thumbnails[entry.Path] = drawing;
        }

        return drawing is null
            ? new Border { Width = Surface.IconSize, Height = Surface.IconSize, Background = Brushes.LightGray }
            : new Image
            {
                Source = drawing,
                Width = Surface.IconSize,
                Height = Surface.IconSize,
                Stretch = Stretch.Fill,
            };
    }

    private void Select(IconPickerEntry entry, Border tile)
    {
        _session.Select(entry);
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
        var plan = _session.PlanAccept();
        if (plan.ShouldAccept)
        {
            Close(plan.Selection);
            return;
        }
        await AvaloniaUserMessageDialog.ShowWarningAsync(this, plan.WarningMessage!, Surface.Title);
    }

}
