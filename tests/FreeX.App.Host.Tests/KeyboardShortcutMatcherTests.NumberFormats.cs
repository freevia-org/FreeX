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

    // R125-keyboard-numberformat-numpad-1: the Avalonia shell's TryGetWorkbookShortcutKey (see
    // MainWindow.cs) aliases NumPad1..6 to the same D1..D6 shortcut keys used by the top-row
    // digits, so Ctrl+Shift+NumPad1..6 already applies these number formats there. This host's
    // matcher used a separate resolver (TryGetWorkbookNumberFormatShortcutKey) that had NO NumPad
    // aliases at all, silently swallowing the numpad chord instead of applying the format --
    // a real cross-shell divergence, fixed to match Avalonia's aliasing.
    [Theory]
    [InlineData(Key.NumPad1, NumberFormatShortcut.Number)]
    [InlineData(Key.NumPad2, NumberFormatShortcut.Time)]
    [InlineData(Key.NumPad3, NumberFormatShortcut.Date)]
    [InlineData(Key.NumPad4, NumberFormatShortcut.Currency)]
    [InlineData(Key.NumPad5, NumberFormatShortcut.Percentage)]
    [InlineData(Key.NumPad6, NumberFormatShortcut.Scientific)]
    public void TryGetNumberFormatShortcut_MapsCtrlShiftNumPadShortcuts_MatchingAvalonia(
        Key key,
        NumberFormatShortcut expected)
    {
        var result = KeyboardShortcutMatcher.TryGetNumberFormatShortcut(
            key,
            ModifierKeys.Control | ModifierKeys.Shift,
            out var shortcut);

        result.Should().BeTrue();
        shortcut.Should().Be(expected);
    }

    // Sibling no-regression: an unrelated numpad digit outside the 1-6 range (and NumPad0, which
    // has no number-format route at all in the shared catalog) must still be correctly rejected,
    // not accidentally swept in by a too-broad NumPad range check.
    [Theory]
    [InlineData(Key.NumPad0)]
    [InlineData(Key.NumPad7)]
    [InlineData(Key.NumPad8)]
    [InlineData(Key.NumPad9)]
    public void TryGetNumberFormatShortcut_DoesNotMatchOutOfRangeNumPadDigits(Key key)
    {
        var result = KeyboardShortcutMatcher.TryGetNumberFormatShortcut(
            key,
            ModifierKeys.Control | ModifierKeys.Shift,
            out _);

        result.Should().BeFalse();
    }
}
