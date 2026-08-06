using System.IO;
using System.Text.RegularExpressions;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWRibbonCommandWorkflowTests
{
    [Fact]
    public void Routes_are_unique_and_cover_the_shared_renderer_groups()
    {
        var routes = FreeWRibbonCommandWorkflow.Routes;

        routes.Should().HaveCount(399);
        routes.Select(route => route.CommandId).Should().OnlyHaveUniqueItems();
        routes.Select(route => route.Action).Should().OnlyHaveUniqueItems();
        routes.Select(route => route.Action)
            .Should().BeEquivalentTo(Enum.GetValues<FreeWRibbonCommandAction>());

        routes.Should().Contain(new FreeWRibbonCommandRoute(
            "freew.bold",
            FreeWRibbonCommandAction.Bold));
        routes.Should().Contain(new FreeWRibbonCommandRoute(
            "freew.table-properties",
            FreeWRibbonCommandAction.TableProperties));
        routes.Should().Contain(new FreeWRibbonCommandRoute(
            "freew.image-brightness-plus20",
            FreeWRibbonCommandAction.ImageBrightnessPlus20));
        routes.Should().Contain(new FreeWRibbonCommandRoute(
            "freew.merge-finish",
            FreeWRibbonCommandAction.MergeFinish));
        routes.Should().Contain(new FreeWRibbonCommandRoute(
            "freew.smartart-add-shape",
            FreeWRibbonCommandAction.SmartartAddShape));
    }

    [Fact]
    public void Register_routes_a_native_command_without_renderer_owned_ids()
    {
        var registry = new RibbonCommandRegistry();
        var command = new RecordingCommand();

        FreeWRibbonCommandWorkflow.Register(
            registry,
            FreeWRibbonCommandAction.TableProperties,
            command);

        registry.TryGet("freew.table-properties", out var resolved).Should().BeTrue();
        resolved.Should().BeSameAs(command);
        FreeWRibbonCommandWorkflow.GetPrimaryCommandId(FreeWRibbonCommandAction.TableProperties)
            .Should().Be(new RibbonCommandId("freew.table-properties"));
    }

    [Fact]
    public void Shared_bindings_own_enabled_checked_value_and_prepare_policies()
    {
        var registry = new RibbonCommandRegistry();
        var enabled = false;
        var checkedState = false;
        var actionCount = 0;
        var prepareCount = 0;
        string? selectedValue = null;

        var action = FreeWRibbonCommandWorkflow.RegisterAction(
            registry,
            FreeWRibbonCommandAction.Find,
            () => actionCount++,
            () => enabled,
            () => prepareCount++);
        var toggle = FreeWRibbonCommandWorkflow.RegisterToggle(
            registry,
            FreeWRibbonCommandAction.Bold,
            () => checkedState = !checkedState,
            () => checkedState,
            prepareExecution: () => prepareCount++);
        var value = FreeWRibbonCommandWorkflow.RegisterValue(
            registry,
            FreeWRibbonCommandAction.FontFamily,
            selected => selectedValue = selected,
            getValue: () => "Aptos",
            prepareExecution: () => prepareCount++);

        action.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsEnabled.Should().BeFalse();
        action.Execute(RibbonCommandContext.Empty);
        actionCount.Should().Be(0);
        prepareCount.Should().Be(0);

        enabled = true;
        action.Execute(RibbonCommandContext.Empty);
        toggle.Execute(RibbonCommandContext.Empty);
        value.Execute(RibbonCommandContext.ForSelectedValue("Consolas"));

        actionCount.Should().Be(1);
        checkedState.Should().BeTrue();
        toggle.GetState().IsChecked.Should().BeTrue();
        value.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().Value.Should().Be("Aptos");
        selectedValue.Should().Be("Consolas");
        prepareCount.Should().Be(3);
    }

    [Fact]
    public void Renderer_sources_cannot_reintroduce_catalog_owned_registration_literals()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = ReadSource(
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "FreeWAvaloniaRibbonCommands.cs");
        var directRegistration = new Regex(
            @"(?:registry|r)\.Register\(\s*""(?<id>freew\.[^""]+)""",
            RegexOptions.CultureInvariant);

        var copiedIds = new[] { wpf, avalonia }
            .SelectMany(source => directRegistration.Matches(source)
                .Select(match => match.Groups["id"].Value))
            .Intersect(
                FreeWRibbonCommandWorkflow.Routes.Select(route => route.CommandId),
                StringComparer.Ordinal)
            .ToArray();

        copiedIds.Should().BeEmpty();
        wpf.Should().Contain("FreeWRibbonCommandWorkflow.Register(");
        avalonia.Should().Contain("FreeWRibbonCommandWorkflow.Register(");

        foreach (var helperPrefix in new[]
                 {
                     "Routed(\"freew.",
                     "Toggle(\"freew.",
                     "PageSetting(\"freew.",
                     "RegisterImageMutation(r, editor, \"freew.",
                     "RegisterSmartArtStructureCommand(r, editor, \"freew.",
                     "RegisterMailingsAlias(r, \"freew.",
                 })
        {
            wpf.Should().NotContain(helperPrefix);
            avalonia.Should().NotContain(helperPrefix);
        }
    }

    private static string ReadSource(params string[] relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(relativePath.Aggregate(root, Path.Combine));
    }

    private sealed class RecordingCommand : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
        }
    }
}
