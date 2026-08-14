using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class CoverPageRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowOwnsCanonicalPresetsAndLegacyAliases()
    {
        var inserted = new List<CoverPagePreset>();
        var bindings = new FreeWRibbonCommandBindingPorts();

        CoverPageRibbonWorkflow.Register(
            bindings,
            new CoverPageRibbonPorts(inserted.Add));

        Execute(bindings, FreeWRibbonCommandAction.CoverPage);
        inserted.Should().Equal(CoverPagePreset.Default);

        foreach (var choice in CoverPageRibbonWorkflow.Choices)
        {
            bindings.TryGet(choice.CommandId, out var canonical).Should().BeTrue();
            bindings.TryGet(choice.LegacyCommandId, out var legacy).Should().BeTrue();
            legacy.Should().BeSameAs(canonical);
            canonical!.Execute(RibbonCommandContext.Empty);
        }

        inserted.Should().Equal(
            CoverPagePreset.Default,
            CoverPagePreset.Default,
            CoverPagePreset.Banded,
            CoverPagePreset.Motion);
    }

    [Fact]
    public void WpfCommandIdsAreTheVisibleAuthority()
    {
        CoverPageRibbonWorkflow.Choices.Should().Equal(
            new CoverPageRibbonChoice(
                "freew.cover-page-default",
                "freew.cover-page.default",
                CoverPagePreset.Default),
            new CoverPageRibbonChoice(
                "freew.cover-page-banded",
                "freew.cover-page.banded",
                CoverPagePreset.Banded),
            new CoverPageRibbonChoice(
                "freew.cover-page-motion",
                "freew.cover-page.motion",
                CoverPagePreset.Motion));
    }

    [Fact]
    public void BothRenderersDelegatePresetDispatchToTheSharedWorkflow()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        wpf.Should().Contain("CoverPageRibbonWorkflow.Register(");
        avalonia.Should().Contain("CoverPageRibbonWorkflow.Register(");
        wpf.Should().NotContain("registry.Register(\"freew.cover-page-default\"");
        avalonia.Should().NotContain("r.Register(\"freew.cover-page.default\"");
    }

    private static void Execute(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action)
    {
        var route = FreeWRibbonCommandWorkflow.Routes.Single(candidate => candidate.Action == action);
        bindings.TryGet(route.CommandId, out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }
}
