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
    [InlineData(Key.V, ModifierKeys.Control | ModifierKeys.Alt, true)]
    [InlineData(Key.V, ModifierKeys.Control, false)]
    [InlineData(Key.V, ModifierKeys.Control | ModifierKeys.Shift, false)]
    [InlineData(Key.C, ModifierKeys.Control | ModifierKeys.Alt, false)]
    public void IsPasteSpecialShortcut_RecognizesExcelCtrlAltVOnly(Key key, ModifierKeys modifiers, bool expected)
    {
        KeyboardShortcutMatcher.IsPasteSpecialShortcut(key, modifiers).Should().Be(expected);
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
    [InlineData(Key.Space, ModifierKeys.Control | ModifierKeys.Shift, KeyboardSelectionShortcut.SelectAll)]
    [InlineData(Key.Multiply, ModifierKeys.Control | ModifierKeys.Shift, KeyboardSelectionShortcut.SelectCurrentRegion)]
    [InlineData(Key.D8, ModifierKeys.Control | ModifierKeys.Shift, KeyboardSelectionShortcut.SelectCurrentRegion)]
    [InlineData(Key.Space, ModifierKeys.Control, null)]
    [InlineData(Key.D8, ModifierKeys.Control, null)]
    public void TryGetSelectionShortcut_MapsExcelSelectionShortcuts(Key key, ModifierKeys modifiers, KeyboardSelectionShortcut? expected)
    {
        var result = KeyboardShortcutMatcher.TryGetSelectionShortcut(key, modifiers, out var shortcut);

        result.Should().Be(expected is not null);
        if (expected is not null)
            shortcut.Should().Be(expected.Value);
    }

    [Theory]
    [InlineData(Key.T, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.CreateTable)]
    [InlineData(Key.L, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.CreateTable)]
    [InlineData(Key.F3, Key.None, ModifierKeys.Shift, KeyboardCommandShortcut.InsertFunction)]
    [InlineData(Key.F7, Key.None, ModifierKeys.None, KeyboardCommandShortcut.SpellCheck)]
    [InlineData(Key.F9, Key.None, ModifierKeys.None, KeyboardCommandShortcut.CalculateNow)]
    [InlineData(Key.F9, Key.None, ModifierKeys.Shift, KeyboardCommandShortcut.CalculateSheet)]
    [InlineData(Key.F9, Key.None, ModifierKeys.Control | ModifierKeys.Alt, KeyboardCommandShortcut.CalculateNow)]
    [InlineData(Key.F9, Key.None, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, KeyboardCommandShortcut.CalculateNow)]
    [InlineData(Key.U, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.ToggleFormulaBarExpansion)]
    [InlineData(Key.Q, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.QuickAnalysis)]
    [InlineData(Key.None, Key.F1, ModifierKeys.Alt, KeyboardCommandShortcut.InsertEmbeddedChart)]
    [InlineData(Key.F11, Key.None, ModifierKeys.None, KeyboardCommandShortcut.InsertChartSheet)]
    [InlineData(Key.F, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.OpenFormatCellsFont)]
    [InlineData(Key.P, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.OpenFormatCellsFont)]
    [InlineData(Key.G, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.WorkbookStatistics)]
    [InlineData(Key.F2, Key.None, ModifierKeys.Shift, KeyboardCommandShortcut.NewNote)]
    [InlineData(Key.F2, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.NewThreadedComment)]
    [InlineData(Key.F12, Key.None, ModifierKeys.None, KeyboardCommandShortcut.SaveAs)]
    [InlineData(Key.F10, Key.None, ModifierKeys.None, KeyboardCommandShortcut.ShowKeyTips)]
    [InlineData(Key.F10, Key.None, ModifierKeys.Shift, KeyboardCommandShortcut.OpenContextMenu)]
    [InlineData(Key.Apps, Key.None, ModifierKeys.None, KeyboardCommandShortcut.OpenContextMenu)]
    [InlineData(Key.F2, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.EditInFormulaBar)]
    [InlineData(Key.None, Key.F1, ModifierKeys.Alt | ModifierKeys.Shift, KeyboardCommandShortcut.InsertWorksheet)]
    [InlineData(Key.OemPlus, Key.None, ModifierKeys.Control | ModifierKeys.Alt, KeyboardCommandShortcut.ZoomIn)]
    [InlineData(Key.Add, Key.None, ModifierKeys.Control | ModifierKeys.Alt, KeyboardCommandShortcut.ZoomIn)]
    [InlineData(Key.OemMinus, Key.None, ModifierKeys.Control | ModifierKeys.Alt, KeyboardCommandShortcut.ZoomOut)]
    [InlineData(Key.Subtract, Key.None, ModifierKeys.Control | ModifierKeys.Alt, KeyboardCommandShortcut.ZoomOut)]
    [InlineData(Key.OemQuotes, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.CopyFormulaFromAbove)]
    [InlineData(Key.OemQuotes, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.CopyValueFromAbove)]
    [InlineData(Key.None, Key.Down, ModifierKeys.Alt, KeyboardCommandShortcut.OpenActiveDropdown)]
    public void TryGetCommandShortcut_MapsCommonExcelShortcuts(
        Key key,
        Key systemKey,
        ModifierKeys modifiers,
        KeyboardCommandShortcut expected)
    {
        var result = KeyboardShortcutMatcher.TryGetCommandShortcut(key, systemKey, modifiers, out var shortcut);

        result.Should().BeTrue();
        shortcut.Should().Be(expected);
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
