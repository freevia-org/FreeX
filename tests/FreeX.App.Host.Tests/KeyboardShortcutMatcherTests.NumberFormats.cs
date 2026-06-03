using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.App.Host.Tests;

public sealed partial class KeyboardShortcutMatcherTests
{
    [Theory]
    [InlineData(Key.Oem3, NumberFormatShortcut.General)]
    [InlineData(Key.D1, NumberFormatShortcut.Number)]
    [InlineData(Key.D2, NumberFormatShortcut.Time)]
    [InlineData(Key.D3, NumberFormatShortcut.Date)]
    [InlineData(Key.D4, NumberFormatShortcut.Currency)]
    [InlineData(Key.D5, NumberFormatShortcut.Percentage)]
    [InlineData(Key.D6, NumberFormatShortcut.Scientific)]
    public void TryGetNumberFormatShortcut_MapsCtrlShiftNumberShortcuts(Key key, NumberFormatShortcut expected)
    {
        var result = KeyboardShortcutMatcher.TryGetNumberFormatShortcut(
            key,
            ModifierKeys.Control | ModifierKeys.Shift,
            out var shortcut);

        result.Should().BeTrue();
        shortcut.Should().Be(expected);
    }

    [Theory]
    [InlineData(Key.Oem3)]
    [InlineData(Key.D1)]
    [InlineData(Key.D2)]
    [InlineData(Key.D3)]
    [InlineData(Key.D4)]
    [InlineData(Key.D5)]
    [InlineData(Key.D6)]
    public void TryGetNumberFormatShortcut_DoesNotStealAltModifiedChords(Key key)
    {
        var result = KeyboardShortcutMatcher.TryGetNumberFormatShortcut(
            key,
            ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt,
            out _);

        result.Should().BeFalse();
    }
}
