namespace Free.Shared.Ribbon.Tests;

public sealed class SisterQuickAccessToolbarCatalogTests
{
    [Fact]
    public void DefaultCommands_define_the_shared_save_undo_redo_contract()
    {
        SisterQuickAccessToolbarCatalog.DefaultCommands
            .Select(command => command.CommandId)
            .Should().Equal("Save", "Undo", "Redo");
        SisterQuickAccessToolbarCatalog.DefaultCommands
            .Select(command => command.IconKind)
            .Should().Equal(
                RibbonCommandIconKind.Save,
                RibbonCommandIconKind.Undo,
                RibbonCommandIconKind.Redo);
    }

    [Fact]
    public void Execute_routes_each_known_command_and_rejects_unknown_commands()
    {
        var invoked = new List<string>();
        var actions = new SisterQuickAccessToolbarActions(
            Save: () => invoked.Add("Save"),
            Undo: () => invoked.Add("Undo"),
            Redo: () => invoked.Add("Redo"));

        foreach (var command in SisterQuickAccessToolbarCatalog.DefaultCommands)
            SisterQuickAccessToolbarCatalog.Execute(actions, command.CommandId).Should().BeTrue();

        SisterQuickAccessToolbarCatalog.Execute(actions, "Unknown").Should().BeFalse();
        invoked.Should().Equal("Save", "Undo", "Redo");
    }
}
