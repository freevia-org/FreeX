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
        var style = Command(registry, FormattingGalleryRibbonWorkflow.StyleCommandId("Heading1"));
        style.Should().BeAssignableTo<IRibbonPreviewCommand>();
        var preview = (IRibbonPreviewCommand)style;
        preview.BeginPreview(RibbonCommandContext.Empty);
        preview.CancelPreview();
        style.Execute(RibbonCommandContext.Empty);
        Execute(FormattingGalleryRibbonWorkflow.StyleCommandId("Strong"));

        events.Should().Equal(
            "prepare", "font:",
            "prepare", "paragraph:#FFFF00",
            "prepare", "character:",
            "prepare", "border:#FF0000",
            "prepare", "highlight:",
            "preview-style:Heading1", "cancel-style",
            "commit-style:Heading1", "prepare",
            "prepare", "style:Strong");

        void Execute(string id) => Command(registry, id).Execute(RibbonCommandContext.Empty);
    }

    [Fact]
    public void OnlyParagraphStyleRoutesExposeLivePreview()
    {
        var registry = new RibbonCommandRegistry();
        FormattingGalleryRibbonWorkflow.Register(registry, CreatePorts([]));

        foreach (var descriptor in BuiltInStyles.Gallery)
        {
            var command = Command(registry, FormattingGalleryRibbonWorkflow.StyleCommandId(descriptor.Id));
            if (descriptor.Type == StyleType.Paragraph)
                command.Should().BeAssignableTo<IRibbonPreviewCommand>();
            else
                command.Should().NotBeAssignableTo<IRibbonPreviewCommand>();
        }
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
            source.Should().Contain("PreviewNamedStyle:");
            source.Should().Contain("CancelNamedStylePreview:");
            source.Should().Contain("CommitNamedStylePreview:");
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
            ApplyNamedStyle: value => events.Add($"style:{value}"),
            PreviewNamedStyle: value => events.Add($"preview-style:{value}"),
            CancelNamedStylePreview: () => events.Add("cancel-style"),
            CommitNamedStylePreview: value => events.Add($"commit-style:{value}"));

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
