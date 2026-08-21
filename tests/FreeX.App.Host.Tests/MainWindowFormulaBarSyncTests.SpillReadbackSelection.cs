using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowFormulaBarSyncTests
{
    /// <summary>
    /// R162-formulabar-spill-readback-selection-gesture: the plain-click selection gesture
    /// (<c>SheetGrid_MouseDown</c> -&gt; <c>SetActiveCell</c>, exercised here via
    /// <see cref="MainWindowHarness.SelectActiveCell"/>, which forwards straight to
    /// <c>SetActiveCellForTest</c>/<c>SetActiveCell</c> with no intervening edit-start call) must
    /// show the spilled value for a non-anchor dynamic-array spill member, exactly like
    /// <c>EditActiveCellInFormulaBar</c>/<c>ShowInlineEditor</c> already do (see
    /// MainWindowFormulaBarSyncTests.SpillReadback.cs). Before R162's Selection.cs fix,
    /// <c>SetActiveCell</c> read the cell via a raw <c>Sheet.GetCell</c>, which returns null for a
    /// spill member (its value lives only in the spill overlay -- see <c>Sheet.GetCell</c>'s
    /// remarks), so simply clicking such a cell left the formula bar blank even though the grid
    /// visibly paints a value into it. The earlier SpillReadback tests all call a second method
    /// (EditActiveCellInFormulaBar / ShowInlineEditor) after selecting, and it was THAT second call
    /// which supplied the correct text -- so they could not, and did not, catch this gap. This test
    /// asserts the formula bar text immediately after the selection call alone.
    /// </summary>
    [Fact]
    public void SelectActiveCell_OnNonAnchorSpillMember_ShowsSpilledValueInsteadOfBlank()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            SeedSequenceSpill(harness.FirstSheet, 1, 1, 5);

            // Row 3 col 1 is a non-anchor spill member: no entry in Sheet's cell storage, but the
            // grid paints "3" there (via Sheet.GetValue, which does see the spill overlay). This is
            // exactly the gesture a plain mouse click drives -- SheetGrid_MouseDown calls
            // SetActiveCell directly, with no formula-bar-focus/edit-start step in between.
            harness.SelectActiveCell(3, 1);

            harness.FormulaBarText.Should().Be("3");
        });
    }

    /// <summary>
    /// Sibling no-regression case for the fix above: selecting the spill ANCHOR (which has a real
    /// <see cref="Cell"/> with a formula) through the same plain-click gesture must keep showing the
    /// formula text, not fall back to a synthesized value-only cell.
    /// </summary>
    [Fact]
    public void SelectActiveCell_OnSpillAnchor_StillShowsFormulaNotValue()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            SeedSequenceSpill(harness.FirstSheet, 1, 1, 5);

            harness.SelectActiveCell(1, 1);

            harness.FormulaBarText.Should().Be("=SEQUENCE(5)");
        });
    }

    /// <summary>
    /// Sibling no-regression case: selecting a genuinely blank cell (no formula, no spill overlay
    /// entry either) through the same plain-click gesture must keep showing an empty formula bar.
    /// </summary>
    [Fact]
    public void SelectActiveCell_OnGenuinelyBlankCell_StillShowsEmptyFormulaBar()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            SeedSequenceSpill(harness.FirstSheet, 1, 1, 5);

            harness.SelectActiveCell(9, 9);

            harness.FormulaBarText.Should().BeEmpty();
        });
    }
}
