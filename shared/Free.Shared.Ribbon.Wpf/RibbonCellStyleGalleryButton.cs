using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Excel-style Cell Styles gallery: a compact ribbon button opens categorized live style previews
/// instead of a long text-only menu. The host still owns the actual style commands.
/// </summary>
public sealed class RibbonCellStyleGalleryButton : Button
{
    private readonly Popup _popup;
    private readonly StackPanel _sections = new();
    private Action<RibbonCommandId, object?>? _execute;
    private IReadOnlyList<string> _itemHeaders = Array.Empty<string>();

    public RibbonCellStyleGalleryButton()
    {
        _popup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            Child = new Border
            {
                Width = 510,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(92, 92, 92)),
                BorderThickness = new Thickness(1),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    MaxHeight = 620,
                    Content = _sections,
                },
            },
        };
        Unloaded += (_, _) => CloseGallery();
    }

    public bool IsGalleryOpen => _popup.IsOpen;

    public FrameworkElement GalleryPopupChild => (FrameworkElement)_popup.Child;

    public IReadOnlyList<string> ItemHeaders => _itemHeaders;

    public void SetMenu(RibbonMenu menu, Action<RibbonCommandId, object?> execute)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(execute);

        _execute = execute;
        _sections.Children.Clear();
        var choices = menu.Items.Where(item => item.Kind == RibbonMenuItemKind.Command && item.CommandId is not null)
            .ToArray();
        _itemHeaders = choices.Select(item => item.Header).ToArray();
        foreach (var section in CellStyleSections)
        {
            var items = choices.Where(item => section.Headers.Contains(item.Header, StringComparer.Ordinal)).ToArray();
            if (items.Length > 0)
                _sections.Children.Add(CreateSection(section.Title, items));
        }

        var assignedHeaders = CellStyleSections.SelectMany(section => section.Headers).ToHashSet(StringComparer.Ordinal);
        var remaining = choices.Where(item => !assignedHeaders.Contains(item.Header)).ToArray();
        if (remaining.Length > 0)
            _sections.Children.Add(CreateSection("Number Format", remaining));
    }

    public void OpenGallery()
    {
        if (!IsEnabled || _sections.Children.Count == 0)
            return;

        _popup.PlacementTarget = this;
        _popup.IsOpen = true;
    }

    public void CloseGallery() => _popup.IsOpen = false;

    private FrameworkElement CreateSection(string title, IReadOnlyList<RibbonMenuItem> items)
    {
        var section = new StackPanel { Margin = new Thickness(8, 6, 8, 2) };
        section.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Margin = new Thickness(4, 0, 0, 4),
        });

        var tiles = new UniformGrid { Columns = 3 };
        foreach (var item in items)
            tiles.Children.Add(CreateTile(item));
        section.Children.Add(tiles);
        return section;
    }

    private Button CreateTile(RibbonMenuItem item)
    {
        var visual = CellStyleTileVisual.For(item.Header);
        var label = new TextBlock
        {
            Text = item.Header,
            FontSize = visual.FontSize,
            FontWeight = visual.Bold ? FontWeights.SemiBold : FontWeights.Normal,
            FontStyle = visual.Italic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = visual.Foreground,
            TextDecorations = visual.Underline ? TextDecorations.Underline : null,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 0, 8, 0),
        };
        var tile = new Button
        {
            Content = label,
            Height = titleHeight(item.Header),
            Margin = new Thickness(3),
            Background = visual.Background,
            BorderBrush = visual.Border,
            BorderThickness = visual.BorderThickness,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Tag = item.CommandId,
            ToolTip = item.Header,
        };
        AutomationProperties.SetName(tile, item.Header);
        tile.Click += (_, _) =>
        {
            CloseGallery();
            if (tile.Tag is RibbonCommandId commandId)
                _execute?.Invoke(commandId, tile);
        };
        return tile;

        static double titleHeight(string header) =>
            header is "Heading 1" or "Heading 2" ? 44 : 34;
    }

    private sealed record CellStyleSection(string Title, IReadOnlyList<string> Headers);

    private static readonly IReadOnlyList<CellStyleSection> CellStyleSections =
    [
        new("Good, Bad and Neutral", ["Normal", "Good", "Bad", "Neutral"]),
        new("Data and Model", ["Input", "Output", "Calculation", "Check Cell", "Linked Cell", "Explanatory Text"]),
        new("Titles and Headings", ["Heading 1", "Heading 2", "Heading 3", "Heading 4", "Title", "Note", "Warning Text", "Total"]),
        new("Themed Cell Styles", [
            "20% - Accent 1", "20% - Accent 2", "20% - Accent 3", "20% - Accent 4", "20% - Accent 5", "20% - Accent 6",
            "40% - Accent 1", "40% - Accent 2", "40% - Accent 3", "40% - Accent 4", "40% - Accent 5", "40% - Accent 6",
            "60% - Accent 1", "60% - Accent 2", "60% - Accent 3", "60% - Accent 4", "60% - Accent 5", "60% - Accent 6"]),
        new("Number Format", ["Currency", "Currency [0]", "Comma", "Comma [0]", "Percent"]),
        new("Hyperlink", ["Hyperlink", "Followed Hyperlink"]),
    ];
}

