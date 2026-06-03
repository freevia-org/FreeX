using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.App.Host.Tests;

public sealed partial class KeyboardShortcutMatcherTests
{
    [Theory]
    [InlineData(Key.D7, ModifierKeys.Control | ModifierKeys.Shift, BorderKeyboardShortcut.Outline)]
    [InlineData(Key.OemMinus, ModifierKeys.Control | ModifierKeys.Shift, BorderKeyboardShortcut.ClearOutline)]
    [InlineData(Key.D7, ModifierKeys.Control, null)]
    public void TryGetBorderShortcut_MapsOutlineBorderShortcuts(Key key, ModifierKeys modifiers, BorderKeyboardShortcut? expected)
    {
        var result = KeyboardShortcutMatcher.TryGetBorderShortcut(key, modifiers, out var shortcut);

        result.Should().Be(expected is not null);
        if (expected is not null)
            shortcut.Should().Be(expected.Value);
    }

    [Theory]
    [InlineData(Key.D7)]
    [InlineData(Key.OemMinus)]
    public void TryGetBorderShortcut_DoesNotStealAltModifiedChords(Key key)
    {
        var result = KeyboardShortcutMatcher.TryGetBorderShortcut(
            key,
            ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt,
            out _);

        result.Should().BeFalse();
    }
}
