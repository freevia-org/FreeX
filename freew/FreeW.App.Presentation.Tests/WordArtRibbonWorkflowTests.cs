using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class WordArtRibbonWorkflowTests
{
    [Fact]
    public void SharedCatalogOwnsEveryStyleAndEveryWarpCommandIdentity()
    {
        WordArtRibbonWorkflow.StylePresets.Select(preset => preset.Value)
            .Should().BeEquivalentTo(Enum.GetValues<WordArtStyle>());
        WordArtRibbonWorkflow.StylePresets.Select(preset => preset.CommandId)
            .Should().OnlyHaveUniqueItems();

        WordArtRibbonWorkflow.WarpPresets.Select(preset => preset.Value)
            .Should().BeEquivalentTo(Enum.GetValues<WordArtWarp>()
                .Except([WordArtWarp.Button, WordArtWarp.InflateBottom]));
        Enum.GetValues<WordArtWarp>()
            .Select(WordArtRibbonWorkflow.WarpCommandId)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void RegistersEveryStyleAndWarpAsASelectionAwareStatefulCommand()
    {
        var registry = new RibbonCommandRegistry();
        var hasSelection = false;
        var events = new List<string>();
        WordArtRibbonWorkflow.Register(
            registry,
            new WordArtRibbonPorts(
                HasSelection: () => hasSelection,
                ApplyStyle: style => events.Add($"style:{style}"),
                ApplyWarp: warp => events.Add($"warp:{warp}"),
                PrepareExecution: () => events.Add("prepare")));

        foreach (var style in Enum.GetValues<WordArtStyle>())
            Stateful(registry, WordArtRibbonWorkflow.StyleCommandId(style)).GetState()
                .Should().Be(new RibbonCommandState(IsEnabled: false));
        foreach (var warp in Enum.GetValues<WordArtWarp>())
            Stateful(registry, WordArtRibbonWorkflow.WarpCommandId(warp)).GetState()
                .Should().Be(new RibbonCommandState(IsEnabled: false));
        Stateful(registry, WordArtRibbonWorkflow.StyleMenuCommandId).GetState()
            .Should().Be(new RibbonCommandState(IsEnabled: false));
        Stateful(registry, WordArtRibbonWorkflow.WarpMenuCommandId).GetState()
            .Should().Be(new RibbonCommandState(IsEnabled: false));

        Stateful(registry, WordArtRibbonWorkflow.StyleCommandId(WordArtStyle.Bevel))
            .Execute(RibbonCommandContext.Empty);
        events.Should().BeEmpty("disabled commands must fail closed");

        hasSelection = true;
        Stateful(registry, WordArtRibbonWorkflow.StyleMenuCommandId).GetState().IsEnabled.Should().BeTrue();
        Stateful(registry, WordArtRibbonWorkflow.WarpMenuCommandId).GetState().IsEnabled.Should().BeTrue();
        Stateful(registry, WordArtRibbonWorkflow.StyleCommandId(WordArtStyle.Bevel))
            .Execute(RibbonCommandContext.Empty);
        Stateful(registry, WordArtRibbonWorkflow.WarpCommandId(WordArtWarp.Circle))
            .Execute(RibbonCommandContext.Empty);

        events.Should().Equal("prepare", "style:Bevel", "prepare", "warp:Circle");
    }

    [Fact]
    public void BothRenderersDelegateWordArtIdentityAndMutationToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = Read(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = Read(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "FreeWAvaloniaRibbonCommands.cs");
        var editor = Read(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var context = Read(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "FloatingRibbonContextSource.cs");

        foreach (var registry in new[] { wpf, avalonia })
            registry.Should().Contain("WordArtRibbonWorkflow.Register(");

        wpf.Should().NotContain("static string WordArtStyleId")
            .And.NotContain("static string WarpId")
            .And.NotContain("WordArt_Style_Choose_Message")
            .And.NotContain("WordArt_Transform_Choose_Message");
        editor.Should().Contain("TryHitTestInlineDrawingObject(")
            .And.Contain("_selectedInlineObject")
            .And.Contain("SelectedDrawingObjectInfo")
            .And.Contain("ObjectEdits.SetWordArtStyle(")
            .And.Contain("ObjectEdits.SetWordArtWarp(");
        context.Should().Contain("_editor.SelectedDrawingObjectInfo")
            .And.NotContain("_editor.SelectedFloatingInfo");
    }

    private static IRibbonStatefulCommand Stateful(
        IRibbonCommandRegistry registry,
        RibbonCommandId commandId)
    {
        registry.TryGet(commandId, out var command).Should().BeTrue(commandId.Value);
        return command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));
}
