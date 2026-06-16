using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using FreeX.App.Avalonia.Ribbon;
using Free.Shared.Ribbon;
using FreeX.Ribbon.Avalonia;

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
        var definition = SampleRibbon.BuildDefinition();
        var registry = SampleRibbon.BuildRegistry();
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);

        var tabControl = Assert.IsType<TabControl>(ribbon);
        Assert.Equal(definition.VisibleTabs.Count(), tabControl.Items.Count);
        Assert.All(tabControl.Items, item => Assert.IsType<TabItem>(item));
    });

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
}
