using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class AvaloniaRibbonAutomationPeerTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(RibbonCommandLayoutKind.Large)]
    [InlineData(RibbonCommandLayoutKind.Medium)]
    [InlineData(RibbonCommandLayoutKind.Small)]
    public Task VisualCommandContent_ExposesLocalizedNamesAndDescriptions(RibbonCommandLayoutKind layout) =>
        Run(() => AvaloniaRibbonRenderer.BuildTabContent(BuildTab(group => group
            .Button("copy", "Copier", control => control with
            {
                PreferredLayout = layout,
                TooltipDescription = "Copie la sélection.",
            })
            .Toggle("bold", "Gras", control => control with
            {
                PreferredLayout = layout,
                TooltipTitle = "Mettre en gras",
                TooltipDescription = "Met le texte sélectionné en gras.",
            })
            .Dropdown("styles", "Styles", new RibbonMenu([new RibbonMenuItem("Normal", "normal")]),
                control => control with { PreferredLayout = layout })
            .CheckBox("headings", "En-têtes", control => control with
            {
                TooltipDescription = "Affiche les en-têtes.",
            }))), content =>
        {
            Peer(FindCommand(content, "copy")).GetName().Should().Be("Copier");
            Peer(FindCommand(content, "copy")).GetHelpText().Should().Be("Copie la sélection.");
            Peer(FindCommand(content, "bold")).GetName().Should().Be("Mettre en gras");
            Peer(FindCommand(content, "bold")).GetHelpText().Should().Be("Met le texte sélectionné en gras.");
            Peer(FindCommand(content, "styles")).GetName().Should().Be("Styles");
            Peer(FindCommand(content, "headings")).GetName().Should().Be("En-têtes");
            Peer(FindCommand(content, "headings")).GetHelpText().Should().Be("Affiche les en-têtes.");
        });

    [Theory]
    [InlineData(RibbonCommandLayoutKind.Large)]
    [InlineData(RibbonCommandLayoutKind.Medium)]
    [InlineData(RibbonCommandLayoutKind.Small)]
    public Task SplitButton_BothInteractivePartsExposeTheirCommandIdentity(RibbonCommandLayoutKind layout) =>
        Run(() => AvaloniaRibbonRenderer.BuildTabContent(BuildTab(group => group
            .SplitButton("paste", "Coller", new RibbonMenu([new RibbonMenuItem("Collage spécial", "pasteSpecial")]),
                control => control with
                {
                    PreferredLayout = layout,
                    TooltipTitle = "Collage",
                    TooltipDescription = "Insère le contenu copié.",
                }))), content =>
        {
            foreach (var commandId in new[] { "paste", "paste.Dropdown" })
            {
                var peer = Peer(FindCommand(content, commandId));
                peer.GetName().Should().Be("Collage");
                peer.GetHelpText().Should().Be("Insère le contenu copié.");
            }
        });

    [Fact]
    public Task Tabs_ExposeTheirHeaderInsteadOfTheHeaderVisualType() =>
        Run(() => AvaloniaRibbonRenderer.BuildRibbon(new RibbonDefinitionBuilder()
            .Tab("home", "Accueil", "H", _ => { })
            .Tab("insert", "Insertion", "I", _ => { })
            .Build()), content =>
        {
            var tabs = Assert.IsType<TabControl>(content).Items.OfType<TabItem>().ToArray();
            tabs.Select(tab => Peer(tab).GetName()).Should().Equal("File", "Accueil", "Insertion");
        });

    [Fact]
    public Task EditableCombo_ExposesFieldIdentitySeparatelyFromItsCurrentValue() =>
        Run(() => AvaloniaRibbonRenderer.BuildTabContent(BuildTab(group => group
            .ComboBox("font", "Police", combo => combo with
            {
                Items = ["Calibri", "Arial"],
                TooltipDescription = "Choisit une police.",
            }))), content =>
        {
            var combo = Assert.IsType<ComboBox>(FindCommand(content, "font"));
            var editor = combo.GetVisualDescendants().OfType<TextBox>().Single();
            Peer(combo).GetName().Should().Be("Police");
            Peer(editor).GetName().Should().Be("Police");
            Peer(editor).GetHelpText().Should().Be("Choisit une police.");
            Peer(editor).GetProvider<IValueProvider>()!.Value.Should().Be("Calibri");
        });

    [Fact]
    public Task EditableCombo_PreservesHostAuthoredAccessibilityMetadataInItsEditor() =>
        Run(() =>
        {
            var content = AvaloniaRibbonRenderer.BuildTabContent(BuildTab(group => group
                .ComboBox("font", "Police", combo => combo with
                {
                    Items = ["Calibri"],
                    TooltipDescription = "Description du catalogue.",
                })));
            var combo = Assert.IsType<ComboBox>(FindCommand(content, "font"));
            AutomationProperties.SetName(combo, "Document font");
            AutomationProperties.SetHelpText(combo, "Choose this document's font.");
            return content;
        }, content =>
        {
            var combo = Assert.IsType<ComboBox>(FindCommand(content, "font"));
            var editor = combo.GetVisualDescendants().OfType<TextBox>().Single();
            Peer(combo).GetName().Should().Be("Document font");
            Peer(editor).GetName().Should().Be("Document font");
            Peer(editor).GetHelpText().Should().Be("Choose this document's font.");
        });

    [Fact]
    public Task GroupLauncher_ExposesDialogTitleAndDescription() =>
        Run(() => AvaloniaRibbonRenderer.BuildTabContent(BuildTab(group => group
            .DialogLauncher("fontDialog", "Format de police", "Ouvre la boîte de dialogue Police.")
            .Button("bold", "Gras"))), content =>
        {
            var peer = Peer(FindCommand(content, "fontDialog"));
            peer.GetName().Should().Be("Format de police");
            peer.GetHelpText().Should().Be("Ouvre la boîte de dialogue Police.");
        });

    [Fact]
    public Task CollapsedGroup_ExposesTheGroupNameInsteadOfItsVisualContentType() =>
        Run(() => AvaloniaRibbonRenderer.BuildTabContent(BuildTab(group => group
            .Large("copy", "Copier", RibbonCommandIconKind.Copy, "C")
            .Large("paste", "Coller", RibbonCommandIconKind.Paste, "V"))), content =>
        {
            var collapsed = content.GetLogicalDescendants().OfType<Button>()
                .Single(button => Equals(button.Tag, "collapsed:group"));
            Peer(collapsed).GetName().Should().Be("Groupe");
        }, width: 90);

    private static RibbonTab BuildTab(Action<RibbonGroupBuilder> controls) =>
        new RibbonDefinitionBuilder()
            .Tab("home", "Accueil", "H", tab => tab.Group("group", "Groupe", "G", 1, controls))
            .Build().FindTab("home")!;

    private static Control FindCommand(Control content, string commandId) =>
        content.GetLogicalDescendants().OfType<Control>().Single(control => Equals(control.Tag, commandId));

    private static AutomationPeer Peer(Control control) => ControlAutomationPeer.CreatePeerForElement(control);

    private static Task Run(Func<Control> create, Action<Control> test, double width = 1200) =>
        Session.Dispatch(() =>
        {
            var content = create();
            var window = new Window { Width = width, Height = 240, Content = content };
            try
            {
                window.Show();
                window.Measure(new Size(width, 240));
                window.Arrange(new Rect(0, 0, width, 240));
                window.UpdateLayout();
                test(content);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
}
