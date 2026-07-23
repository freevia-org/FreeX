using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R79-calc-volatile-recalc-5-1: F9 ("Calculate Now") and Ctrl+Alt+F9 ("Calculate Full") must map
/// to distinct KeyboardCommandShortcut values -- Excel gives them different, escalating cost/scope
/// (F9 recalculates only what is dirty; Ctrl+Alt+F9 force-recalculates every formula cell), so
/// they cannot share a single dispatcher route. Before this fix both rules in
/// KeyboardShortcutMatcher.CommandRules.cs targeted KeyboardCommandShortcut.CalculateNow.
/// </summary>
public sealed class R79_F9RecalcScopeShortcutDistinctionTests
{
    [Fact]
    public void PlainF9AndCtrlAltF9_MapToDifferentCommandShortcuts()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(Key.F9, Key.None, ModifierKeys.None, out var plainF9)
            .Should().BeTrue();
        KeyboardShortcutMatcher.TryGetCommandShortcut(Key.F9, Key.None, ModifierKeys.Control | ModifierKeys.Alt, out var ctrlAltF9)
            .Should().BeTrue();

        plainF9.Should().Be(KeyboardCommandShortcut.CalculateNow);
        ctrlAltF9.Should().NotBe(plainF9,
            "F9 and Ctrl+Alt+F9 must have distinct recalc scopes, matching Excel's differing cost/behavior");
        ctrlAltF9.Should().Be(KeyboardCommandShortcut.CalculateFull);
    }

    [Fact]
    public void CtrlAltShiftF9_StillMapsToRebuildDependenciesAndCalculate_DistinctFromBothOtherF9Variants()
    {
        // No-regression sibling: the third F9 variant (rebuild dependency graph + full recalc)
        // must remain distinct from both plain F9 and the new Ctrl+Alt+F9 CalculateFull shortcut.
        KeyboardShortcutMatcher.TryGetCommandShortcut(
                Key.F9, Key.None, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.RebuildDependenciesAndCalculate);
        shortcut.Should().NotBe(KeyboardCommandShortcut.CalculateNow);
        shortcut.Should().NotBe(KeyboardCommandShortcut.CalculateFull);
    }
}
