using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class DesignRibbonWorkflowTests
{
    [Fact]
    public void RegistersEveryCatalogRouteAndReturnsCanonicalStatefulCommands()
    {
        var registry = new RibbonCommandRegistry();
        var events = new List<string>();

        var result = DesignRibbonWorkflow.Register(registry, CreateBindings(events));

        Command(registry, "freew.theme").Should().BeSameAs(result.StatefulCommands[0].Command);
        Command(registry, "freew.style-set").Should().BeSameAs(result.StatefulCommands[1].Command);
        result.StatefulCommands.Select(entry => entry.Id).Should().Equal(
            "freew.theme",
            "freew.style-set");

        foreach (var theme in DocumentTheme.Catalog)
        {
            Command(registry, $"freew.theme.{theme.Name.ToLowerInvariant()}")
                .Should().BeAssignableTo<IRibbonPreviewCommand>();
            Command(registry, $"freew.theme-colors.{theme.Name.ToLowerInvariant()}")
                .Should().BeAssignableTo<IRibbonPreviewCommand>();
        }
        foreach (var fontSet in DocumentFontSet.Catalog)
            Command(registry, $"freew.theme-fonts.{fontSet.Name.ToLowerInvariant()}")
                .Should().BeAssignableTo<IRibbonPreviewCommand>();
        foreach (var styleSet in DocumentStyleSet.Catalog)
            Command(registry, DesignRibbonWorkflow.StyleSetCommandId(styleSet.Name))
                .Should().BeAssignableTo<IRibbonPreviewCommand>();
        foreach (var spacing in DocumentParagraphSpacingSet.Catalog)
            Command(registry, DesignRibbonWorkflow.ParagraphSpacingCommandId(spacing.Name))
                .Should().BeAssignableTo<IRibbonPreviewCommand>();
        for (var index = 0; index < DocumentEffectSet.Catalog.Count; index++)
            Command(registry, $"freew.context.effects.{index}")
                .Should().BeAssignableTo<IRibbonPreviewCommand>();
        foreach (var color in FreeWRibbonPaletteCatalog.PageColors)
            Command(registry, color.CommandId);

        Command(registry, "freew.paragraph-spacing")
            .Should().BeSameAs(Command(registry, "freew.para-spacing"));
        Command(registry, "freew.page-border")
            .Should().BeSameAs(Command(registry, "freew.page-borders"));
        foreach (var id in new[]
                 {
                     "freew.watermark.confidential",
                     "freew.watermark.do-not-copy",
                     "freew.watermark.draft",
                     "freew.watermark.urgent",
                     "freew.watermark.custom",
                     "freew.watermark.none",
                 })
        {
            Command(registry, id);
        }
    }

    [Fact]
    public void SharedWorkflowOwnsChoiceResolutionPresetMappingAndPreparationOrder()
    {
        var registry = new RibbonCommandRegistry();
        var events = new List<string>();
        DesignRibbonWorkflow.Register(registry, CreateBindings(events));

        var theme = DocumentTheme.Catalog[^1];
        Command(registry, "freew.theme").Execute(RibbonCommandContext.ForSelectedValue(theme.Name));
        Command(registry, $"freew.theme-colors.{theme.Name.ToLowerInvariant()}")
            .Execute(RibbonCommandContext.Empty);
        var fontSet = DocumentFontSet.Catalog[^1];
        Command(registry, "freew.theme-fonts").Execute(RibbonCommandContext.ForSelectedValue(fontSet.Name));
        var styleSet = DocumentStyleSet.Catalog[^1];
        Command(registry, DesignRibbonWorkflow.StyleSetCommandId(styleSet.Name))
            .Execute(RibbonCommandContext.Empty);
        var spacing = DocumentParagraphSpacingSet.Catalog[^1];
        Command(registry, DesignRibbonWorkflow.ParagraphSpacingCommandId(spacing.Name))
            .Execute(RibbonCommandContext.Empty);
        var effect = DocumentEffectSet.Catalog[^1];
        Command(registry, "freew.theme-effects").Execute(RibbonCommandContext.ForSelectedValue(effect.Name));
        var pageColor = FreeWRibbonPaletteCatalog.PageColors[^1];
        Command(registry, pageColor.CommandId).Execute(RibbonCommandContext.Empty);
        Command(registry, "freew.watermark.draft").Execute(RibbonCommandContext.Empty);
        Command(registry, "freew.watermark.none").Execute(RibbonCommandContext.Empty);
        Command(registry, "freew.theme-fonts").Execute(RibbonCommandContext.ForSelectedValue("missing-font-set"));

        events.Should().Equal(
            "prepare", $"theme:{theme.Name}",
            "cancel-preview", "prepare", $"colors:{theme.Name}",
            "prepare", $"fonts:{fontSet.Name}",
            "cancel-preview", "prepare", $"style-set:{styleSet.Name}",
            "cancel-preview", "prepare", $"spacing:{spacing.Name}",
            "prepare", $"effects:{effect.Name}",
            "prepare", $"page-color:{pageColor.Hex}",
            "prepare", "watermark:DRAFT",
            "prepare", "watermark:");
    }

    [Fact]
    public void PresetCommandsPreviewCancelAndCommitInSharedOrder()
    {
        var registry = new RibbonCommandRegistry();
        var events = new List<string>();
        DesignRibbonWorkflow.Register(registry, CreateBindings(events));
        var theme = DocumentTheme.Catalog[^1];
        var command = Command(registry, $"freew.theme.{theme.Name.ToLowerInvariant()}")
            .Should().BeAssignableTo<IRibbonPreviewCommand>().Subject;

        command.BeginPreview(RibbonCommandContext.Empty);
        command.CancelPreview();
        command.BeginPreview(RibbonCommandContext.Empty);
        command.Execute(RibbonCommandContext.Empty);

        events.Should().Equal(
            $"preview-theme:{theme.Name}",
            "cancel-preview",
            $"preview-theme:{theme.Name}",
            "cancel-preview",
            "prepare",
            $"theme:{theme.Name}");
    }

    [Fact]
    public void StyleSetPresetPreviewsFromSharedBaselineAndCommitsThroughFormattingSession()
    {
        var registry = new RibbonCommandRegistry();
        var events = new List<string>();
        DesignRibbonWorkflow.Register(registry, CreateBindings(events));
        var styleSet = DocumentStyleSet.Catalog[^1];
        var command = Command(registry, DesignRibbonWorkflow.StyleSetCommandId(styleSet.Name))
            .Should().BeAssignableTo<IRibbonPreviewCommand>().Subject;

        command.BeginPreview(RibbonCommandContext.Empty);
        command.CancelPreview();
        command.BeginPreview(RibbonCommandContext.Empty);
        command.Execute(RibbonCommandContext.Empty);

        events.Should().Equal(
            $"preview-style-set:{styleSet.Name}",
            "cancel-preview",
            $"preview-style-set:{styleSet.Name}",
            "cancel-preview",
            "prepare",
            $"style-set:{styleSet.Name}");
    }

    [Fact]
    public void BothRenderersDelegateDesignIdentityToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = Read(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = Read(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");
        var avaloniaRenderer = Read(
            root,
            "shared",
            "Free.Shared.Ribbon.Avalonia",
            "AvaloniaRibbonRenderer.cs");
        var wpfEditor = Read(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaEditor = Read(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var canonicalTabs = Read(root, "freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.cs");
        var coordinator = Read(
            root,
            "freew",
            "FreeW.App.Presentation",
            "Editing",
            "DocumentDesignEditingCoordinator.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("DesignRibbonWorkflow.Register(");
            (source.Contains("new DesignRibbonBindings(", StringComparison.Ordinal)
             || source.Contains("CreateDesignRibbonBindings(", StringComparison.Ordinal))
                .Should().BeTrue();
            source.Should().NotContain("freew.watermark.confidential");
            source.Should().NotContain("freew.theme-colors.{");
            source.Should().NotContain("freew.theme-fonts.{");
            source.Should().NotContain("freew.para-spacing.{");
            source.Should().NotContain("RegisterDesignCommands(");
        }

        wpf.Should().NotContain("class ApplyThemeColorsCommand")
            .And.NotContain("class ApplyFontSetCommand")
            .And.NotContain("class ApplyParagraphSpacingSetCommand")
            .And.NotContain("class ApplyEffectSetCommand");
        avalonia.Should().NotContain("new ThemeCommand(formatting)")
            .And.NotContain("new StyleSetCommand(formatting)")
            .And.NotContain("RegisterPageColorPalette(")
            .And.Contain("PreviewTheme: editor.PreviewTheme")
            .And.Contain("PreviewStyleSet: editor.PreviewStyleSet")
            .And.Contain("CancelPreview: editor.CancelDesignPreview");
        wpf.Should().Contain("PreviewTheme: editor.PreviewTheme")
            .And.Contain("PreviewStyleSet: editor.PreviewStyleSet")
            .And.Contain("CancelPreview: editor.EndThemePreview");
        canonicalTabs.Should().Contain("BuildAvaloniaStyleSetsMenu()")
            .And.Contain("DesignRibbonWorkflow.StyleSetCommandId(styleSet.Name)");
        System.Text.RegularExpressions.Regex.Matches(
                canonicalTabs,
                "group\\.ComboBox\\(\"freew\\.style-set\"")
            .Should().ContainSingle("only the WPF authority keeps its native Style Set gallery combo");
        avaloniaRenderer.Should().Contain("IRibbonPreviewCommand")
            .And.Contain("PointerEntered +=")
            .And.Contain("GotFocus +=")
            .And.Contain("CancelMenuPreviews(");
        coordinator.Should().Contain("private DocumentDesignPreviewSnapshot? _previewSnapshot;")
            .And.Contain("public bool CancelPreview()")
            .And.Contain("RestorePreviewSnapshot(");
        foreach (var editor in new[] { wpfEditor, avaloniaEditor })
        {
            editor.Should().Contain("DesignEdits.PreviewTheme(")
                .And.Contain("DesignEdits.PreviewThemeColors(")
                .And.Contain("DesignEdits.PreviewStyleSet(")
                .And.Contain("DesignEdits.PreviewFontSet(")
                .And.Contain("DesignEdits.PreviewParagraphSpacingSet(")
                .And.Contain("DesignEdits.PreviewEffectSet(")
                .And.NotContain("_themeSnapshot")
                .And.NotContain("_fontSetSnapshot")
                .And.NotContain("_effectSetSnapshot");
        }
    }

    private static DesignRibbonBindings CreateBindings(ICollection<string> events)
    {
        var document = new TextDocument();
        var formatting = new FreeWRibbonFormattingSession(new FreeWRibbonFormattingPorts(
            GetCurrentParagraph: () => ParagraphFormatting.Default,
            ApplyIndentLeft: _ => { },
            ApplyIndentRight: _ => { },
            ApplySpaceBefore: _ => { },
            ApplySpaceAfter: _ => { },
            GetDocument: () => document,
            GetCurrentParagraphStyleId: () => null,
            ApplyParagraphStyle: _ => { },
            ApplyTheme: value => events.Add($"theme:{value.Name}"),
            ApplyStyleSet: value => events.Add($"style-set:{value.Name}")));

        IRibbonCommand Record(string name) => new RecordingCommand(events, name);
        return new DesignRibbonBindings(
            Formatting: formatting,
            PrepareExecution: () => events.Add("prepare"),
            ResolveChoice: context => context.SelectedValue,
            ApplyThemeColors: value => events.Add($"colors:{value.Name}"),
            ApplyFontSet: value => events.Add($"fonts:{value.Name}"),
            ApplyParagraphSpacingSet: value => events.Add($"spacing:{value.Name}"),
            ApplyEffectSet: value => events.Add($"effects:{value.Name}"),
            PreviewTheme: value => events.Add($"preview-theme:{value.Name}"),
            PreviewThemeColors: value => events.Add($"preview-colors:{value.Name}"),
            PreviewStyleSet: value => events.Add($"preview-style-set:{value.Name}"),
            PreviewFontSet: value => events.Add($"preview-fonts:{value.Name}"),
            PreviewParagraphSpacingSet: value => events.Add($"preview-spacing:{value.Name}"),
            PreviewEffectSet: value => events.Add($"preview-effects:{value.Name}"),
            CancelPreview: () => events.Add("cancel-preview"),
            ApplyDefaultStyleSet: () => events.Add("style-set:default"),
            ApplyPageColor: value => events.Add($"page-color:{value}"),
            ApplyWatermarkText: value => events.Add($"watermark:{value}"),
            CustomizeColors: Record("custom-colors"),
            CustomizeFonts: Record("custom-fonts"),
            CustomParagraphSpacing: Record("custom-spacing"),
            PageColor: Record("page-color-dialog"),
            MorePageColors: Record("more-page-colors"),
            PageBorders: Record("page-borders"),
            Watermark: Record("watermark-dialog"),
            CustomWatermark: Record("custom-watermark"));
    }

    private static IRibbonCommand Command(IRibbonCommandRegistry registry, string id)
    {
        registry.TryGet(id, out var command).Should().BeTrue($"{id} should be registered");
        return command!;
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));

    private sealed class RecordingCommand(ICollection<string> events, string name) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => events.Add(name);
    }
}
