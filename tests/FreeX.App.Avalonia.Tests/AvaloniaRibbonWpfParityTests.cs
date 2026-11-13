using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// WPF parity for shared Avalonia ribbon rendering: tooltips on every control, gallery number-format
/// rows, the categorized cell-style gallery, natural-width large captions and the fixed ribbon height.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaRibbonWpfParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static Task RunOnUiThread(Action action) => Session.Dispatch(action, CancellationToken.None);

    private static Control Show(RibbonTab tab, double width = 900)
    {
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, registry: null);
        var window = new Window { Width = width, Height = 160, Content = content };
        window.Show();
        window.Measure(new Size(width, 160));
        window.Arrange(new Rect(0, 0, width, 160));
        return content;
    }

    private static string TipText(Control control)
    {
        var tip = ToolTip.GetTip(control);
        Assert.NotNull(tip);
        var panel = Assert.IsAssignableFrom<Panel>(tip);
        return string.Join("|", panel.Children.OfType<TextBlock>().Select(text => text.Text));
    }

    [Fact]
    public Task EveryControlKind_CarriesTitleDescriptionAndKeyTipTooltip() => RunOnUiThread(() =>
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab => tab.Group("g", "Group", "G", 100, group =>
            {
                group.Button("hero", "Hero", c => c with
                {
                    PreferredLayout = RibbonCommandLayoutKind.Large,
                    KeyTip = "HE",
                    TooltipTitle = "Hero Title",
                    TooltipDescription = "Hero description.",
                });
                group.Button("mid", "Middle", c => c with { TooltipDescription = "Press Ctrl+B to bold." });
                group.Button("icon", "Icon", c => c with { PreferredLayout = RibbonCommandLayoutKind.Small });
                group.SplitButton("split", "Split", new RibbonMenu([new RibbonMenuItem("One", "one")]),
                    c => c with { PreferredLayout = RibbonCommandLayoutKind.Large, TooltipDescription = "Split description." });
                group.ComboBox("combo", "Combo", c => c with { Items = ["A", "B"], TooltipDescription = "Combo description." });
                group.CheckBox("check", "Check", c => c with { TooltipDescription = "Check description." });
            }))
            .Build();

        var content = Show(definition.FindTab("home")!);
        var tipped = content.GetLogicalDescendants().OfType<Control>()
            .Where(control => control is Button or ComboBox or CheckBox)
            .Where(control => ToolTip.GetTip(control) is not null)
            .ToList();

        var hero = tipped.OfType<Button>().Single(b => Equals(b.Tag, "hero"));
        Assert.Equal("Hero Title (HE)|Hero description.", TipText(hero));
        Assert.Equal(500, ToolTip.GetShowDelay(hero));
        var mid = tipped.OfType<Button>().Single(b => Equals(b.Tag, "mid"));
        Assert.StartsWith("Middle|", TipText(mid));
        Assert.Contains(tipped.OfType<Button>(), b => Equals(b.Tag, "icon"));
        Assert.Contains(tipped.OfType<Button>(), b => Equals(b.Tag, "split"));
        Assert.Contains(tipped.OfType<Button>(), b => Equals(b.Tag, "split.Dropdown"));
        Assert.Contains(tipped, c => c is ComboBox);
        Assert.Contains(tipped, c => c is CheckBox);
        // Windows/Linux keep Ctrl; macOS shows Cmd.
        Assert.Contains(OperatingSystem.IsMacOS() ? "Cmd+B" : "Ctrl+B", TipText(mid));
    });

    [Fact]
    public Task GalleryComboBox_RendersPreviewLabelAndDescriptionRows() => RunOnUiThread(() =>
    {
        var choices = new[]
        {
            new RibbonComboBoxChoice("General", "General", "No specific format", RibbonComboBoxGalleryPreviewKind.General),
            new RibbonComboBoxChoice("Currency", "Currency", "$1,234.00", RibbonComboBoxGalleryPreviewKind.Currency),
            new RibbonComboBoxChoice("More", "More Number Formats...", null, RibbonComboBoxGalleryPreviewKind.More),
        };
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab => tab.Group("n", "Number", "N", 100, group =>
                group.ComboBox("fmt", "Number Format", c => c with
                {
                    PresentationKind = RibbonComboBoxPresentationKind.Gallery,
                    Choices = choices,
                    Width = 124,
                })))
            .Build();

        var content = Show(definition.FindTab("home")!);
        var combo = content.GetLogicalDescendants().OfType<ComboBox>().Single();
        Assert.NotNull(combo.ItemTemplate);
        Assert.Equal(640, combo.MaxDropDownHeight);

        var row = Assert.IsType<Grid>(combo.ItemTemplate!.Build(choices[0])!);
        Assert.Equal(60, row.Height);
        var labels = row.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
        Assert.Contains("General", labels);
        Assert.Contains("No specific format", labels);
        Assert.Contains(row.GetLogicalDescendants(), c => c.GetType().Name == "AvaloniaRibbonGalleryPreview");
        var more = Assert.IsType<Grid>(combo.ItemTemplate.Build(choices[2])!);
        Assert.Equal(38, more.Height);
    });

    [Fact]
    public Task CellStylesDropdown_OpensCategorizedGalleryInsteadOfTextMenu() => RunOnUiThread(() =>
    {
        var menu = new RibbonMenu([
            new RibbonMenuItem("Normal", "Normal"),
            RibbonMenuItem.Separator(),
            new RibbonMenuItem("Good", "Good"),
            new RibbonMenuItem("Bad", "Bad"),
            RibbonMenuItem.Separator(),
            new RibbonMenuItem("20% - Accent 1", "20% - Accent 1"),
            new RibbonMenuItem("Currency", "Currency"),
        ]);
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab => tab.Group("s", "Styles", "Y", 100, group =>
                group.Dropdown("Cell Styles", "Cell Styles", menu, c => c with
                {
                    PreferredLayout = RibbonCommandLayoutKind.Large,
                    PresentationKind = RibbonDropdownPresentationKind.CellStyleGallery,
                })))
            .Build();

        var content = Show(definition.FindTab("home")!);
        var button = content.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, "Cell Styles"));
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        var gallery = Assert.IsAssignableFrom<Control>(Assert.Single(flyout.Items.Cast<object>()));
        var border = Assert.IsType<Border>(gallery);
        Assert.Equal(510, border.MinWidth);
        var headings = border.GetLogicalDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).Where(t => t is "Good, Bad and Neutral" or "Themed Cell Styles" or "Number Format").ToArray();
        Assert.Equal(["Good, Bad and Neutral", "Themed Cell Styles", "Number Format"], headings);
        var tiles = border.GetLogicalDescendants().OfType<Button>().Select(b => (string)b.Tag!).ToArray();
        Assert.Equal(["Normal", "Good", "Bad", "20% - Accent 1", "Currency"], tiles);
    });

    [Fact]
    public Task LargeCaption_WithLongWord_WidensButtonInsteadOfCropping() => RunOnUiThread(() =>
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("insert", "Insert", "N", tab => tab.Group("c", "Charts", "C", 100, group =>
            {
                group.Button("rec", "Recommended Charts", c => c with { PreferredLayout = RibbonCommandLayoutKind.Large });
                group.Button("tbl", "Table", c => c with { PreferredLayout = RibbonCommandLayoutKind.Large });
            }))
            .Build();

        var content = Show(definition.FindTab("insert")!, 1000);
        var rec = content.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, "rec"));
        var tbl = content.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, "tbl"));
        var captions = rec.GetLogicalDescendants().OfType<TextBlock>().Single();
        var longest = captions.Text!.Split(' ').Max(word =>
        {
            var probe = new TextBlock { Text = word, FontSize = captions.FontSize, FontFamily = captions.FontFamily };
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Width;
        });
        Assert.True(rec.Width >= longest, $"button {rec.Width} must fit longest word {longest}");
        Assert.True(captions.Bounds.Width >= longest - 0.5);
        Assert.Equal(80, tbl.Width);
    });

    [Fact]
    public Task RibbonTabControl_PinsWpfMinHeightOnMacOs() => RunOnUiThread(() =>
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab => tab.Group("g", "G", "G", 100, group => group.Button("b", "B")))
            .Build();
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry: null);
        var tabs = ribbon as TabControl ?? ribbon.GetLogicalDescendants().OfType<TabControl>().First();
        if (OperatingSystem.IsMacOS())
            Assert.Equal(132, tabs.MinHeight);
        else
            Assert.Equal(0, tabs.MinHeight);
    });
}
