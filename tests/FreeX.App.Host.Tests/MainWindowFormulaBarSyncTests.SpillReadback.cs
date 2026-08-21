using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowFormulaBarSyncTests
{
    /// <summary>
    /// Seeds a dynamic-array spill the way the recalc engine would after evaluating
    /// <c>=SEQUENCE(<paramref name="count"/>)</c> anchored at (<paramref name="anchorRow"/>,
    /// <paramref name="anchorCol"/>): the anchor cell keeps the formula and its own first value,
    /// while every other member row only exists in the sheet's spill overlay -- <see cref="Sheet.GetCell"/>
    /// returns null for those addresses by design (see its remarks), and only
    /// <see cref="Sheet.GetValue(CellAddress)"/> sees them.
    /// </summary>
    private static void SeedSequenceSpill(Sheet sheet, uint anchorRow, uint anchorCol, int count)
    {
        var anchor = new CellAddress(sheet.Id, anchorRow, anchorCol);
        sheet.SetCell(anchor, Cell.FromFormula($"SEQUENCE({count})"));
        sheet.GetCell(anchor)!.Value = new NumberValue(1);

        var cells = new ScalarValue[count, 1];
        for (var r = 0; r < count; r++)
            cells[r, 0] = new NumberValue(r + 1);
        sheet.SetSpillRange(anchor, new RangeValue(cells));
    }

    [Fact]
    public void EditActiveCellInFormulaBar_OnNonAnchorSpillMember_ShowsSpilledValueInsteadOfBlank()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            SeedSequenceSpill(harness.FirstSheet, 1, 1, 5);

            // Row 3 col 1 is a non-anchor spill member: no entry in Sheet's cell storage, but the
            // grid paints "3" there (via Sheet.GetValue, which does see the spill overlay).
            harness.SelectActiveCell(3, 1);

            harness.EditActiveCellInFormulaBar();

            harness.FormulaBarText.Should().Be("3");
            harness.FormulaBarFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void ShowInlineEditor_OnNonAnchorSpillMember_ShowsSpilledValueInsteadOfBlank()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            SeedSequenceSpill(harness.FirstSheet, 1, 1, 5);

            harness.SelectActiveCell(3, 1);
            harness.ShowInlineEditor(3, 1);

            harness.FormulaBarText.Should().Be("3");
            harness.InlineEditorText.Should().Be("3");
        });
    }

    [Fact]
    public void EditActiveCellInFormulaBar_OnSpillAnchor_StillShowsFormulaNotValue()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            SeedSequenceSpill(harness.FirstSheet, 1, 1, 5);

            // Sibling case: the anchor cell (row 1) DOES have a real Cell with a formula, so the
            // fix for the spill-member blind spot must not affect it -- it should keep showing the
            // formula text, not fall back to the synthesized value-only cell.
            harness.SelectActiveCell(1, 1);

            harness.EditActiveCellInFormulaBar();

            harness.FormulaBarText.Should().Be("=SEQUENCE(5)");
        });
    }

    [Fact]
    public void EditActiveCellInFormulaBar_OnGenuinelyBlankCell_StillShowsEmptyFormulaBar()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            SeedSequenceSpill(harness.FirstSheet, 1, 1, 5);

            // Sibling case: an ordinary blank cell (no formula, no spill overlay entry either)
            // must keep showing an empty formula bar -- the synthesized fallback cell wraps
            // BlankValue for this address, which formats identically to the null it replaces.
            harness.SelectActiveCell(9, 9);

            harness.EditActiveCellInFormulaBar();

            harness.FormulaBarText.Should().BeEmpty();
        });
    }
}
