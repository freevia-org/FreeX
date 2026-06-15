namespace FreeX.Ribbon.Tests;

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
}
