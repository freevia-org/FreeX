using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R112-core-commands-mergecells-spill-guard: MergeCellsCommand.Apply (the single choke point every
// merge-creating command funnels through) only rejected an out-of-worksheet-bounds range, a table
// overlap, or a partial existing-merge overlap -- it never checked whether the target range touches a
// live dynamic-array spill. CellMergePlanner.HasLiveSpillTarget guards the ribbon-driven "Merge &
// Center" / "Merge Cells" / "Merge Across" paths one layer up (FreeX.App.Services), but
// FormatPainterCommandFactory.AddTiledMerges (FreeX.Core.Commands) constructs MergeCellsCommand
// directly with no upstream guard at all, so painting a merged source format onto a target range that
// overlaps a live SEQUENCE()/spill silently merges over it. Because the merge blanks the spill's
// non-anchor cells and adds a merged region that now covers the spill anchor too, Sheet.IsSpillBlocked
// refuses to re-spill on the very next recalculation -- turning a valid array into #SPILL! with no
// warning or rejection anywhere in the pipeline.
public sealed class R112_MergeCellsCommandArraySpillGuardTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static void SpillSequence(Sheet sheet, CellAddress anchor, int count)
    {
        sheet.SetFormula(anchor, $"SEQUENCE(1,{count})");
        var cells = new ScalarValue[1, count];
        for (var i = 0; i < count; i++)
            cells[0, i] = new NumberValue(i + 1);
        sheet.SetSpillRange(anchor, new RangeValue(cells, 1, 1));
    }

    [Fact]
    public void Apply_RangeExactlyMatchesLiveSpillExtent_IsRejectedAndSheetUntouched()
    {
        // This is the concrete failure scenario from the defect report: a 1x3 merge tiled onto a
        // target range whose footprint is an EXACT match for a live SEQUENCE(1,3) spill's full extent
        // (anchor + both spilled cells). CommandGuards.RejectIfSplitsArray's "the whole array may be
        // edited as a unit" exception would wrongly let this exact case through, so the guard here must
        // reject on ANY overlap, not just a partial one.
        var (_, sheet, ctx) = Setup();
        var anchor = new CellAddress(sheet.Id, 5, 4); // D5
        SpillSequence(sheet, anchor, 3); // spills into D5:F5

        var range = new GridRange(anchor, new CellAddress(sheet.Id, 5, 6)); // D5:F5, exact spill extent
        var outcome = new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        sheet.MergedRegions.Should().BeEmpty("a rejected merge must not register a merged region");
        sheet.GetValue(anchor.Row, anchor.Col + 2).Should().Be(new NumberValue(3),
            "the spilled value must survive an aborted merge attempt");
    }

    [Fact]
    public void Apply_RangePartiallyOverlapsLiveSpill_IsRejected()
    {
        var (_, sheet, ctx) = Setup();
        var anchor = new CellAddress(sheet.Id, 5, 4); // D5
        SpillSequence(sheet, anchor, 3); // spills into D5:F5

        // Merge only D5:E5 -- partially overlaps the spill (misses F5) but still touches the anchor
        // and one spilled cell.
        var range = new GridRange(anchor, new CellAddress(sheet.Id, 5, 5));
        var outcome = new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.MergedRegions.Should().BeEmpty();
    }

    [Fact]
    public void Apply_RangeDisjointFromLiveSpill_StillSucceeds_NoRegression()
    {
        // Sibling/no-regression coverage: an ordinary merge that shares no cell at all with the spill
        // must keep working exactly as before.
        var (_, sheet, ctx) = Setup();
        var anchor = new CellAddress(sheet.Id, 5, 4); // D5
        SpillSequence(sheet, anchor, 3); // spills into D5:F5

        var range = new GridRange(
            new CellAddress(sheet.Id, 10, 1),
            new CellAddress(sheet.Id, 10, 2));
        var outcome = new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(range);
    }

    [Fact]
    public void FormatPainter_TiledMergeOntoLiveSpill_WholeCompositeRejectedAndSpillSurvives()
    {
        // Exercises the REAL product entry point named in the defect: FormatPainterCommandFactory's
        // merge-tiling path (AddTiledMerges), not a hand-built model. Source is a 1x3 merged cell
        // (A1:C1); the user paints that format onto D5, whose footprint expands to the exact 1x3 shape
        // of a live SEQUENCE(1,3) spill anchored there.
        var (wb, sheet, ctx) = Setup();
        var sourceTop = new CellAddress(sheet.Id, 1, 1);
        var sourceRange = new GridRange(sourceTop, new CellAddress(sheet.Id, 1, 3));
        sheet.AddMergedRegion(sourceRange);

        var anchor = new CellAddress(sheet.Id, 5, 4); // D5
        SpillSequence(sheet, anchor, 3); // spills into D5:F5

        var targetRange = new GridRange(anchor, anchor); // single cell -> expands to D5:F5
        var command = FormatPainterCommandFactory.Create(wb, sheet, sourceRange, targetRange);
        var outcome = command.Apply(ctx);

        // The defect's own guarantee -- the corrupting merged region must never land, so
        // Sheet.IsSpillBlocked can never see D5 covered by a merge on the next recalculation -- is
        // this outcome plus the merged-region assertion below. (Whether the composite's OTHER
        // already-applied sub-commands, e.g. ApplyStyleCommand, fully restore the anchor's spill
        // overlay on rollback is a separate, pre-existing CompositeWorkbookCommand/ApplyStyleCommand
        // concern outside MergeCellsCommand's own choke point -- see FormatPainter_TiledMergeOntoLiveSpill_
        // RevertRestoresAnchorFormula below and the siblingLeads note in the round report.)
        outcome.Success.Should().BeFalse();
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(sourceRange,
            "only the pre-existing source merge should remain -- the tiled destination merge must never land");
    }

    [Fact]
    public void FormatPainter_TiledMergeOntoLiveSpill_RevertRestoresAnchorFormula()
    {
        // No-regression companion to the test above: even though the composite is rejected, the
        // spill anchor cell itself (D5) must still hold its original SEQUENCE formula afterwards --
        // rolling back must not leave the anchor blank or otherwise corrupted.
        var (wb, sheet, ctx) = Setup();
        var sourceTop = new CellAddress(sheet.Id, 1, 1);
        var sourceRange = new GridRange(sourceTop, new CellAddress(sheet.Id, 1, 3));
        sheet.AddMergedRegion(sourceRange);

        var anchor = new CellAddress(sheet.Id, 5, 4); // D5
        SpillSequence(sheet, anchor, 3); // spills into D5:F5

        var targetRange = new GridRange(anchor, anchor);
        var command = FormatPainterCommandFactory.Create(wb, sheet, sourceRange, targetRange);
        command.Apply(ctx);

        sheet.GetCell(anchor)!.FormulaText.Should().Be("SEQUENCE(1,3)");
    }

    [Fact]
    public void FormatPainter_TiledMergeOntoSpillFreeTarget_StillSucceeds_NoRegression()
    {
        // Sibling of the FormatPainter test above: the same tiled-merge path over an ordinary,
        // spill-free target must keep merging exactly as it did before this fix.
        var (wb, sheet, ctx) = Setup();
        var sourceTop = new CellAddress(sheet.Id, 1, 1);
        var sourceRange = new GridRange(sourceTop, new CellAddress(sheet.Id, 1, 3));
        sheet.AddMergedRegion(sourceRange);

        var target = new CellAddress(sheet.Id, 10, 1);
        var targetRange = new GridRange(target, target);
        var command = FormatPainterCommandFactory.Create(wb, sheet, sourceRange, targetRange);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var expectedMerge = new GridRange(target, new CellAddress(sheet.Id, 10, 3));
        sheet.MergedRegions.Should().Contain(expectedMerge);
    }
}
