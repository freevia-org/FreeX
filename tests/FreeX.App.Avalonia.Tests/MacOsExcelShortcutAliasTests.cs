using Avalonia.Input;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Excel for Mac documents Cmd-based chords for commands whose shared catalog entry is the Windows
/// chord. The alias table projects them onto the Windows chord on macOS only.
/// </summary>
public sealed class MacOsExcelShortcutAliasTests
{
    [Theory]
    [InlineData(Key.Y, KeyModifiers.Meta, Key.Y, KeyModifiers.Control)]
    [InlineData(Key.V, KeyModifiers.Control | KeyModifiers.Meta, Key.V, KeyModifiers.Meta | KeyModifiers.Alt)]
    [InlineData(Key.F, KeyModifiers.Meta | KeyModifiers.Shift, Key.L, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.OemSemicolon, KeyModifiers.Meta, Key.OemSemicolon, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.K, KeyModifiers.Meta | KeyModifiers.Shift, Key.Right, KeyModifiers.Alt | KeyModifiers.Shift)]
    [InlineData(Key.J, KeyModifiers.Meta | KeyModifiers.Shift, Key.Left, KeyModifiers.Alt | KeyModifiers.Shift)]
    public void MacChords_MapToSharedWindowsChordsOnlyOnMac(
        Key key,
        KeyModifiers modifiers,
        Key expectedKey,
        KeyModifiers expectedModifiers)
    {
        MainWindow.TryMapMacExcelShortcutAlias(true, key, modifiers, out var mappedKey, out var mappedModifiers)
            .Should().BeTrue();
        mappedKey.Should().Be(expectedKey);
        mappedModifiers.Should().Be(expectedModifiers);

        MainWindow.TryMapMacExcelShortcutAlias(false, key, modifiers, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(Key.C, KeyModifiers.Meta)]
    [InlineData(Key.Y, KeyModifiers.Control)]
    [InlineData(Key.F, KeyModifiers.Meta)]
    public void UnrelatedChords_AreNotAliased(Key key, KeyModifiers modifiers)
    {
        MainWindow.TryMapMacExcelShortcutAlias(true, key, modifiers, out _, out _).Should().BeFalse();
    }
}
