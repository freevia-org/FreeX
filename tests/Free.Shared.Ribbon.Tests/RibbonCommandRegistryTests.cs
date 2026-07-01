namespace Free.Shared.Ribbon.Tests;

public class RibbonCommandRegistryTests
{
    private sealed class CountingCommand : IRibbonCommand
    {
        public int Invocations { get; private set; }
        public void Execute(RibbonCommandContext context) => Invocations++;
    }

    [Fact]
    public void Resolves_RegisteredCommand()
    {
        var registry = new RibbonCommandRegistry();
        var command = new CountingCommand();
        registry.Register("paste", command);

        registry.TryGet("paste", out var resolved).Should().BeTrue();
        resolved!.Execute(RibbonCommandContext.Empty);
        command.Invocations.Should().Be(1);
    }

    [Fact]
    public void Missing_Command_ResolvesFalse()
    {
        new RibbonCommandRegistry().TryGet("nope", out _).Should().BeFalse();
    }

    [Fact]
    public void ActionRibbonCommand_InvokesDelegate()
    {
        var invocations = 0;
        var command = new ActionRibbonCommand(() => invocations++);

        command.Execute(RibbonCommandContext.Empty);

        invocations.Should().Be(1);
    }

    [Fact]
    public void ContextRibbonCommand_ForwardsExecutionContext()
    {
        RibbonCommandContext? captured = null;
        var context = RibbonCommandContext.ForSelectedValue("Heading1");
        var command = new ContextRibbonCommand(value => captured = value);

        command.Execute(context);

        captured.Should().BeSameAs(context);
    }

    [Fact]
    public void ValueRibbonCommand_ForwardsSelectedValue()
    {
        string? captured = null;
        var command = new ValueRibbonCommand(value => captured = value);

        command.Execute(RibbonCommandContext.ForSelectedValue("12"));

        captured.Should().Be("12");
    }

    [Fact]
    public void EmptyRibbonCommand_IsReusableSafePlaceholder()
    {
        var command = EmptyRibbonCommand.Instance;

        command.Execute(RibbonCommandContext.Empty);

        command.Should().BeSameAs(EmptyRibbonCommand.Instance);
    }
}
