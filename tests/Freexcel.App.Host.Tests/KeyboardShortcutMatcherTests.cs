using System.Windows.Input;
using FluentAssertions;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host.Tests;

public sealed class KeyboardShortcutMatcherTests
{
    [Theory]
    [InlineData(Key.Add, Key.None, ModifierKeys.Control, true)]
    [InlineData(Key.OemPlus, Key.None, ModifierKeys.Control, true)]
    [InlineData(Key.None, Key.Add, ModifierKeys.Control, true)]
    [InlineData(Key.Add, Key.None, ModifierKeys.Control | ModifierKeys.Shift, false)]
    public void IsCtrlPlus_RecognizesExcelInsertShortcut(Key key, Key systemKey, ModifierKeys modifiers, bool expected)
    {
        KeyboardShortcutMatcher.IsCtrlPlus(key, systemKey, modifiers).Should().Be(expected);
    }

    [Theory]
    [InlineData(Key.Subtract, Key.None, ModifierKeys.Control, true)]
    [InlineData(Key.OemMinus, Key.None, ModifierKeys.Control, true)]
    [InlineData(Key.None, Key.OemMinus, ModifierKeys.Control, true)]
    [InlineData(Key.Subtract, Key.None, ModifierKeys.Control | ModifierKeys.Shift, false)]
    public void IsCtrlMinus_RecognizesExcelDeleteShortcut(Key key, Key systemKey, ModifierKeys modifiers, bool expected)
    {
        KeyboardShortcutMatcher.IsCtrlMinus(key, systemKey, modifiers).Should().Be(expected);
    }

    [Theory]
    [InlineData(Key.D9, ModifierKeys.Control, KeyboardGridShortcut.HideRows)]
    [InlineData(Key.NumPad9, ModifierKeys.Control, KeyboardGridShortcut.HideRows)]
    [InlineData(Key.D9, ModifierKeys.Control | ModifierKeys.Shift, KeyboardGridShortcut.UnhideRows)]
    [InlineData(Key.D0, ModifierKeys.Control, KeyboardGridShortcut.HideColumns)]
    [InlineData(Key.NumPad0, ModifierKeys.Control | ModifierKeys.Shift, KeyboardGridShortcut.UnhideColumns)]
    [InlineData(Key.D8, ModifierKeys.Control, null)]
    public void TryGetGridShortcut_MapsHideAndUnhideShortcuts(Key key, ModifierKeys modifiers, KeyboardGridShortcut? expected)
    {
        var result = KeyboardShortcutMatcher.TryGetGridShortcut(key, modifiers, out var shortcut);

        result.Should().Be(expected is not null);
        if (expected is not null)
            shortcut.Should().Be(expected.Value);
    }

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
    [InlineData(Key.B, ModifierKeys.Control, FontToggleShortcut.Bold)]
    [InlineData(Key.D2, ModifierKeys.Control, FontToggleShortcut.Bold)]
    [InlineData(Key.I, ModifierKeys.Control, FontToggleShortcut.Italic)]
    [InlineData(Key.D3, ModifierKeys.Control, FontToggleShortcut.Italic)]
    [InlineData(Key.U, ModifierKeys.Control, FontToggleShortcut.Underline)]
    [InlineData(Key.D4, ModifierKeys.Control, FontToggleShortcut.Underline)]
    [InlineData(Key.D5, ModifierKeys.Control, null)]
    public void TryGetFontToggleShortcut_MapsExcelFontShortcuts(Key key, ModifierKeys modifiers, FontToggleShortcut? expected)
    {
        var result = KeyboardShortcutMatcher.TryGetFontToggleShortcut(key, modifiers, out var shortcut);

        result.Should().Be(expected is not null);
        if (expected is not null)
            shortcut.Should().Be(expected.Value);
    }

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
}
