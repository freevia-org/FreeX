using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Free.Shared.Ribbon.Avalonia;

/// <summary>
/// Avalonia counterpart of WPF <c>RibbonCellStyleGalleryButton</c>: the ribbon menu of a
/// <see cref="RibbonDropdownPresentationKind.CellStyleGallery"/> dropdown is shown as a 510px
/// categorized gallery of live style tiles (three per row, section headings) instead of a text menu.
/// The host still owns the commands: each tile executes the menu item's command id.
/// </summary>
internal static class AvaloniaRibbonCellStyleGallery
{
    internal const double PopupWidth = 510;
    internal const double PopupMaxHeight = 620;

    private sealed record Section(string Title, string[] Headers);

    private static readonly Section[] Sections =
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

    internal static bool IsGallery(RibbonControl control, RibbonMenu? menu) =>
        control is RibbonDropdown { PresentationKind: RibbonDropdownPresentationKind.CellStyleGallery }
        && menu is not null
        && menu.Items.Any(item => item.Kind == RibbonMenuItemKind.Command && item.CommandId is not null);

    internal static MenuFlyout BuildFlyout(RibbonMenu menu, FontFamily fontFamily, Action<RibbonCommandId> execute)
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(BuildGallery(menu, fontFamily, execute, () => flyout.Hide()));
        // MenuFlyoutPresenter caps its width for text menus; the gallery needs the full 510px (three
        // 165px tiles per row) and WPF's white 1px #5C5C5C frame.
        flyout.Opened += (_, _) =>
        {
            if (flyout.Popup.Child is Control presenter)
            {
                presenter.MaxWidth = double.PositiveInfinity;
                presenter.MinWidth = PopupWidth;
                if (presenter is TemplatedControl templated)
                {
                    templated.Padding = new Thickness(0);
                    templated.Background = Brushes.White;
                    templated.BorderBrush = new SolidColorBrush(Color.FromRgb(92, 92, 92));
                    templated.BorderThickness = new Thickness(1);
                }
            }
        };
        return flyout;
    }

    internal static Control BuildGallery(RibbonMenu menu, FontFamily fontFamily, Action<RibbonCommandId> execute, Action close)
    {
        var choices = menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command && item.CommandId is not null)
            .ToArray();
        var sections = new StackPanel();
        foreach (var section in Sections)
        {
            var items = choices.Where(item => section.Headers.Contains(item.Header, StringComparer.Ordinal)).ToArray();
            if (items.Length > 0)
                sections.Children.Add(CreateSection(section.Title, items, fontFamily, execute, close));
        }

        var assigned = Sections.SelectMany(section => section.Headers).ToHashSet(StringComparer.Ordinal);
        var remaining = choices.Where(item => !assigned.Contains(item.Header)).ToArray();
        if (remaining.Length > 0)
            sections.Children.Add(CreateSection("Number Format", remaining, fontFamily, execute, close));

        return new Border
        {
            MinWidth = PopupWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.White,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = PopupMaxHeight,
                Content = sections,
            },
        };
    }

    private static Control CreateSection(
        string title,
        IReadOnlyList<RibbonMenuItem> items,
        FontFamily fontFamily,
        Action<RibbonCommandId> execute,
        Action close)
    {
        var section = new StackPanel { Margin = new Thickness(8, 6, 8, 2) };
        section.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = fontFamily,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Margin = new Thickness(4, 0, 0, 4),
        });

        var tiles = new UniformGrid { Columns = 3 };
        foreach (var item in items)
            tiles.Children.Add(CreateTile(item, fontFamily, execute, close));
        section.Children.Add(tiles);
        return section;
    }

    private static Control CreateTile(RibbonMenuItem item, FontFamily fontFamily, Action<RibbonCommandId> execute, Action close)
    {
        var visual = TileVisual.For(item.Header);
        var label = new TextBlock
        {
            Text = item.Header,
            FontFamily = fontFamily,
            FontSize = visual.FontSize,
            FontWeight = visual.Bold ? FontWeight.SemiBold : FontWeight.Normal,
            FontStyle = visual.Italic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = visual.Foreground,
            TextDecorations = visual.Underline ? TextDecorations.Underline : null,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 0),
        };
        var tile = new Button
        {
            Content = new Border
            {
                Background = visual.Background,
                BorderBrush = visual.Border,
                BorderThickness = visual.BorderThickness,
                Child = label,
            },
            Height = item.Header is "Heading 1" or "Heading 2" ? 44 : 34,
            Margin = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Tag = item.CommandId?.Value,
            IsEnabled = item.IsEnabled,
        };
        ToolTip.SetTip(tile, item.Header);
        AutomationProperties.SetName(tile, item.Header);
        tile.Click += (_, _) =>
        {
            close();
            if (item.CommandId is { } commandId)
                execute(commandId);
        };
        return tile;
    }

    internal sealed record TileVisual(
        IBrush Background,
        IBrush Foreground,
        IBrush Border,
        Thickness BorderThickness,
        bool Bold = false,
        bool Italic = false,
        bool Underline = false,
        double FontSize = 14)
    {
        public static TileVisual For(string header)
        {
            IBrush black = Brushes.Black;
            IBrush muted = new SolidColorBrush(Color.FromRgb(89, 89, 89));
            IBrush defaultBorder = new SolidColorBrush(Color.FromRgb(166, 166, 166));
            return header switch
            {
                "Normal" => new(Brushes.White, black, Brushes.Black, new Thickness(2)),
                "Good" => new(Fill("C6EFCE"), Fill("006100"), defaultBorder, new Thickness(1)),
                "Bad" => new(Fill("FFC7CE"), Fill("9C0006"), defaultBorder, new Thickness(1)),
                "Neutral" => new(Fill("FFEB9C"), Fill("9C6500"), defaultBorder, new Thickness(1)),
                "Input" => new(Fill("FFFFCC"), black, defaultBorder, new Thickness(1)),
                "Output" => new(Fill("F2F2F2"), black, Fill("7F7F7F"), new Thickness(1), Bold: true),
                "Calculation" => new(Fill("F2DCDB"), Fill("C65911"), defaultBorder, new Thickness(1), Bold: true),
                "Check Cell" => new(Fill("FCE4D6"), Fill("9C5700"), defaultBorder, new Thickness(1), Bold: true),
                "Linked Cell" => new(Fill("DDEBF7"), Fill("0563C1"), defaultBorder, new Thickness(1), Underline: true),
                "Explanatory Text" => new(Fill("F2F2F2"), muted, defaultBorder, new Thickness(1), Italic: true),
                "Heading 1" => new(Brushes.White, Fill("17365D"), Fill("5B9BD5"), new Thickness(0, 0, 0, 2), Bold: true, FontSize: 19),
                "Heading 2" => new(Brushes.White, Fill("1F4E79"), Fill("5B9BD5"), new Thickness(0, 0, 0, 2), Bold: true, FontSize: 16),
                "Heading 3" => new(Brushes.White, Fill("1F4E79"), Fill("5B9BD5"), new Thickness(0, 0, 0, 1), Bold: true),
                "Heading 4" => new(Brushes.White, Fill("1F4E79"), defaultBorder, new Thickness(0, 0, 0, 1), Bold: true),
                "Title" => new(Brushes.White, Fill("17365D"), Brushes.Transparent, new Thickness(0), Bold: true, FontSize: 18),
                "Note" => new(Fill("FFF2CC"), black, defaultBorder, new Thickness(1)),
                "Warning Text" => new(Fill("FFC000"), black, defaultBorder, new Thickness(1), Bold: true),
                "Total" => new(Brushes.White, black, Brushes.Black, new Thickness(0, 1, 0, 2), Bold: true),
                "Hyperlink" => new(Brushes.White, Fill("0563C1"), defaultBorder, new Thickness(1), Underline: true),
                "Followed Hyperlink" => new(Brushes.White, Fill("954F72"), defaultBorder, new Thickness(1), Underline: true),
                _ when header.Contains("Accent", StringComparison.Ordinal) => Accent(header, defaultBorder),
                _ => new(Brushes.White, black, defaultBorder, new Thickness(1)),
            };
        }

        private static TileVisual Accent(string header, IBrush border)
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
            return new TileVisual(new SolidColorBrush(background), Brushes.Black, border, new Thickness(1));
        }

        private static IBrush Fill(string hex) => new SolidColorBrush(Color.Parse($"#{hex}"));
    }
}
