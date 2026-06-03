using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.App.Host.Tests;

public sealed partial class KeyboardShortcutMatcherTests
{
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
    [InlineData(Key.NumPad9, ModifierKeys.None)]
    [InlineData(Key.NumPad9, ModifierKeys.Shift)]
    [InlineData(Key.NumPad9, ModifierKeys.Alt)]
    [InlineData(Key.NumPad0, ModifierKeys.None)]
    [InlineData(Key.NumPad0, ModifierKeys.Shift)]
    [InlineData(Key.NumPad0, ModifierKeys.Alt)]
    public void TryGetGridShortcut_AliasKeysRequireExactModifiers(Key key, ModifierKeys modifiers)
    {
        var result = KeyboardShortcutMatcher.TryGetGridShortcut(key, modifiers, out _);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(Key.Space, ModifierKeys.Control | ModifierKeys.Shift, KeyboardSelectionShortcut.SelectAll)]
    [InlineData(Key.Space, ModifierKeys.Control, KeyboardSelectionShortcut.SelectWholeColumns)]
    [InlineData(Key.Space, ModifierKeys.Shift, KeyboardSelectionShortcut.SelectWholeRows)]
    [InlineData(Key.Multiply, ModifierKeys.Control, KeyboardSelectionShortcut.SelectCurrentRegion)]
    [InlineData(Key.Multiply, ModifierKeys.Control | ModifierKeys.Shift, KeyboardSelectionShortcut.SelectCurrentRegion)]
    [InlineData(Key.D8, ModifierKeys.Control | ModifierKeys.Shift, KeyboardSelectionShortcut.SelectCurrentRegion)]
    [InlineData(Key.D8, ModifierKeys.Control, null)]
    public void TryGetSelectionShortcut_MapsExcelSelectionShortcuts(Key key, ModifierKeys modifiers, KeyboardSelectionShortcut? expected)
    {
        var result = KeyboardShortcutMatcher.TryGetSelectionShortcut(key, modifiers, out var shortcut);

        result.Should().Be(expected is not null);
        if (expected is not null)
            shortcut.Should().Be(expected.Value);
    }

    [Theory]
    [InlineData(Key.Multiply, ModifierKeys.None)]
    [InlineData(Key.Multiply, ModifierKeys.Shift)]
    [InlineData(Key.Multiply, ModifierKeys.Alt)]
    [InlineData(Key.D8, ModifierKeys.None)]
    [InlineData(Key.D8, ModifierKeys.Shift)]
    [InlineData(Key.D8, ModifierKeys.Alt)]
    public void TryGetSelectionShortcut_CurrentRegionKeysRequireExactModifiers(Key key, ModifierKeys modifiers)
    {
        var result = KeyboardShortcutMatcher.TryGetSelectionShortcut(key, modifiers, out _);

        result.Should().BeFalse();
    }
}
