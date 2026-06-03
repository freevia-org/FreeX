using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.App.Host.Tests;

public sealed partial class KeyboardShortcutMatcherTests
{
    [Theory]
    [InlineData(Key.B, ModifierKeys.Control, FontToggleShortcut.Bold)]
    [InlineData(Key.D2, ModifierKeys.Control, FontToggleShortcut.Bold)]
    [InlineData(Key.I, ModifierKeys.Control, FontToggleShortcut.Italic)]
    [InlineData(Key.D3, ModifierKeys.Control, FontToggleShortcut.Italic)]
    [InlineData(Key.U, ModifierKeys.Control, FontToggleShortcut.Underline)]
    [InlineData(Key.D4, ModifierKeys.Control, FontToggleShortcut.Underline)]
    [InlineData(Key.D5, ModifierKeys.Control, FontToggleShortcut.Strikethrough)]
    [InlineData(Key.NumPad5, ModifierKeys.Control, FontToggleShortcut.Strikethrough)]
    public void TryGetFontToggleShortcut_MapsExcelFontShortcuts(Key key, ModifierKeys modifiers, FontToggleShortcut? expected)
    {
        var result = KeyboardShortcutMatcher.TryGetFontToggleShortcut(key, modifiers, out var shortcut);

        result.Should().Be(expected is not null);
        if (expected is not null)
            shortcut.Should().Be(expected.Value);
    }

    [Theory]
    [InlineData(Key.B, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.B, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.I, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.I, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.U, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.U, ModifierKeys.Control | ModifierKeys.Shift)]
    public void TryGetFontToggleShortcut_DoesNotStealExtraModifierCombinations(Key key, ModifierKeys modifiers)
    {
        var result = KeyboardShortcutMatcher.TryGetFontToggleShortcut(key, modifiers, out _);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(Key.NumPad2, ModifierKeys.None)]
    [InlineData(Key.NumPad3, ModifierKeys.None)]
    [InlineData(Key.NumPad4, ModifierKeys.None)]
    [InlineData(Key.NumPad5, ModifierKeys.None)]
    [InlineData(Key.NumPad5, ModifierKeys.Shift)]
    public void TryGetFontToggleShortcut_AliasKeysRequireExactModifiers(Key key, ModifierKeys modifiers)
    {
        var result = KeyboardShortcutMatcher.TryGetFontToggleShortcut(key, modifiers, out _);

        result.Should().BeFalse();
    }
}
