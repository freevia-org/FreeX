using System.Reflection;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for round-154 remediation finding M3
/// (src/FreeX.App.Avalonia/MainWindow.cs, FormulaBox_KeyDown): the window-level Escape guard
/// added for shared-keyboard-customization F1 (ShouldHandleEscapeLocallyBeforeFormulaPointMode in
/// src/FreeX.App.Avalonia/MainWindow.KeyboardParity.cs, applied to MainWindow_KeyDownAsync) was
/// not mirrored onto FormulaBox_KeyDown -- the identical, formula-bar-focused entry point that calls
/// the same unguarded TryRouteFormulaPointModeKey. A workbook window ("source") with its OWN F8
/// sticky-selection mode armed must claim Escape locally instead of letting it fall through to
/// TryRouteFormulaPointModeKey, which -- when source has no formula edit of its own -- routes the
/// Cancel to whichever OTHER open workbook window ("owner") has a live formula point-mode edit and
/// silently discards it. See R90_CrossWorkbookFormulaPointModeAvaloniaTests for the deliberate
/// cross-window routing contract this fix must NOT disturb.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R154_M3_FormulaBoxEscapeLocalBeforeFormulaPointModeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Escape_WithF8StickySelectionArmedInOtherWindow_ClaimsEscapeLocally_LeavesOtherWindowsFormulaEditIntact()
    {
        await Session.Dispatch(() =>
        {
            var owner = new MainWindow([]);
            var source = new MainWindow([]);
            try
            {
                owner.Session.Workbook.Name = "Owner.xlsx";
                source.Session.Workbook.Name = "Source.xlsx";
                owner.Session.ActiveSheet.Name = "Owner";
                source.Session.ActiveSheet.Name = "Input Data";
                owner.Show();
                source.Show();
                var formulaCell = new CellAddress(owner.Session.ActiveSheet.Id, 8, 7);

                owner.Session.SelectCell(formulaCell);
                owner.BeginFormulaPointModeEditForTest(formulaCell, "=SUM(");
                owner.HasActiveFormulaPointMode.Should().BeTrue(
                    "the failure scenario requires window A's formula edit to be live before Escape");

                ArmF8ExtendMode(source);
                source.KeyboardSelectionModeForTest.Should().Be(ExcelSelectionMode.Extend,
                    "the failure scenario requires window B's OWN F8 sticky-selection mode to be armed before Escape");

                source.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Escape });

                owner.HasActiveFormulaPointMode.Should().BeTrue(
                    "window A's in-progress formula edit must survive an Escape that window B's own F8 mode claimed locally");
                owner.FormulaBoxTextForTest.Should().Be("=SUM(",
                    "window A's formula text must not be reverted by an Escape meant for window B's own local state");
            }
            finally
            {
                source.AllowCloseWithoutDirtyPromptForParityCapture();
                source.Close();

                owner.AllowCloseWithoutDirtyPromptForParityCapture();
                owner.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Escape_WithNoLocalUiStateInOtherWindow_StillRoutesAndCancelsOtherWindowsFormulaEdit()
    {
        // Sibling/no-regression guard: when window B has no local UI state of its own claiming
        // Escape (F8 sticky-selection mode is Normal), the ordinary cross-window point-mode Cancel
        // routing exercised by R90_CrossWorkbookFormulaPointModeAvaloniaTests must still fire
        // exactly as before -- this is the legitimate "pointed into another open workbook, then
        // cancelled" gesture and must not be disabled by the fix above.
        await Session.Dispatch(() =>
        {
            var owner = new MainWindow([]);
            var source = new MainWindow([]);
            try
            {
                owner.Session.Workbook.Name = "Owner.xlsx";
                source.Session.Workbook.Name = "Source.xlsx";
                owner.Show();
                source.Show();
                var formulaCell = new CellAddress(owner.Session.ActiveSheet.Id, 8, 7);

                owner.Session.SelectCell(formulaCell);
                owner.BeginFormulaPointModeEditForTest(formulaCell, "=SUM(");
                source.KeyboardSelectionModeForTest.Should().Be(ExcelSelectionMode.Normal);

                source.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Escape });

                owner.HasActiveFormulaPointMode.Should().BeFalse(
                    "with no local UI state active in window B, Escape must still route to and cancel window A's formula edit");
            }
            finally
            {
                source.AllowCloseWithoutDirtyPromptForParityCapture();
                source.Close();

                owner.AllowCloseWithoutDirtyPromptForParityCapture();
                owner.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    /// <summary>Arms F8 (Extend Selection) keyboard selection mode through the real toggle method.</summary>
    private static void ArmF8ExtendMode(MainWindow window)
    {
        var toggleMethod = typeof(MainWindow).GetMethod(
            "ToggleKeyboardSelectionMode", BindingFlags.Instance | BindingFlags.NonPublic)!;
        toggleMethod.Invoke(window, [ExcelWorksheetNavigationModifiers.None]);
    }
}
