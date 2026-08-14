using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class InsertMediaRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowRegistersEveryNativeAdapterWithoutWrappingIt()
    {
        var events = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();
        var ports = new InsertMediaRibbonPorts(
            new RecordingCommand(events, "chart"),
            new RecordingCommand(events, "smartart"),
            new RecordingCommand(events, "icon"),
            new RecordingCommand(events, "wordart"),
            new RecordingCommand(events, "object"));

        InsertMediaRibbonWorkflow.Register(bindings, ports);

        InsertMediaRibbonWorkflow.Actions.Should().OnlyHaveUniqueItems().And.HaveCount(5);
        Command(bindings, FreeWRibbonCommandAction.Chart).Should().BeSameAs(ports.Chart);
        Command(bindings, FreeWRibbonCommandAction.Smartart).Should().BeSameAs(ports.SmartArt);
        Command(bindings, FreeWRibbonCommandAction.InsertIcon).Should().BeSameAs(ports.Icon);
        Command(bindings, FreeWRibbonCommandAction.Wordart).Should().BeSameAs(ports.WordArt);
        Command(bindings, FreeWRibbonCommandAction.Object).Should().BeSameAs(ports.EmbeddedObject);

        foreach (var action in InsertMediaRibbonWorkflow.Actions)
            Command(bindings, action).Execute(RibbonCommandContext.Empty);

        events.Should().Equal("chart", "smartart", "icon", "wordart", "object");
    }

    [Fact]
    public void BothRenderersDelegateInsertMediaPolicyToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("InsertMediaRibbonWorkflow.Register(");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Chart");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Smartart");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.InsertIcon");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Wordart");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Object");
        }
    }

    private static IRibbonCommand Command(FreeWRibbonCommandBindingPorts bindings, FreeWRibbonCommandAction action)
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
