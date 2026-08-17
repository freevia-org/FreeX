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
    // Roving-tabindex keyboard focus: the flat index (into the current tile list, same order as
    // _session's VisibleEntries) that arrow/Home/End keys move and Enter/Space selects. Mirrors the
    // WPF twin's IconPickerDialog._focusedIndex so both shells navigate identically.
    private int _focusedIndex;

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
            MinWidth = categoryField.Width,
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
        // Keyboard focus lands on one tile at a time (roving tab stop, set in ResetTileFocusState /
        // MoveKeyboardFocus below); KeyDown bubbles up from that tile through this panel, so a
        // single handler here drives arrow/Home/End navigation and Enter/Space selection for the
        // whole grid.
        _tiles.KeyDown += OnGridKeyDown;
        _status = new TextBlock
        {
            Foreground = Brushes.Gray,
            FontStyle = FontStyle.Italic,
            FontFamily = ChromeStyle.FontFamily,
            FontSize = Surface.StatusFontSize,
            Margin = new Thickness(0, Surface.StatusVerticalMargin),
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
            Margin = new Thickness(0, 0, Surface.FieldLabelTrailingMargin, 0),
        });
        filter.Children.Add(_category);
        filter.Children.Add(new TextBlock
        {
            Text = searchField.Label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Surface.FieldLabelTrailingMargin, 0),
        });
        filter.Children.Add(_search);

        var scroll = new ScrollViewer
        {
            Content = _tiles,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(Surface.ScrollBorderThickness),
            BorderBrush = new SolidColorBrush(Color.Parse(Surface.AvaloniaScrollBorderHex)),
        };
        var actions = AvaloniaCompactDialogChrome.CreateOkCancelRow(
            Accept,
            () => Close(null),
            buttonWidth: Surface.ActionButtonWidth,
            margin: new Thickness(0),
            style: ChromeStyle);

        var bottom = new DockPanel { Margin = new Thickness(0, Surface.BottomRowTopMargin, 0, 0) };
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
                Margin = new Thickness(Surface.TileMargin),
                Padding = new Thickness(Surface.TilePadding),
                BorderThickness = new Thickness(Surface.TileBorderThickness),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = entry,
                // Border is a real Avalonia Control, so Focusable + the roving IsTabStop managed in
                // ResetTileFocusState / MoveKeyboardFocus below let Tab move into (and out of) the
                // grid as a single stop, then arrows move within it.
                Focusable = true,
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
        ResetTileFocusState();
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
                Stretch = Surface.PreserveThumbnailAspectRatio ? Stretch.Uniform : Stretch.Fill,
            };
    }

    private void Select(IconPickerEntry entry, Border tile)
    {
        var tiles = _tiles.Children.OfType<Border>().ToList();
        var index = tiles.IndexOf(tile);
        if (index >= 0)
            SelectTile(tiles, index, entry);
    }

    // ── Keyboard navigation ───────────────────────────────────────────────────────────────────────
    // Arrows move the roving tab stop across the grid, Home/End jump to the first/last tile, and
    // Enter/Space select the currently focused tile — a keyboard-only user can reach and pick any
    // icon without ever touching the mouse. IconGridNavigation (FreeW.App.Presentation) owns the
    // actual index math so this shell and the WPF twin move identically.
    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        var tiles = _tiles.Children.OfType<Border>().ToList();
        if (tiles.Count == 0)
            return;

        if (MapNavigationKey(e.Key) is { } navigationKey)
        {
            var nextIndex = IconGridNavigation.Move(_focusedIndex, navigationKey, tiles.Count, Surface.TilesPerRow);
            MoveKeyboardFocus(tiles, nextIndex);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Space)
        {
            var index = Math.Clamp(_focusedIndex, 0, tiles.Count - 1);
            if (tiles[index].Tag is IconPickerEntry entry)
                SelectTile(tiles, index, entry);
            e.Handled = true;
        }
    }

    private static IconGridNavigationKey? MapNavigationKey(Key key) => key switch
    {
        Key.Left => IconGridNavigationKey.Left,
        Key.Right => IconGridNavigationKey.Right,
        Key.Up => IconGridNavigationKey.Up,
        Key.Down => IconGridNavigationKey.Down,
        Key.Home => IconGridNavigationKey.Home,
        Key.End => IconGridNavigationKey.End,
        _ => null,
    };

    /// <summary>Resets the roving tab stop to the first tile after a filter/search change.</summary>
    private void ResetTileFocusState()
    {
        var tiles = _tiles.Children.OfType<Border>().ToList();
        _focusedIndex = 0;
        for (var i = 0; i < tiles.Count; i++)
            tiles[i].IsTabStop = i == 0;
        ApplyFocusVisuals(tiles);
    }

    /// <summary>Moves the roving tab stop and keyboard focus without changing the selection.</summary>
    private void MoveKeyboardFocus(IReadOnlyList<Border> tiles, int index)
    {
        if (index < 0 || index >= tiles.Count)
            return;

        for (var i = 0; i < tiles.Count; i++)
            tiles[i].IsTabStop = i == index;

        _focusedIndex = index;
        ApplyFocusVisuals(tiles);
        tiles[index].Focus();
    }

    private void SelectTile(IReadOnlyList<Border> tiles, int index, IconPickerEntry entry)
    {
        _session.Select(entry);
        MoveKeyboardFocus(tiles, index);
    }

    /// <summary>
    /// Single source of truth for tile appearance: a black outline marks the keyboard-focused tile
    /// (visible regardless of theme settings, and asserted directly by tests), independent of the
    /// accent selection highlight so a user can see both at once.
    /// </summary>
    private void ApplyFocusVisuals(IReadOnlyList<Border> tiles)
    {
        var selected = _session.State.SelectedEntry;
        var highlight = Color.Parse(Surface.AvaloniaSelectionHighlightHex);
        for (var i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            var isFocused = i == _focusedIndex;
            var isSelected = tile.Tag is IconPickerEntry entry && Equals(selected, entry);

            tile.BorderThickness = new Thickness(isFocused ? Surface.TileBorderThickness + 1 : Surface.TileBorderThickness);
            tile.BorderBrush = isFocused
                ? Brushes.Black
                : isSelected ? new SolidColorBrush(highlight) : Brushes.Transparent;
            tile.Background = isSelected
                ? new SolidColorBrush(highlight, Surface.SelectionHighlightOpacity)
                : Brushes.Transparent;
        }
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
