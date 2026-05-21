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
    [InlineData(Key.OemPlus, Key.None, ModifierKeys.Control | ModifierKeys.Shift, true)]
    [InlineData(Key.None, Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift, true)]
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
    [InlineData(Key.V, Key.None, ModifierKeys.Control | ModifierKeys.Alt, true)]
    [InlineData(Key.System, Key.V, ModifierKeys.Control | ModifierKeys.Alt, true)]
    [InlineData(Key.V, Key.None, ModifierKeys.Control, false)]
    [InlineData(Key.V, Key.None, ModifierKeys.Control | ModifierKeys.Shift, false)]
    [InlineData(Key.C, Key.None, ModifierKeys.Control | ModifierKeys.Alt, false)]
    public void IsPasteSpecialShortcut_RecognizesExcelCtrlAltVOnly(Key key, Key systemKey, ModifierKeys modifiers, bool expected)
    {
        KeyboardShortcutMatcher.IsPasteSpecialShortcut(key, systemKey, modifiers).Should().Be(expected);
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
    [InlineData(Key.Space, ModifierKeys.Control, KeyboardSelectionShortcut.SelectWholeColumns)]
    [InlineData(Key.Space, ModifierKeys.Shift, KeyboardSelectionShortcut.SelectWholeRows)]
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
    [InlineData(Key.N, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.NewWorkbook)]
    [InlineData(Key.O, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.OpenWorkbook)]
    [InlineData(Key.S, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.SaveWorkbook)]
    [InlineData(Key.C, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.Copy)]
    [InlineData(Key.C, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.Copy)]
    [InlineData(Key.Insert, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.Copy)]
    [InlineData(Key.X, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.Cut)]
    [InlineData(Key.Delete, Key.None, ModifierKeys.Shift, KeyboardCommandShortcut.Cut)]
    [InlineData(Key.V, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.Paste)]
    [InlineData(Key.Insert, Key.None, ModifierKeys.Shift, KeyboardCommandShortcut.Paste)]
    [InlineData(Key.A, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.SelectCurrentRegionOrAll)]
    [InlineData(Key.Z, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.Undo)]
    [InlineData(Key.Y, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.Redo)]
    [InlineData(Key.T, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.CreateTable)]
    [InlineData(Key.L, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.CreateTable)]
    [InlineData(Key.K, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.InsertHyperlink)]
    [InlineData(Key.D, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.FillDown)]
    [InlineData(Key.R, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.FillRight)]
    [InlineData(Key.E, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.FlashFill)]
    [InlineData(Key.OemSemicolon, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.InsertCurrentDate)]
    [InlineData(Key.OemSemicolon, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.InsertCurrentTime)]
    [InlineData(Key.Oem3, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.ToggleShowFormulas)]
    [InlineData(Key.PageUp, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.ActivatePreviousSheet)]
    [InlineData(Key.PageDown, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.ActivateNextSheet)]
    [InlineData(Key.PageUp, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.SelectPreviousSheetGroup)]
    [InlineData(Key.PageDown, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.SelectNextSheetGroup)]
    [InlineData(Key.D1, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.OpenFormatCells)]
    [InlineData(Key.NumPad1, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.OpenFormatCells)]
    [InlineData(Key.F, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.Find)]
    [InlineData(Key.H, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.Replace)]
    [InlineData(Key.F3, Key.None, ModifierKeys.Shift, KeyboardCommandShortcut.InsertFunction)]
    [InlineData(Key.F7, Key.None, ModifierKeys.None, KeyboardCommandShortcut.SpellCheck)]
    [InlineData(Key.F4, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.CloseWorkbook)]
    [InlineData(Key.W, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.CloseWorkbook)]
    [InlineData(Key.F9, Key.None, ModifierKeys.None, KeyboardCommandShortcut.CalculateNow)]
    [InlineData(Key.F9, Key.None, ModifierKeys.Shift, KeyboardCommandShortcut.CalculateSheet)]
    [InlineData(Key.F9, Key.None, ModifierKeys.Control | ModifierKeys.Alt, KeyboardCommandShortcut.CalculateNow)]
    [InlineData(Key.F9, Key.None, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, KeyboardCommandShortcut.RebuildDependenciesAndCalculate)]
    [InlineData(Key.U, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.ToggleFormulaBarExpansion)]
    [InlineData(Key.Q, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.QuickAnalysis)]
    [InlineData(Key.P, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.OpenPrintPreview)]
    [InlineData(Key.V, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.PasteValues)]
    [InlineData(Key.L, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.ToggleFilter)]
    [InlineData(Key.F5, Key.None, ModifierKeys.None, KeyboardCommandShortcut.GoTo)]
    [InlineData(Key.G, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.GoTo)]
    [InlineData(Key.None, Key.F1, ModifierKeys.Alt, KeyboardCommandShortcut.InsertEmbeddedChart)]
    [InlineData(Key.F11, Key.None, ModifierKeys.None, KeyboardCommandShortcut.InsertChartSheet)]
    [InlineData(Key.None, Key.OemPlus, ModifierKeys.Alt, KeyboardCommandShortcut.AutoSum)]
    [InlineData(Key.None, Key.Add, ModifierKeys.Alt, KeyboardCommandShortcut.AutoSum)]
    [InlineData(Key.None, Key.Right, ModifierKeys.Alt | ModifierKeys.Shift, KeyboardCommandShortcut.GroupSelection)]
    [InlineData(Key.None, Key.Left, ModifierKeys.Alt | ModifierKeys.Shift, KeyboardCommandShortcut.UngroupSelection)]
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
    [InlineData(Key.F11, Key.None, ModifierKeys.Shift, KeyboardCommandShortcut.InsertWorksheet)]
    [InlineData(Key.None, Key.F1, ModifierKeys.Alt | ModifierKeys.Shift, KeyboardCommandShortcut.InsertWorksheet)]
    [InlineData(Key.OemPlus, Key.None, ModifierKeys.Control | ModifierKeys.Alt, KeyboardCommandShortcut.ZoomIn)]
    [InlineData(Key.Add, Key.None, ModifierKeys.Control | ModifierKeys.Alt, KeyboardCommandShortcut.ZoomIn)]
    [InlineData(Key.OemMinus, Key.None, ModifierKeys.Control | ModifierKeys.Alt, KeyboardCommandShortcut.ZoomOut)]
    [InlineData(Key.Subtract, Key.None, ModifierKeys.Control | ModifierKeys.Alt, KeyboardCommandShortcut.ZoomOut)]
    [InlineData(Key.OemQuotes, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.CopyFormulaFromAbove)]
    [InlineData(Key.OemQuotes, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.CopyValueFromAbove)]
    [InlineData(Key.None, Key.Down, ModifierKeys.Alt, KeyboardCommandShortcut.OpenActiveDropdown)]
    [InlineData(Key.Back, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.ScrollActiveCellIntoView)]
    [InlineData(Key.OemPeriod, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.CycleSelectionCorner)]
    [InlineData(Key.Decimal, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.CycleSelectionCorner)]
    [InlineData(Key.OemOpenBrackets, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.SelectDirectPrecedents)]
    [InlineData(Key.OemCloseBrackets, Key.None, ModifierKeys.Control, KeyboardCommandShortcut.SelectDirectDependents)]
    [InlineData(Key.OemOpenBrackets, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.SelectAllPrecedents)]
    [InlineData(Key.OemCloseBrackets, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.SelectAllDependents)]
    [InlineData(Key.O, Key.None, ModifierKeys.Control | ModifierKeys.Shift, KeyboardCommandShortcut.SelectCellsWithComments)]
    [InlineData(Key.None, Key.Oem1, ModifierKeys.Alt, KeyboardCommandShortcut.SelectVisibleCellsOnly)]
    [InlineData(Key.F2, Key.None, ModifierKeys.None, KeyboardCommandShortcut.EditCell)]
    [InlineData(Key.Delete, Key.None, ModifierKeys.None, KeyboardCommandShortcut.ClearSelection)]
    [InlineData(Key.Back, Key.None, ModifierKeys.None, KeyboardCommandShortcut.ClearSelectionAndEdit)]
    [InlineData(Key.Back, Key.None, ModifierKeys.Shift, KeyboardCommandShortcut.ClearSelectionAndEdit)]
    [InlineData(Key.F4, Key.None, ModifierKeys.None, KeyboardCommandShortcut.RepeatLastAction)]
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
    [InlineData(Key.C, Key.None, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.X, Key.None, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.X, Key.None, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.A, Key.None, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.Z, Key.None, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.Y, Key.None, ModifierKeys.Control | ModifierKeys.Alt)]
    public void TryGetCommandShortcut_DoesNotStealExtraModifierCombinations(Key key, Key systemKey, ModifierKeys modifiers)
    {
        var result = KeyboardShortcutMatcher.TryGetCommandShortcut(key, systemKey, modifiers, out _);

        result.Should().BeFalse();
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
