using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Free.Shared.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Modal "Insert Icon" picker: a searchable, category-filtered grid of icon thumbnails.
/// Selecting an icon and clicking OK rasterises the SVG via <see cref="SvgRasterizerHelper"/>
/// and returns an <see cref="InlineImage"/>; Cancel returns null.
/// </summary>
internal sealed class IconPickerDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    // ── State ─────────────────────────────────────────────────────────────────────────────────────
    private InlineImage? _result;
    private ContentIconCatalog.IconEntry? _selected;

    // ── Controls ──────────────────────────────────────────────────────────────────────────────────
    private readonly ComboBox _categoryBox;
    private readonly TextBox  _searchBox;
    private readonly WrapPanel _grid;
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _statusBar;

    // ── Thumbnail geometry ────────────────────────────────────────────────────────────────────────
    private const int ThumbSize   = 54;   // pixels (tile)
    private const int IconSize    = 38;   // px for the rendered icon inside the tile
    private const int TilesPerRow = 8;
    private const double DialogW  = TilesPerRow * (ThumbSize + 4) + 32;

    // ── Constructor ───────────────────────────────────────────────────────────────────────────────
    private IconPickerDialog(Window? owner)
    {
        Owner = owner;
        Title = "Insert Icon";
        Width = DialogW;
        Height = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(10) };

        // ── Filter row ────────────────────────────────────────────────────────────────────────────
        var filterRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        filterRow.Children.Add(new TextBlock
        {
            Text = "Category:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        _categoryBox = new ComboBox { MinWidth = 120, Margin = new Thickness(0, 0, 14, 0) };
        _categoryBox.Items.Add(ContentIconCatalog.AllCategoriesLabel);
        foreach (var cat in ContentIconCatalog.Categories)
            _categoryBox.Items.Add(cat);
        _categoryBox.SelectedIndex = 0;
        _categoryBox.SelectionChanged += (_, _) => Refresh();
        filterRow.Children.Add(_categoryBox);

        filterRow.Children.Add(new TextBlock
        {
            Text = "Search:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        _searchBox = new TextBox { Width = 160, VerticalContentAlignment = VerticalAlignment.Center };
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
            Margin = new Thickness(0, 4, 0, 4),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Reuse the shared OK/Cancel button row (localized content, accelerators, automation
        // names; Cancel is IsCancel so Esc/Cancel closes). Single source of truth shared with
        // FreeX/FreeW dialogs -- see DialogSharedHelperDedupTests.
        var btnPanel = DialogButtonRowFactory.Create(Accept, buttonWidth: 72);

        var bottomRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(btnPanel, Dock.Right);
        bottomRow.Children.Add(btnPanel);
        bottomRow.Children.Add(_statusBar);

        DockPanel.SetDock(bottomRow, Dock.Bottom);
        root.Children.Add(bottomRow);

        // ── Icon grid ─────────────────────────────────────────────────────────────────────────────
        _grid = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        _scroll = new ScrollViewer
        {
            Content = _grid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(1),
            BorderBrush = SystemColors.ControlDarkBrush
        };

        root.Children.Add(_scroll);
        Content = root;

        // Initial population
        Refresh();
        _searchBox.Focus();
    }

    // ── Grid population ───────────────────────────────────────────────────────────────────────────
    private void Refresh()
    {
        _grid.Children.Clear();
        _selected = null;

        var category = _categoryBox.SelectedItem as string;
        var search   = _searchBox.Text;
        var entries  = ContentIconCatalog.Filter(category, search).ToList();

        _statusBar.Text = entries.Count == 0 ? "No icons match." : $"{entries.Count} icons";

        foreach (var entry in entries)
        {
            var tile = MakeTile(entry);
            _grid.Children.Add(tile);
        }
    }

    private Border MakeTile(ContentIconCatalog.IconEntry entry)
    {
        // Load a small preview thumbnail: render the SVG at IconSize×IconSize.
        Image? preview = null;
        try
        {
            var bmp = LoadThumbnail(entry.Path, IconSize);
            preview = new Image
            {
                Source = bmp,
                Width = IconSize,
                Height = IconSize,
                Stretch = Stretch.Uniform,
                ToolTip = $"{entry.Name}\n({entry.Category})"
            };
        }
        catch
        {
            // Fallback: grey box if a thumbnail can't be loaded (e.g. broken SVG)
        }

        var content = preview ?? (UIElement)new Border
        {
            Width = IconSize,
            Height = IconSize,
            Background = Brushes.LightGray
        };

        var tile = new Border
        {
            Width = ThumbSize,
            Height = ThumbSize,
            Margin = new Thickness(2),
            Padding = new Thickness(4),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Child = content,
            Cursor = Cursors.Hand,
            Tag = entry
        };

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
        // Deselect any previously selected tile
        foreach (Border existing in _grid.Children.OfType<Border>())
            SetSelected(existing, false);

        if (sender is Border tile)
        {
            SetSelected(tile, true);
            _selected = tile.Tag as ContentIconCatalog.IconEntry;
        }
    }

    private void OnTileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border tile)
        {
            _selected = tile.Tag as ContentIconCatalog.IconEntry;
            Accept();
        }
    }

    private static void SetSelected(Border tile, bool selected)
    {
        tile.BorderBrush  = selected ? SystemColors.HighlightBrush : Brushes.Transparent;
        tile.Background   = selected ? SystemColors.HighlightBrush.CloneCurrentValue() : Brushes.Transparent;
        if (selected && tile.Background is SolidColorBrush bg)
            bg.Opacity = 0.25;
    }

    // ── Accept ────────────────────────────────────────────────────────────────────────────────────
    private void Accept()
    {
        if (_selected is null)
        {
            DialogMessageHelper.ShowWarning(this, "Select an icon first.", "Insert Icon");
            return;
        }

        try
        {
            _result = SvgRasterizerHelper.RasterizeToInlineImage(_selected.Path);
            Close();
        }
        catch (Exception ex)
        {
            DialogMessageHelper.ShowError(this,
                $"Could not rasterize the icon:\n{ex.Message}", "Insert Icon");
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
