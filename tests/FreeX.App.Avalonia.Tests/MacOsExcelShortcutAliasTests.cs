using Avalonia.Input;
using FluentAssertions;
using FreeX.App.Presentation.Shell;

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
    [InlineData(Key.U, KeyModifiers.Control, Key.F2, KeyModifiers.None)]
    [InlineData(Key.F3, KeyModifiers.Meta | KeyModifiers.Shift, Key.F3, KeyModifiers.Control | KeyModifiers.Shift)]
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
    [InlineData(Key.U, KeyModifiers.Meta)]
    [InlineData(Key.Q, KeyModifiers.Meta)]
    [InlineData(Key.Q, KeyModifiers.Control)]
    [InlineData(Key.U, KeyModifiers.Control | KeyModifiers.Shift)]
    public void UnrelatedChords_AreNotAliased(Key key, KeyModifiers modifiers)
    {
        MainWindow.TryMapMacExcelShortcutAlias(true, key, modifiers, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void CmdShiftF3_ResolvesToCreateNamesFromSelection()
    {
        MainWindow.TryMapMacExcelShortcutAlias(true, Key.F3, KeyModifiers.Meta | KeyModifiers.Shift, out var key, out var modifiers)
            .Should().BeTrue();
        WorkbookKeyboardShortcutCatalog.TryGetApplicationCommand(
                WorkbookShortcutKey.F3,
                (modifiers.HasFlag(KeyModifiers.Control) ? WorkbookShortcutModifiers.Control : 0) |
                (modifiers.HasFlag(KeyModifiers.Shift) ? WorkbookShortcutModifiers.Shift : 0),
                out var command)
            .Should().BeTrue();
        key.Should().Be(Key.F3);
        command.Should().Be(KeyboardCommandShortcut.CreateNamesFromSelection);
    }

    [Fact]
    public void ControlQAndControlShiftU_AreTheMacExcelChordsAlreadyInTheCatalog_AndCmdQIsNotQuickAnalysis()
    {
        WorkbookKeyboardShortcutCatalog.TryGetApplicationCommand(
                WorkbookShortcutKey.Q, WorkbookShortcutModifiers.Control, out var quick).Should().BeTrue();
        quick.Should().Be(KeyboardCommandShortcut.QuickAnalysis);
        WorkbookKeyboardShortcutCatalog.TryGetApplicationCommand(
                WorkbookShortcutKey.U, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, out var formulaBar)
            .Should().BeTrue();
        formulaBar.Should().Be(KeyboardCommandShortcut.ToggleFormulaBarExpansion);
        WorkbookKeyboardShortcutCatalog.TryGetApplicationCommand(
                WorkbookShortcutKey.Q, WorkbookShortcutModifiers.Meta, out _).Should().BeFalse("Cmd+Q is Quit");
    }
}
