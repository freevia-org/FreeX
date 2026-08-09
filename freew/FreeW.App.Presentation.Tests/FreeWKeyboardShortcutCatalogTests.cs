using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWKeyboardShortcutCatalogTests
{
    [Fact]
    public void CatalogDefinesEverySharedCommandExactlyOnce()
    {
        FreeWKeyboardShortcutCatalog.All.Should().HaveCount(19);
        FreeWKeyboardShortcutCatalog.All
            .Select(shortcut => shortcut.Command)
            .Should().BeEquivalentTo(Enum.GetValues<FreeWKeyboardCommand>());
        FreeWKeyboardShortcutCatalog.All
            .Select(shortcut => (shortcut.Key, shortcut.Modifiers))
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ShiftF9_is_the_current_field_code_toggle()
    {
        FreeWKeyboardShortcutCatalog.All.Should().ContainSingle(shortcut =>
            shortcut.Command == FreeWKeyboardCommand.ToggleCurrentFieldCode &&
            shortcut.Key == FreeWKeyboardKey.F9 &&
            shortcut.Modifiers == FreeWKeyboardModifiers.Shift);
    }

    [Fact]
    public void PrintDocument_is_the_shared_ctrl_p_command()
    {
        FreeWKeyboardShortcutCatalog.All.Should().ContainSingle(shortcut =>
            shortcut.Command == FreeWKeyboardCommand.PrintDocument &&
            shortcut.Key == FreeWKeyboardKey.P &&
            shortcut.Modifiers == FreeWKeyboardModifiers.Control);
    }

    [Fact]
    public void EveryCatalogGestureDispatchesItsDeclaredCommand()
    {
        foreach (var shortcut in FreeWKeyboardShortcutCatalog.All)
        {
            FreeWKeyboardCommand? dispatched = null;

            var handled = FreeWKeyboardShortcutCatalog.TryDispatch(
                shortcut.Key,
                shortcut.Modifiers,
                command => dispatched = command);

            handled.Should().BeTrue();
            dispatched.Should().Be(shortcut.Command);
        }
    }

    [Theory]
    [InlineData(FreeWKeyboardKey.F, FreeWKeyboardModifiers.None)]
    [InlineData(FreeWKeyboardKey.F1, FreeWKeyboardModifiers.Control)]
    [InlineData(FreeWKeyboardKey.Z, FreeWKeyboardModifiers.Control | FreeWKeyboardModifiers.Shift)]
    public void UnmappedGesturesAreNotConsumed(
        FreeWKeyboardKey key,
        FreeWKeyboardModifiers modifiers)
    {
        var dispatched = false;

        FreeWKeyboardShortcutCatalog.TryDispatch(key, modifiers, _ => dispatched = true)
            .Should().BeFalse();
        dispatched.Should().BeFalse();
    }
}
