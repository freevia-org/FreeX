using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FormattingGalleryRibbonWorkflowTests
{
    [Fact]
    public void RegistersEveryPortablePaletteAndBuiltInStyleRoute()
    {
        var registry = new RibbonCommandRegistry();
        FormattingGalleryRibbonWorkflow.Register(registry, CreatePorts([]));

        foreach (var choice in AllPaletteChoices())
            Command(registry, choice.CommandId);
        foreach (var descriptor in BuiltInStyles.Gallery)
            Command(registry, FormattingGalleryRibbonWorkflow.StyleCommandId(descriptor.Id));
    }

    [Fact]
    public void SharedMappingsPrepareThenApplyExactCatalogPayloads()
    {
        var registry = new RibbonCommandRegistry();
        var events = new List<string>();
        FormattingGalleryRibbonWorkflow.Register(registry, CreatePorts(events));

        Execute("freew.font-color.automatic");
        Execute("freew.para-shading.yellow");
        Execute("freew.char-shading.none");
        Execute("freew.char-border.red");
        Execute("freew.highlight.none");
        Execute(FormattingGalleryRibbonWorkflow.StyleCommandId("Heading1"));

        events.Should().Equal(
            "prepare", "font:",
            "prepare", "paragraph:#FFFF00",
            "prepare", "character:",
            "prepare", "border:#FF0000",
            "prepare", "highlight:",
            "prepare", "style:Heading1");

        void Execute(string id) => Command(registry, id).Execute(RibbonCommandContext.Empty);
    }

    [Fact]
    public void BothRenderersAndDefinitionsDelegateFormattingGalleryIdentityToPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = Read(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = Read(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");
        var canonical = Read(root, "freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.Ordinary.cs");
        var data = Read(root, "freew", "FreeW.Ribbon.Definitions", "FreeWRibbonDefinitionData.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("FormattingGalleryRibbonWorkflow.Register(");
            source.Should().Contain("new FormattingGalleryRibbonPorts(");
        }

        foreach (var oldMethod in new[]
                 {
                     "RegisterFontColorPalette(",
                     "RegisterParagraphShadingPalette(",
                     "RegisterCharacterShadingPalette(",
                     "RegisterCharacterBorderPalette(",
                     "RegisterHighlightPalette(",
                     "RegisterStyleGalleryCommands(",
                 })
        {
            avalonia.Should().NotContain(oldMethod);
        }

        canonical.Should().Contain("FormattingGalleryRibbonWorkflow.StyleCommandId(");
        data.Should().NotContain("string StyleCommandId(");
    }

    private static FormattingGalleryRibbonPorts CreatePorts(ICollection<string> events) =>
        new(
            PrepareExecution: () => events.Add("prepare"),
            ApplyFontColor: value => events.Add($"font:{value}"),
            ApplyParagraphShading: value => events.Add($"paragraph:{value}"),
            ApplyCharacterShading: value => events.Add($"character:{value}"),
            ApplyCharacterBorderColor: value => events.Add($"border:{value}"),
            ApplyHighlightColor: value => events.Add($"highlight:{value}"),
            ApplyNamedStyle: value => events.Add($"style:{value}"));

    private static IEnumerable<FreeWRibbonPaletteChoice> AllPaletteChoices() =>
        FreeWRibbonPaletteCatalog.FontColors
            .Concat(FreeWRibbonPaletteCatalog.ParagraphShading)
            .Concat(FreeWRibbonPaletteCatalog.CharacterShading)
            .Concat(FreeWRibbonPaletteCatalog.CharacterBorders)
            .Concat(FreeWRibbonPaletteCatalog.Highlights);

    private static IRibbonCommand Command(IRibbonCommandRegistry registry, string id)
    {
        registry.TryGet(id, out var command).Should().BeTrue($"{id} should be registered");
        return command!;
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));
}
