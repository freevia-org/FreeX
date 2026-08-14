using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class DropCapRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowOwnsPrimaryCanonicalAndLegacyCommandIdentity()
    {
        var events = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();
        var ports = new DropCapRibbonPorts(
            new RecordingCommand(events, "dropped"),
            new RecordingCommand(events, "in-margin"),
            new RecordingCommand(events, "none"),
            new RecordingCommand(events, "options"));

        DropCapRibbonWorkflow.Register(bindings, ports);

        DropCapRibbonWorkflow.Actions.Should().OnlyHaveUniqueItems().And.HaveCount(8);
        Command(bindings, FreeWRibbonCommandAction.DropCap).Should().BeSameAs(ports.Dropped);
        Command(bindings, FreeWRibbonCommandAction.DropCap_Dropped).Should().BeSameAs(ports.Dropped);
        Command(bindings, FreeWRibbonCommandAction.DropCapDropped).Should().BeSameAs(ports.Dropped);
        Command(bindings, FreeWRibbonCommandAction.DropCap_InMargin).Should().BeSameAs(ports.InMargin);
        Command(bindings, FreeWRibbonCommandAction.DropCapInMargin).Should().BeSameAs(ports.InMargin);
        Command(bindings, FreeWRibbonCommandAction.DropCap_None).Should().BeSameAs(ports.None);
        Command(bindings, FreeWRibbonCommandAction.DropCapNone).Should().BeSameAs(ports.None);
        Command(bindings, FreeWRibbonCommandAction.DropCapOptions).Should().BeSameAs(ports.Options);

        foreach (var action in DropCapRibbonWorkflow.Actions)
            Command(bindings, action).Execute(RibbonCommandContext.Empty);

        events.Should().Equal(
            "dropped", "dropped", "in-margin", "none",
            "dropped", "in-margin", "none", "options");
    }

    [Fact]
    public void BothRenderersDelegateDropCapPolicyToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("DropCapRibbonWorkflow.Register(");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.DropCapDropped");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.DropCapInMargin");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.DropCapNone");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.DropCapOptions");
        }
    }

    private static IRibbonCommand Command(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action)
    {
        var route = FreeWRibbonCommandWorkflow.Routes.Single(candidate => candidate.Action == action);
        bindings.TryGet(route.CommandId, out var command).Should().BeTrue(action.ToString());
        return command!;
    }

    private sealed class RecordingCommand(ICollection<string> events, string value) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => events.Add(value);
    }
}
