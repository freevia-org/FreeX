using Free.Shared.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class MultilevelListRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowOwnsDefaultPresetsLevelsAndDefineDialog()
    {
        var applied = new List<MultilevelListDefinition>();
        var levelDeltas = new List<int>();
        var defineCalls = 0;
        var bindings = new FreeWRibbonCommandBindingPorts();

        MultilevelListRibbonWorkflow.Register(
            bindings,
            new MultilevelListRibbonPorts(
                applied.Add,
                levelDeltas.Add,
                () => defineCalls++));

        Execute(bindings, FreeWRibbonCommandAction.MultilevelList);
        applied.Should().ContainSingle().Which.Should().BeSameAs(MultilevelListDialogPlanner.DefaultDefinition);

        foreach (var preset in MultilevelListDialogPlanner.Presets)
        {
            bindings.TryGet(preset.CommandId, out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
        }
        applied.Skip(1).Should().Equal(MultilevelListDialogPlanner.Presets.Select(preset => preset.Definition));

        Execute(bindings, FreeWRibbonCommandAction.MultilevelDemote);
        Execute(bindings, FreeWRibbonCommandAction.MultilevelPromote);
        levelDeltas.Should().Equal(+1, -1);

        Execute(bindings, FreeWRibbonCommandAction.MultilevelDefine);
        defineCalls.Should().Be(1);
    }

    [Fact]
    public void MissingDefineDialogFailsClosedInsteadOfApplyingDefaults()
    {
        var applied = new List<MultilevelListDefinition>();
        var bindings = new FreeWRibbonCommandBindingPorts();

        MultilevelListRibbonWorkflow.Register(
            bindings,
            new MultilevelListRibbonPorts(applied.Add, _ => { }, OpenDefineDialog: null));

        var command = Command(bindings, FreeWRibbonCommandAction.MultilevelDefine);
        command.Should().BeSameAs(FreeWRibbonExecutionProfile.UnavailableCommand);
        ((IRibbonStatefulCommand)command).GetState().IsEnabled.Should().BeFalse();
        applied.Should().BeEmpty();
    }

    [Fact]
    public void BothRenderersDelegateTheFamilyToTheSharedWorkflow()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        wpf.Should().Contain("MultilevelListRibbonWorkflow.Register(");
        avalonia.Should().Contain("MultilevelListRibbonWorkflow.Register(");
        avalonia.Should().NotContain("callbacks.OpenMultilevelListDialog ?? (() =>");
    }

    private static void Execute(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action) =>
        Command(bindings, action).Execute(RibbonCommandContext.Empty);

    private static IRibbonCommand Command(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action)
    {
        var route = FreeWRibbonCommandWorkflow.Routes.Single(candidate => candidate.Action == action);
        bindings.TryGet(route.CommandId, out var command).Should().BeTrue();
        return command!;
    }
}
