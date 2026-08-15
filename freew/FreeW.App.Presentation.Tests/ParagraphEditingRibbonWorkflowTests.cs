using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

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
    public void ListTogglesPublishLiveStateAndAllExecutionsPrepareFirst()
    {
        var events = new List<string>();
        var currentListKind = ListKind.Bullet;
        var bindings = new FreeWRibbonCommandBindingPorts();
        var commands = ParagraphEditingRibbonWorkflow.Register(
            bindings,
            CreatePorts(events, () => currentListKind));

        bindings.TryGet("freew.bullets", out var bullets).Should().BeTrue();
        bullets.Should().BeAssignableTo<IRibbonStatefulCommand>();
        ((IRibbonStatefulCommand)bullets!).GetState().Should().Be(
            new RibbonCommandState(IsChecked: true));

        bindings.TryGet("freew.numbering", out var numbering).Should().BeTrue();
        numbering.Should().BeAssignableTo<IRibbonStatefulCommand>();
        ((IRibbonStatefulCommand)numbering!).GetState().Should().Be(
            new RibbonCommandState(IsChecked: false));

        currentListKind = ListKind.Number;
        ((IRibbonStatefulCommand)bullets).GetState().IsChecked.Should().BeFalse();
        ((IRibbonStatefulCommand)numbering).GetState().IsChecked.Should().BeTrue();
        commands.StatefulCommands.Select(command => command.Id.Value).Should().Equal(
            "freew.bullets",
            "freew.numbering");

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
            source.Should().Contain("CurrentListKind: () => editor.");
            source.Should().Contain("ToggleBullets: () => editor.ToggleList(ListKind.Bullet)");
            source.Should().Contain("ToggleNumbering: () => editor.ToggleList(ListKind.Number)");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Bullets");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.AlignLeft");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.IndentIncrease");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.SpaceBeforeToggle");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.KeepWithNext");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.ParaBorder");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Sort");
        }

        wpf.Should().Contain("paragraphCommands.StatefulCommands");
    }

    private static void Execute(FreeWRibbonCommandBindingPorts bindings, string commandId)
    {
        bindings.TryGet(commandId, out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static ParagraphEditingRibbonPorts CreatePorts(
        ICollection<string> events,
        Func<ListKind>? currentListKind = null) =>
        new(
            PrepareExecution: () => events.Add("prepare"),
            CurrentListKind: currentListKind ?? (() => ListKind.None),
            ToggleBullets: () => events.Add("bullets"),
            ToggleNumbering: () => events.Add("numbering"),
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

}
