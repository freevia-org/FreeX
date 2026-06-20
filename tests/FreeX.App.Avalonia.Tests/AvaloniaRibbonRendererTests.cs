using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using FreeX.App.Avalonia.Ribbon;
using Free.Shared.Ribbon;
using FreeX.Ribbon.Avalonia;
using Free.Shared.Ribbon.Icons;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

[assembly: AvaloniaTestApplication(typeof(FreeX.App.Avalonia.Tests.RibbonHeadlessApp))]

namespace FreeX.App.Avalonia.Tests;

/// <summary>Minimal headless Avalonia app providing a Fluent theme so styled controls can measure.</summary>
public sealed class RibbonHeadlessApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<RibbonHeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

public sealed class AvaloniaRibbonRendererTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static Task RunOnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    private static RibbonTab BuildHomeTab()
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", home =>
            {
                home.Group("clipboard", "Clipboard", "C", 100, g =>
                {
                    g.SplitButton("paste", "Paste", new RibbonMenu(new[]
                    {
                        new RibbonMenuItem("Paste", "paste"),
                        RibbonMenuItem.Separator(),
                        new RibbonMenuItem("Paste Special", "pasteSpecial"),
                    }), c => c with { PreferredLayout = RibbonCommandLayoutKind.Large });
                    g.Button("cut", "Cut");
                    g.Button("copy", "Copy");
                });
                home.Group("font", "Font", "F", 90, g =>
                {
                    g.ComboBox("fontName", "Font", c => c with { Items = new[] { "Calibri", "Arial" } });
                    g.Toggle("bold", "Bold");
                    g.Separator();
                    g.Dropdown("fill", "Fill", new RibbonMenu(new[]
                    {
                        new RibbonMenuItem("Yellow", "fillYellow"),
                    }));
                });
            })
            .Build();

        return definition.FindTab("home")!;
    }

    [Fact]
    public Task BuildTabContent_ProducesNonEmptyVisualTree() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        var registry = new RibbonCommandRegistry();
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, registry);

        var window = new Window { Width = 1200, Height = 200, Content = content };
        window.Show();
        window.Measure(new Size(1200, 200));
        window.Arrange(new Rect(0, 0, 1200, 200));

        var descendants = content.GetLogicalDescendants().ToList();
        Assert.NotEmpty(descendants);

        // One group header label per group (2 groups).
        var headers = descendants.OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Clipboard", headers);
        Assert.Contains("Font", headers);

        // Buttons (Paste/Cut/Copy/Fill dropdown) and a toggle (Bold) were created.
        Assert.True(descendants.OfType<Button>().Count() >= 4);
        Assert.Contains(descendants, d => d is ToggleButton);

        // The combo box rendered with its items.
        var combo = descendants.OfType<ComboBox>().Single();
        Assert.Equal(2, combo.Items.Count);

        // The content actually took up space once arranged.
        Assert.True(content.Bounds.Width > 0);
        Assert.True(content.Bounds.Height > 0);
    });

    [Fact]
    public Task BuildTabContent_GroupCount_MatchesDefinition() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, registry: null);

        var groupGrids = content.GetLogicalDescendants()
            .OfType<Grid>()
            .Where(g => g.Tag is string)
            .Select(g => (string)g.Tag!)
            .ToList();

        Assert.Equal(new[] { "clipboard", "font" }, groupGrids);
    });

    [Fact]
    public Task BuildTabContent_NarrowWidth_CollapsesRibbonGroups() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, registry: null);

        var window = new Window { Width = 180, Height = 200, Content = content };
        window.Show();
        window.Measure(new Size(180, 200));
        window.Arrange(new Rect(0, 0, 180, 200));

        var collapsedButtons = content.GetLogicalDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("freex-ribbon-collapsed-group"))
            .ToList();

        Assert.NotEmpty(collapsedButtons);
        Assert.All(collapsedButtons, b => Assert.IsType<MenuFlyout>(b.Flyout));
        Assert.True(content.Bounds.Width <= 180);
    });

    [Fact]
    public Task BuildTabContent_WideWidth_KeepsRibbonGroupsExpanded() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, registry: null);

        var window = new Window { Width = 1200, Height = 200, Content = content };
        window.Show();
        window.Measure(new Size(1200, 200));
        window.Arrange(new Rect(0, 0, 1200, 200));

        var collapsedButtons = content.GetLogicalDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("freex-ribbon-collapsed-group"))
            .ToList();

        Assert.Empty(collapsedButtons);
    });

    [Fact]
    public Task Dropdown_ButtonHasFlyout_BuiltFromMenu() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, new RibbonCommandRegistry());

        var flyoutButton = content.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Flyout is MenuFlyout);

        Assert.NotNull(flyoutButton);
        var flyout = (MenuFlyout)flyoutButton!.Flyout!;
        Assert.NotEmpty(flyout.Items);
        // Paste menu: Paste, Separator, Paste Special.
        Assert.Contains(flyout.Items, i => i is MenuItem);
        Assert.Contains(flyout.Items, i => i is Separator);
    });

    [Fact]
    public Task BuildRibbon_ProducesTabPerVisibleTab() => RunOnUiThread(() =>
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);

        var tabControl = Assert.IsType<TabControl>(ribbon);
        Assert.Equal(definition.VisibleTabs.Count(), tabControl.Items.Count);
        Assert.All(tabControl.Items, item => Assert.IsType<TabItem>(item));
    });

    [Fact]
    public Task BuildRibbon_ContextSource_ShowsAndHidesContextualTabs() => RunOnUiThread(() =>
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", t => t.Group("g", "G", "G", 1, g => g.Button("b", "B")))
            .ContextualTab("chart", "Chart Design", new RibbonTabContext("chart.selected", "Chart Design", RibbonContextColor.Green),
                t => t.Group("cg", "CG", "C", 1, g => g.Button("cb", "CB")))
            .Build();

        var source = new FakeContextSource();
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry: null, source);
        var tabControl = Assert.IsType<TabControl>(ribbon);

        static IEnumerable<string> TabIds(TabControl tc) =>
            tc.Items.OfType<TabItem>().Select(i => (string)i.Tag!);

        // Only the static tab initially.
        Assert.Equal(new[] { "home" }, TabIds(tabControl).ToArray());

        // Activating the chart context inserts the contextual tab.
        source.Raise(RibbonContextState.None.With("chart.selected"));
        Assert.Equal(new[] { "home", "chart" }, TabIds(tabControl).ToArray());

        // Selecting it, then clearing context, removes it and falls back to the first tab.
        tabControl.SelectedIndex = 1;
        source.Raise(RibbonContextState.None);
        Assert.Equal(new[] { "home" }, TabIds(tabControl).ToArray());
        Assert.Equal(0, tabControl.SelectedIndex);
    });

    private sealed class FakeContextSource : IRibbonContextSource
    {
        public RibbonContextState Current { get; private set; } = RibbonContextState.None;
        public event EventHandler? ContextChanged;
        public void Raise(RibbonContextState state)
        {
            Current = state;
            ContextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Fact]
    public Task UnregisteredCommand_RendersDisabled() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        // Empty registry => every command id is unregistered => controls disabled.
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, new RibbonCommandRegistry());

        var buttons = content.GetLogicalDescendants().OfType<Button>().ToList();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, b => Assert.False(b.IsEnabled));
    });

    [Fact]
    public Task IconBuild_ProducesVectorShapes_NotTextGlyphs() => RunOnUiThread(() =>
    {
        // Save is a multi-line vector icon: it must render as native Avalonia shapes, proving the
        // Avalonia renderer draws the shared neutral geometry rather than a single TextBlock glyph.
        var icon = AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Save, 24);

        var viewbox = Assert.IsType<Viewbox>(icon);
        var visuals = viewbox.GetVisualDescendants().ToList();

        // The Save geometry is made entirely of lines.
        Assert.Contains(visuals, v => v is Line);
        // And it is definitely not a lone text glyph.
        Assert.DoesNotContain(visuals, v => v is TextBlock);
    });

    [Fact]
    public Task IconBuild_ShapeIconHasPathAndEllipse() => RunOnUiThread(() =>
    {
        // Protect (shield) mixes a stroked path and a check path; Cut mixes lines and ellipses.
        var shield = AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Protect, 24);
        Assert.Contains(((Viewbox)shield).GetVisualDescendants(), v => v is AvaloniaPath);

        var cut = AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Cut, 24);
        var cutVisuals = ((Viewbox)cut).GetVisualDescendants().ToList();
        Assert.Contains(cutVisuals, v => v is Line);
        Assert.Contains(cutVisuals, v => v is Ellipse);
    });

    [Fact]
    public Task IconBuild_EveryKind_ProducesVisualsAndScales() => RunOnUiThread(() =>
    {
        foreach (var kind in Enum.GetValues<RibbonCommandIconKind>())
        {
            var icon = AvaloniaRibbonIcons.Build(kind, 22);
            var viewbox = Assert.IsType<Viewbox>(icon);
            Assert.Equal(22, viewbox.Width);

            var canvas = Assert.IsType<Canvas>(viewbox.Child);
            Assert.NotEmpty(canvas.Children);
        }
    });

    [Fact]
    public Task IconBuild_Accent_ColorsTheShape() => RunOnUiThread(() =>
    {
        // A non-None accent should color the geometry with the accent color, ignoring the foreground.
        var accented = new RibbonCommandIcon(RibbonCommandIconKind.Save, RibbonCommandIconAccent.Warning);
        var icon = AvaloniaRibbonIcons.Build(accented, 24, Brushes.Black);

        var expected = RibbonIconAccents.Resolve(RibbonCommandIconAccent.Warning)!.Value;
        var lines = ((Viewbox)icon).GetVisualDescendants().OfType<Line>().ToList();
        Assert.NotEmpty(lines);

        var stroke = Assert.IsType<SolidColorBrush>(lines[0].Stroke);
        Assert.Equal(Color.FromArgb(expected.A, expected.R, expected.G, expected.B), stroke.Color);
    });

    [Fact]
    public Task LargeControl_EmbedsIconShapes() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, new RibbonCommandRegistry());

        var window = new Window { Width = 1200, Height = 200, Content = content };
        window.Show();
        window.Measure(new Size(1200, 200));
        window.Arrange(new Rect(0, 0, 1200, 200));

        // The rendered ribbon should contain real icon shapes (from the neutral geometry), not just text.
        var shapes = content.GetVisualDescendants().OfType<Shape>().ToList();
        Assert.NotEmpty(shapes);
    });

    [Theory]
    [InlineData("Paste")]
    [InlineData("Format Painter")]
    [InlineData("Conditional Formatting")]
    public Task IconBuild_KnownCommand_LoadsSharedSvg(string commandName) => RunOnUiThread(() =>
    {
        // A command with a matching CommandIconsSvg/<slug>.svg must render the SAME shared SVG the WPF
        // host loads, parsed natively into an Avalonia Image backed by a DrawingImage (no external SVG
        // library), not the fallback kind glyph.
        var icon = AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Generic, 32, commandName);

        var image = Assert.IsType<Image>(icon);
        var drawingImage = Assert.IsType<DrawingImage>(image.Source);
        var group = Assert.IsType<DrawingGroup>(drawingImage.Drawing);
        Assert.NotEmpty(group.Children);
        Assert.Equal(32, image.Width);
    });

    [Fact]
    public Task IconBuild_UnknownCommand_FallsBackToKindGlyph() => RunOnUiThread(() =>
    {
        // A command name with no SVG file falls back to the neutral kind glyph (a Viewbox over a Canvas),
        // exactly like the WPF host.
        var icon = AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Save, 24, "No Such Command Xyzzy");

        var viewbox = Assert.IsType<Viewbox>(icon);
        Assert.IsType<Canvas>(viewbox.Child);
    });
}
