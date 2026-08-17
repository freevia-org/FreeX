using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Modal "Insert Icon" picker: a searchable, category-filtered grid of icon thumbnails.
/// Selecting an icon and clicking OK rasterises the SVG via <see cref="SvgRasterizerHelper"/>
/// and returns an <see cref="InlineImage"/>; Cancel returns null.
/// </summary>
internal sealed class IconPickerDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private static readonly IconPickerSurfaceSpec Surface = IconPickerDialogPlanner.Surface;
    // ── State ─────────────────────────────────────────────────────────────────────────────────────
    private InlineImage? _result;
    private readonly IconPickerDialogSession _session;
    // Roving-tabindex keyboard focus: the flat index (into the current tile list, same order as
    // _session's VisibleEntries) that arrow/Home/End keys move and Enter/Space selects.
    private int _focusedIndex;

    // ── Controls ──────────────────────────────────────────────────────────────────────────────────
    private readonly ComboBox _categoryBox;
    private readonly TextBox  _searchBox;
    private readonly WrapPanel _grid;
    private readonly TextBlock _statusBar;

    // ── Thumbnail geometry ────────────────────────────────────────────────────────────────────────
    // ── Constructor ───────────────────────────────────────────────────────────────────────────────
    private IconPickerDialog(Window? owner)
    {
        Owner = owner;
        Title = Surface.Title;
        Width = Surface.DialogWidth;
        Height = Surface.DialogHeight;
        MinHeight = Surface.MinDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        _session = new IconPickerDialogSession(
            IconPickerCatalog.LoadFromBaseDirectory(AppContext.BaseDirectory));

        var root = new DockPanel { Margin = new Thickness(Surface.RootMargin) };

        // ── Filter row ────────────────────────────────────────────────────────────────────────────
        var filterRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, Surface.FilterBottomMargin)
        };
        var categoryField = Surface.Field(IconPickerFieldKind.Category);
        var searchField = Surface.Field(IconPickerFieldKind.Search);
        filterRow.Children.Add(new TextBlock
        {
            Text = categoryField.Label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Surface.FieldLabelTrailingMargin, 0)
        });

        _categoryBox = new ComboBox
        {
            MinWidth = categoryField.Width,
            Margin = new Thickness(0, 0, Surface.CategoryTrailingMargin, 0)
        };
        AutomationProperties.SetAutomationId(_categoryBox, categoryField.AutomationId);
        _categoryBox.Items.Add(IconPickerDialogPlanner.AllCategoriesLabel);
        foreach (var cat in _session.Categories)
            _categoryBox.Items.Add(cat);
        _categoryBox.SelectedIndex = 0;
        _categoryBox.SelectionChanged += (_, _) => Refresh();
        filterRow.Children.Add(_categoryBox);

        filterRow.Children.Add(new TextBlock
        {
            Text = searchField.Label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Surface.FieldLabelTrailingMargin, 0)
        });

        _searchBox = new TextBox
        {
            Width = searchField.Width,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(_searchBox, searchField.AutomationId);
        _searchBox.TextChanged += (_, _) => Refresh();
        filterRow.Children.Add(_searchBox);

        DockPanel.SetDock(filterRow, Dock.Top);
        root.Children.Add(filterRow);

        // ── Status bar + OK/Cancel ─────────────────────────────────────────────────────────────────
        _statusBar = new TextBlock
        {
            Text = string.Empty,
            Foreground = SystemColors.GrayTextBrush,
            FontStyle = FontStyles.Italic,
            FontSize = Surface.StatusFontSize,
            Margin = new Thickness(0, Surface.StatusVerticalMargin, 0, Surface.StatusVerticalMargin),
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(_statusBar, Surface.StatusAutomationId);

        var bottomRow = new DockPanel { Margin = new Thickness(0, Surface.BottomRowTopMargin, 0, 0) };
        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: Surface.ActionButtonWidth,
            rowMargin: new Thickness(0));
        DockPanel.SetDock(buttons, Dock.Right);
        bottomRow.Children.Add(buttons);
        bottomRow.Children.Add(_statusBar);

        DockPanel.SetDock(bottomRow, Dock.Bottom);
        root.Children.Add(bottomRow);

        // ── Icon grid ─────────────────────────────────────────────────────────────────────────────
        _grid = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetAutomationId(_grid, Surface.TilesAutomationId);
        // Keyboard focus lands on one tile at a time (roving tab stop, set in ResetTileFocusState /
        // MoveKeyboardFocus below); KeyDown bubbles up from that tile through this panel, so a
        // single handler here drives arrow/Home/End navigation and Enter/Space selection for the
        // whole grid.
        _grid.KeyDown += OnGridKeyDown;

        var scroll = new ScrollViewer
        {
            Content = _grid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(Surface.ScrollBorderThickness),
            BorderBrush = SystemColors.ControlDarkBrush
        };

        root.Children.Add(scroll);
        Content = root;

        // Initial population
        Refresh();
        _searchBox.Focus();
    }

    // ── Grid population ───────────────────────────────────────────────────────────────────────────
    private void Refresh()
    {
        _grid.Children.Clear();

        var category = _categoryBox.SelectedItem as string;
        var search = _searchBox.Text;
        var state = _session.ApplyFilter(category, search);

        _statusBar.Text = state.StatusText;

        foreach (var entry in state.VisibleEntries)
            _grid.Children.Add(MakeTile(entry));

        ResetTileFocusState();
    }

    private Border MakeTile(IconPickerEntry entry)
    {
        var iconSize = (int)Surface.IconSize;
        // Load a small preview thumbnail: render the SVG at IconSize×IconSize.
        Image? preview = null;
        try
        {
            var bmp = LoadThumbnail(entry.Path, iconSize);
            preview = new Image
            {
                Source = bmp,
                Width = Surface.IconSize,
                Height = Surface.IconSize,
                Stretch = Surface.PreserveThumbnailAspectRatio ? Stretch.Uniform : Stretch.Fill,
                ToolTip = IconPickerDialogPlanner.ToolTipFor(entry)
            };
        }
        catch
        {
            // Fallback: grey box if a thumbnail can't be loaded (e.g. broken SVG)
        }

        var content = preview ?? (UIElement)new Border
        {
            Width = Surface.IconSize,
            Height = Surface.IconSize,
            Background = Brushes.LightGray
        };

        var tile = new Border
        {
            Width = Surface.TileSize,
            Height = Surface.TileSize,
            Margin = new Thickness(Surface.TileMargin),
            Padding = new Thickness(Surface.TilePadding),
            BorderThickness = new Thickness(Surface.TileBorderThickness),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Child = content,
            Cursor = Cursors.Hand,
            Tag = entry,
            // Border is not a Control, but any UIElement can take keyboard focus once Focusable is
            // set. Combined with the roving IsTabStop managed in ResetTileFocusState / MoveKeyboard-
            // Focus, this lets Tab move into (and out of) the grid as a single stop, then arrows move
            // within it -- Border itself has no default focus chrome, so ApplyFocusVisuals paints an
            // explicit indicator instead of relying on WPF's default dotted adorner.
            Focusable = true,
        };
        AutomationProperties.SetAutomationId(tile, IconPickerDialogPlanner.TileAutomationId(entry));
        AutomationProperties.SetName(tile, entry.Name);

        tile.MouseLeftButtonUp += OnTileClick;
        // Border has no MouseDoubleClick — detect double-click via ClickCount on MouseLeftButtonDown.
        tile.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) OnTileDoubleClick(s, e); };

        return tile;
    }

    private static BitmapSource LoadThumbnail(string svgPath, int size)
    {
        // Parse the SVG the same way SvgRasterizerHelper does but render at thumbnail size.
        var settings = new SharpVectors.Renderers.Wpf.WpfDrawingSettings
        {
            IncludeRuntime = false,
            OptimizePath = true,
            TextAsGeometry = true
        };
        using var reader = new SharpVectors.Converters.FileSvgReader(settings);
        var drawing = reader.Read(svgPath)
            ?? throw new InvalidOperationException($"Could not parse SVG: {svgPath}");

        var drawingImage = new DrawingImage(drawing);
        drawingImage.Freeze();

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        var dv = new System.Windows.Media.DrawingVisual();
        using (var ctx = dv.RenderOpen())
            ctx.DrawImage(drawingImage, new Rect(0, 0, size, size));
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // ── Selection ─────────────────────────────────────────────────────────────────────────────────
    private void OnTileClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border tile || tile.Tag is not IconPickerEntry entry)
            return;

        var tiles = _grid.Children.OfType<Border>().ToList();
        var index = tiles.IndexOf(tile);
        if (index >= 0)
            SelectTile(tiles, index, entry);
    }

    private void OnTileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border tile)
        {
            if (tile.Tag is IconPickerEntry entry)
            {
                _session.Select(entry);
                Accept();
            }
        }
    }

    // ── Keyboard navigation ───────────────────────────────────────────────────────────────────────
    // Arrows move the roving tab stop across the grid, Home/End jump to the first/last tile, and
    // Enter/Space select the currently focused tile — a keyboard-only user can reach and pick any
    // icon without ever touching the mouse. IconGridNavigation (FreeW.App.Presentation) owns the
    // actual index math so both this shell and the Avalonia twin move identically.
    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        var tiles = _grid.Children.OfType<Border>().ToList();
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
        var tiles = _grid.Children.OfType<Border>().ToList();
        _focusedIndex = 0;
        for (var i = 0; i < tiles.Count; i++)
            KeyboardNavigation.SetIsTabStop(tiles[i], i == 0);
        ApplyFocusVisuals(tiles);
    }

    /// <summary>Moves the roving tab stop and keyboard focus without changing the selection.</summary>
    private void MoveKeyboardFocus(IReadOnlyList<Border> tiles, int index)
    {
        if (index < 0 || index >= tiles.Count)
            return;

        for (var i = 0; i < tiles.Count; i++)
            KeyboardNavigation.SetIsTabStop(tiles[i], i == index);

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
    /// (visible regardless of theme/system focus-adorner settings, and asserted directly by tests),
    /// independent of the blue selection highlight so a user can see both at once.
    /// </summary>
    private void ApplyFocusVisuals(IReadOnlyList<Border> tiles)
    {
        var selected = _session.State.SelectedEntry;
        for (var i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            var isFocused = i == _focusedIndex;
            var isSelected = tile.Tag is IconPickerEntry entry && Equals(selected, entry);

            tile.BorderThickness = new Thickness(isFocused ? Surface.TileBorderThickness + 1 : Surface.TileBorderThickness);
            tile.BorderBrush = isFocused
                ? Brushes.Black
                : isSelected ? SystemColors.HighlightBrush : Brushes.Transparent;

            if (isSelected)
            {
                var background = SystemColors.HighlightBrush.CloneCurrentValue();
                if (background is SolidColorBrush solid)
                    solid.Opacity = 0.25;
                tile.Background = background;
            }
            else
            {
                tile.Background = Brushes.Transparent;
            }
        }
    }

    // ── Accept ────────────────────────────────────────────────────────────────────────────────────
    private void Accept()
    {
        var plan = _session.PlanAccept();
        if (!plan.ShouldAccept)
        {
            DialogMessageHelper.ShowWarning(this, plan.WarningMessage!, Surface.Title);
            return;
        }

        try
        {
            _result = SvgRasterizerHelper.RasterizeToInlineImage(plan.Selection!.Path);
            Close();
        }
        catch (Exception ex)
        {
            DialogMessageHelper.ShowError(
                this,
                IconPickerDialogPlanner.RasterizationErrorMessage(ex.Message),
                Surface.Title);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Show the icon picker. Returns a rasterised <see cref="InlineImage"/> on OK, or null if cancelled.
    /// </summary>
    public static InlineImage? Prompt(Window? owner)
    {
        var dlg = new IconPickerDialog(owner);
        dlg.ShowDialog();
        return dlg._result;
    }
}
