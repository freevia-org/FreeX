using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class ParagraphEditingRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowRegistersEveryOwnedActionAndIndentAliases()
    {
        var events = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();
        ParagraphEditingRibbonWorkflow.Register(bindings, CreatePorts(events));

        ParagraphEditingRibbonWorkflow.Actions.Should().OnlyHaveUniqueItems().And.HaveCount(15);
        foreach (var action in ParagraphEditingRibbonWorkflow.Actions)
        {
            var route = FreeWRibbonCommandWorkflow.Routes.First(candidate => candidate.Action == action);
            bindings.TryGet(route.CommandId, out var command).Should().BeTrue(action.ToString());
            command.Should().NotBeNull();
        }

        bindings.TryGet("freew.indent-increase", out var canonicalIncrease).Should().BeTrue();
        bindings.TryGet("freew.increase-indent", out var increaseAlias).Should().BeTrue();
        increaseAlias.Should().BeSameAs(canonicalIncrease);
        bindings.TryGet("freew.indent-decrease", out var canonicalDecrease).Should().BeTrue();
        bindings.TryGet("freew.decrease-indent", out var decreaseAlias).Should().BeTrue();
        decreaseAlias.Should().BeSameAs(canonicalDecrease);
    }

    [Fact]
    public void NativeCommandsPreserveStateAndAllExecutionsPrepareFirst()
    {
        var events = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();
        ParagraphEditingRibbonWorkflow.Register(bindings, CreatePorts(events));

        bindings.TryGet("freew.bullets", out var bullets).Should().BeTrue();
        bullets.Should().BeAssignableTo<IRibbonStatefulCommand>();
        ((IRibbonStatefulCommand)bullets!).GetState().Should().Be(
            new RibbonCommandState(IsEnabled: false, IsChecked: true, Value: "native"));

        bullets.Execute(RibbonCommandContext.Empty);
        Execute(bindings, "freew.indent-increase");
        Execute(bindings, "freew.space-before-toggle");
        Execute(bindings, "freew.sort");

        events.Should().Equal(
            "prepare", "bullets",
            "prepare", "indent-increase",
            "prepare", "space-before",
            "prepare", "sort");
    }

    [Fact]
    public void BothRenderersDelegateParagraphPolicyToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ParagraphEditingRibbonWorkflow.Register(");
            source.Should().Contain("CreateParagraphEditingPorts(");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Bullets");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.AlignLeft");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.IndentIncrease");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.SpaceBeforeToggle");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.KeepWithNext");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.ParaBorder");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Sort");
        }
    }

    private static void Execute(FreeWRibbonCommandBindingPorts bindings, string commandId)
    {
        bindings.TryGet(commandId, out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static ParagraphEditingRibbonPorts CreatePorts(ICollection<string> events) =>
        new(
            PrepareExecution: () => events.Add("prepare"),
            ToggleBullets: new RecordingStatefulCommand(events, "bullets"),
            ToggleNumbering: new RecordingCommand(events, "numbering"),
            AlignLeft: new RecordingCommand(events, "align-left"),
            AlignCenter: new RecordingCommand(events, "align-center"),
            AlignRight: new RecordingCommand(events, "align-right"),
            AlignJustify: new RecordingCommand(events, "align-justify"),
            IncreaseIndent: () => events.Add("indent-increase"),
            DecreaseIndent: () => events.Add("indent-decrease"),
            ToggleSpaceBefore: () => events.Add("space-before"),
            ToggleSpaceAfter: () => events.Add("space-after"),
            ToggleKeepWithNext: () => events.Add("keep-next"),
            ToggleKeepLinesTogether: () => events.Add("keep-lines"),
            ToggleWidowControl: () => events.Add("widow-control"),
            ToggleParagraphBorder: () => events.Add("paragraph-border"),
            Sort: new RecordingCommand(events, "sort"));

    private class RecordingCommand(ICollection<string> events, string name) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => events.Add(name);
    }

    private sealed class RecordingStatefulCommand(ICollection<string> events, string name)
        : RecordingCommand(events, name), IRibbonStatefulCommand
    {
        public RibbonCommandState GetState() =>
            new(IsEnabled: false, IsChecked: true, Value: "native");
    }
}
