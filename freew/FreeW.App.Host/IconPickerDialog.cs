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
                Stretch = Stretch.Uniform,
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
            Tag = entry
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
        // Deselect any previously selected tile
        foreach (Border existing in _grid.Children.OfType<Border>())
            SetSelected(existing, false);

        if (sender is Border tile)
        {
            SetSelected(tile, true);
            if (tile.Tag is IconPickerEntry entry)
                _session.Select(entry);
        }
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
