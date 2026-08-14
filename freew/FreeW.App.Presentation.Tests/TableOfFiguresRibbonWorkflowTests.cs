using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class TableOfFiguresRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowOwnsInsertRefreshLabelsAndPrimaryIdentity()
    {
        var events = new List<string>();
        var prepared = 0;
        var bindings = new FreeWRibbonCommandBindingPorts();

        TableOfFiguresRibbonWorkflow.Register(
            bindings,
            new TableOfFiguresRibbonPorts(
                label => events.Add($"insert:{label}"),
                label => events.Add($"refresh:{label}"),
                () => prepared++));

        Command(bindings, FreeWRibbonCommandAction.Tof)
            .Should().BeSameAs(Command(bindings, FreeWRibbonCommandAction.Tof_Figure));
        Command(bindings, FreeWRibbonCommandAction.TofRefresh)
            .Should().BeSameAs(Command(bindings, FreeWRibbonCommandAction.TofRefresh_Figure));

        var actions = new[]
        {
            FreeWRibbonCommandAction.Tof,
            FreeWRibbonCommandAction.Tof_Figure,
            FreeWRibbonCommandAction.Tof_Table,
            FreeWRibbonCommandAction.Tof_Equation,
            FreeWRibbonCommandAction.TofRefresh,
            FreeWRibbonCommandAction.TofRefresh_Figure,
            FreeWRibbonCommandAction.TofRefresh_Table,
            FreeWRibbonCommandAction.TofRefresh_Equation,
        };
        foreach (var action in actions)
            Command(bindings, action).Execute(RibbonCommandContext.Empty);

        prepared.Should().Be(actions.Length);
        events.Should().Equal(
            "insert:Figure", "insert:Figure", "insert:Table", "insert:Equation",
            "refresh:Figure", "refresh:Figure", "refresh:Table", "refresh:Equation");
    }

    [Fact]
    public void EditorFamilyBuilderReceivesTheSameSharedCommands()
    {
        var family = new FreeWRibbonEditorCommandFamilyBuilder();
        TableOfFiguresRibbonWorkflow.Register(
            family,
            new TableOfFiguresRibbonPorts(_ => { }, _ => { }));

        var commands = family.Build().Commands;
        commands.Should().ContainKeys(
            FreeWRibbonCommandAction.Tof,
            FreeWRibbonCommandAction.Tof_Figure,
            FreeWRibbonCommandAction.Tof_Table,
            FreeWRibbonCommandAction.Tof_Equation,
            FreeWRibbonCommandAction.TofRefresh,
            FreeWRibbonCommandAction.TofRefresh_Figure,
            FreeWRibbonCommandAction.TofRefresh_Table,
            FreeWRibbonCommandAction.TofRefresh_Equation);
        commands[FreeWRibbonCommandAction.Tof].Should().BeSameAs(commands[FreeWRibbonCommandAction.Tof_Figure]);
        commands[FreeWRibbonCommandAction.TofRefresh].Should().BeSameAs(commands[FreeWRibbonCommandAction.TofRefresh_Figure]);
    }

    [Fact]
    public void BothRenderersDelegateTableOfFiguresPolicyToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("TableOfFiguresRibbonWorkflow.Register(");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Tof,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Tof_Figure,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.TofRefresh,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.TofRefresh_Figure,");
        }
    }

    private static IRibbonCommand Command(FreeWRibbonCommandBindingPorts bindings, FreeWRibbonCommandAction action)
    {
        var route = FreeWRibbonCommandWorkflow.Routes.Single(candidate => candidate.Action == action);
        bindings.TryGet(route.CommandId, out var command).Should().BeTrue(action.ToString());
        return command!;
    }
}
