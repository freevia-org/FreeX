using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.App.Host.Tests;

public sealed partial class KeyboardShortcutMatcherTests
{
    [Theory]
    [InlineData(Key.Add, Key.None, ModifierKeys.Control, true)]
    [InlineData(Key.OemPlus, Key.None, ModifierKeys.Control, true)]
    [InlineData(Key.None, Key.Add, ModifierKeys.Control, true)]
    [InlineData(Key.System, Key.OemPlus, ModifierKeys.Control, true)]
    [InlineData(Key.OemPlus, Key.None, ModifierKeys.Control | ModifierKeys.Shift, true)]
    [InlineData(Key.None, Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift, true)]
    [InlineData(Key.Add, Key.None, ModifierKeys.Control | ModifierKeys.Shift, false)]
    [InlineData(Key.System, Key.OemPlus, ModifierKeys.Control | ModifierKeys.Alt, false)]
    [InlineData(Key.System, Key.Add, ModifierKeys.Control | ModifierKeys.Alt, false)]
    [InlineData(Key.C, Key.OemPlus, ModifierKeys.Control, false)]
    [InlineData(Key.C, Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift, false)]
    public void IsCtrlPlus_RecognizesExcelInsertShortcut(Key key, Key systemKey, ModifierKeys modifiers, bool expected)
    {
        KeyboardShortcutMatcher.IsCtrlPlus(key, systemKey, modifiers).Should().Be(expected);
    }

    [Theory]
    [InlineData(Key.Subtract, Key.None, ModifierKeys.Control, true)]
    [InlineData(Key.OemMinus, Key.None, ModifierKeys.Control, true)]
    [InlineData(Key.None, Key.OemMinus, ModifierKeys.Control, true)]
    [InlineData(Key.System, Key.OemMinus, ModifierKeys.Control, true)]
    [InlineData(Key.Subtract, Key.None, ModifierKeys.Control | ModifierKeys.Shift, false)]
    [InlineData(Key.System, Key.OemMinus, ModifierKeys.Control | ModifierKeys.Alt, false)]
    [InlineData(Key.System, Key.Subtract, ModifierKeys.Control | ModifierKeys.Alt, false)]
    [InlineData(Key.C, Key.OemMinus, ModifierKeys.Control, false)]
    public void IsCtrlMinus_RecognizesExcelDeleteShortcut(Key key, Key systemKey, ModifierKeys modifiers, bool expected)
    {
        KeyboardShortcutMatcher.IsCtrlMinus(key, systemKey, modifiers).Should().Be(expected);
    }

    [Theory]
    [InlineData(Key.V, Key.None, ModifierKeys.Control | ModifierKeys.Alt, true)]
    [InlineData(Key.System, Key.V, ModifierKeys.Control | ModifierKeys.Alt, true)]
    [InlineData(Key.System, Key.V, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, false)]
    [InlineData(Key.V, Key.None, ModifierKeys.Control, false)]
    [InlineData(Key.V, Key.None, ModifierKeys.Control | ModifierKeys.Shift, false)]
    [InlineData(Key.V, Key.None, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, false)]
    [InlineData(Key.C, Key.None, ModifierKeys.Control | ModifierKeys.Alt, false)]
    [InlineData(Key.C, Key.V, ModifierKeys.Control | ModifierKeys.Alt, false)]
    public void IsPasteSpecialShortcut_RecognizesExcelCtrlAltVOnly(Key key, Key systemKey, ModifierKeys modifiers, bool expected)
    {
        KeyboardShortcutMatcher.IsPasteSpecialShortcut(key, systemKey, modifiers).Should().Be(expected);
    }
}
