using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for freex-protection F2: a Ctrl+click "add disjoint selection area" never
/// checked the sheet's "Select locked cells" permission, even though the identical cell would be
/// refused by a plain click via <c>CanSelectCellForClick</c>
/// (<see cref="FreeX.Core.Commands.CommandGuards.CanSelectCell"/>).
///
/// Before this fix, <c>SheetGrid_MouseDown</c>'s Ctrl+click branch (MainWindow.Selection.cs) went
/// straight from the hyperlink check to <c>AddOrMoveAdditionalSelection</c>, so a locked cell on a
/// protected sheet with "Select locked cells" unchecked was silently added as a new disjoint
/// selection area -- the same cell would be refused outright by a plain click just a few lines
/// below in the same method.
///
/// These are source-contract tests, not a driven <c>SheetGrid_MouseDown</c> call, because WPF
/// resolves <c>Keyboard.Modifiers</c> from the real (global, async) Win32 key state rather than
/// from any constructed <see cref="System.Windows.Input.MouseButtonEventArgs"/>
/// (see R62_NameBoxStructuredTableTests.PressEnter's docstring for the established precedent), so
/// there is no reliable way to drive the Ctrl+click branch itself in a headless test. The
/// functional behavior of the shared <c>CanSelectCellForClick</c> gate it now also uses is already
/// covered by R75_ProtectionSelectionNavigationTests.
/// </summary>
public sealed class FreeXProtectionF2_CtrlClickAdditionalSelectionGateTests
{
    [Fact]
    public void CtrlClickBranch_ChecksCanSelectCellForClick_BeforeAddingAdditionalSelection()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var mouseDownStart = selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal);
        var textInputStart = selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal);
        var mouseDown = selectionSource[mouseDownStart..textInputStart];

        var ctrlBranchStart = mouseDown.IndexOf(
            "else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)", StringComparison.Ordinal);
        // The plain-click else branch's own doc comment is a unique, whitespace-stable anchor for
        // where the Ctrl+click branch ends (avoids depending on exact brace/newline formatting).
        var plainBranchStart = mouseDown.IndexOf(
            "// A plain click onto a locked cell on a protected sheet with \"Select locked cells\"",
            StringComparison.Ordinal);
        ctrlBranchStart.Should().BeGreaterThanOrEqualTo(0, "the Ctrl+click branch must still exist");
        plainBranchStart.Should().BeGreaterThan(ctrlBranchStart, "the plain-click else branch must follow the Ctrl+click branch");

        var ctrlBranch = mouseDown[ctrlBranchStart..plainBranchStart];

        // The three fixed points of the branch, in order: open a hyperlink if present, THEN
        // refuse a locked cell exactly like a plain click would, THEN (only if still here) add
        // the disjoint selection area.
        var hyperlinkIdx = ctrlBranch.IndexOf("if (TryOpenHyperlink(newAddr))", StringComparison.Ordinal);
        var gateIdx = ctrlBranch.IndexOf("if (!CanSelectCellForClick(newAddr))", StringComparison.Ordinal);
        var addIdx = ctrlBranch.IndexOf("AddOrMoveAdditionalSelection(newAddr, extendSelection: false);", StringComparison.Ordinal);

        hyperlinkIdx.Should().BeGreaterThanOrEqualTo(0, "Ctrl+click must still be able to open a hyperlink first");
        gateIdx.Should().BeGreaterThan(hyperlinkIdx,
            "the protection gate must run after the hyperlink check (freex-protection F2)");
        addIdx.Should().BeGreaterThan(gateIdx,
            "AddOrMoveAdditionalSelection must only run once the protection gate has passed (freex-protection F2)");

        // The gate must actually refuse the click (mirroring the plain-click branch a few lines
        // below) rather than merely being present but inert.
        var gateBlock = ctrlBranch[gateIdx..addIdx];
        gateBlock.Should().Contain("e.Handled = true;");
        gateBlock.Should().Contain("return;");
    }

    [Fact]
    public void PlainClickBranch_StillChecksCanSelectCellForClick_NoRegression()
    {
        // Sibling/no-regression: the pre-existing plain-click gate (R75-services-protection-
        // security-4-1) must be untouched by wiring the same helper into the Ctrl+click branch.
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var mouseDownStart = selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal);
        var textInputStart = selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal);
        var mouseDown = selectionSource[mouseDownStart..textInputStart];

        // Bound strictly to the plain-click else branch (from its doc comment to the method's own
        // closing brace) so this does not accidentally match the unrelated
        // TryHandleCellAreaExtendClick helper (defined later in the same file slice), which has an
        // identical-looking "if (!CanSelectCellForClick(newAddr))" guard of its own.
        var plainBranchStart = mouseDown.IndexOf(
            "// A plain click onto a locked cell on a protected sheet with \"Select locked cells\"",
            StringComparison.Ordinal);
        plainBranchStart.Should().BeGreaterThanOrEqualTo(0);
        var plainBranch = mouseDown[plainBranchStart..];

        var setActiveCellIdx = plainBranch.IndexOf("SetActiveCell(newAddr);", StringComparison.Ordinal);
        var gateIdx = plainBranch.IndexOf("if (!CanSelectCellForClick(newAddr))", StringComparison.Ordinal);

        gateIdx.Should().BeGreaterThanOrEqualTo(0);
        setActiveCellIdx.Should().BeGreaterThan(gateIdx,
            "the plain click's active-cell move must still happen only after its own protection gate");
    }
}