internal sealed record CellStyleTileVisual(
    Brush Background,
    Brush Foreground,
    Brush Border,
    Thickness BorderThickness,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    double FontSize = 14)
{
    public static CellStyleTileVisual For(string header)
    {
        var black = Brushes.Black;
        var muted = new SolidColorBrush(Color.FromRgb(89, 89, 89));
        var defaultBorder = new SolidColorBrush(Color.FromRgb(166, 166, 166));
        return header switch
        {
            "Normal" => new(Brushes.White, black, Brushes.Black, new Thickness(2)),
            "Good" => new(Brush("C6EFCE"), Brush("006100"), defaultBorder, new Thickness(1)),
            "Bad" => new(Brush("FFC7CE"), Brush("9C0006"), defaultBorder, new Thickness(1)),
            "Neutral" => new(Brush("FFEB9C"), Brush("9C6500"), defaultBorder, new Thickness(1)),
            "Input" => new(Brush("FFFFCC"), black, defaultBorder, new Thickness(1)),
            "Output" => new(Brush("F2F2F2"), black, Brush("7F7F7F"), new Thickness(1), Bold: true),
            "Calculation" => new(Brush("F2DCDB"), Brush("C65911"), defaultBorder, new Thickness(1), Bold: true),
            "Check Cell" => new(Brush("FCE4D6"), Brush("9C5700"), defaultBorder, new Thickness(1), Bold: true),
            "Linked Cell" => new(Brush("DDEBF7"), Brush("0563C1"), defaultBorder, new Thickness(1), Underline: true),
            "Explanatory Text" => new(Brush("F2F2F2"), muted, defaultBorder, new Thickness(1), Italic: true),
            "Heading 1" => new(Brushes.White, Brush("17365D"), Brush("5B9BD5"), new Thickness(0, 0, 0, 2), Bold: true, FontSize: 19),
            "Heading 2" => new(Brushes.White, Brush("1F4E79"), Brush("5B9BD5"), new Thickness(0, 0, 0, 2), Bold: true, FontSize: 16),
            "Heading 3" => new(Brushes.White, Brush("1F4E79"), Brush("5B9BD5"), new Thickness(0, 0, 0, 1), Bold: true),
            "Heading 4" => new(Brushes.White, Brush("1F4E79"), defaultBorder, new Thickness(0, 0, 0, 1), Bold: true),
            "Title" => new(Brushes.White, Brush("17365D"), Brushes.Transparent, new Thickness(0), Bold: true, FontSize: 18),
            "Note" => new(Brush("FFF2CC"), black, defaultBorder, new Thickness(1)),
            "Warning Text" => new(Brush("FFC000"), black, defaultBorder, new Thickness(1), Bold: true),
            "Total" => new(Brushes.White, black, Brushes.Black, new Thickness(0, 1, 0, 2), Bold: true),
            "Hyperlink" => new(Brushes.White, Brush("0563C1"), defaultBorder, new Thickness(1), Underline: true),
            "Followed Hyperlink" => new(Brushes.White, Brush("954F72"), defaultBorder, new Thickness(1), Underline: true),
            _ when header.Contains("Accent", StringComparison.Ordinal) => Accent(header, defaultBorder),
            _ => new(Brushes.White, black, defaultBorder, new Thickness(1)),
        };
    }

    private static CellStyleTileVisual Accent(string header, Brush border)
    {
        var index = int.TryParse(header[^1..], out var parsed) ? parsed : 1;
        var accent = index switch
        {
            1 => Color.FromRgb(91, 155, 213),
            2 => Color.FromRgb(237, 125, 49),
            3 => Color.FromRgb(165, 165, 165),
            4 => Color.FromRgb(255, 192, 0),
            5 => Color.FromRgb(68, 114, 196),
            _ => Color.FromRgb(112, 173, 71),
        };
        var mix = header.StartsWith("20%", StringComparison.Ordinal) ? .80 :
            header.StartsWith("40%", StringComparison.Ordinal) ? .60 : .40;
        var background = Color.FromRgb(
            (byte)(255 - ((255 - accent.R) * mix)),
            (byte)(255 - ((255 - accent.G) * mix)),
            (byte)(255 - ((255 - accent.B) * mix)));
        return new CellStyleTileVisual(new SolidColorBrush(background), Brushes.Black, border, new Thickness(1));
    }

    private static Brush Brush(string hex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString($"#{hex}")!);
}
