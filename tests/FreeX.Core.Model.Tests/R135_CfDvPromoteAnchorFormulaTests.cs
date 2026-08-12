using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R135-commands-cf-dv-promote-anchor-1 (RowColumnShiftHelpers.Rules.cs): when a multi-area CF/DV
/// rule's primary <c>AppliesTo</c> area is fully consumed by a row/column delete and
/// PromoteCfSurvivorOrRemove/PromoteDvSurvivorOrRemove promote a surviving
/// <c>AdditionalRanges</c> entry to become the new primary area, the rule's relative-reference
/// formula (CF FormulaText/thresholds, DV Formula1/Formula2) must be re-anchored by the same delta
/// the AppliesTo anchor moved by. Evaluation always shifts a rule's relative references by
/// (targetCell - AppliesTo.Start) -- see ViewportService.ConditionalFormatFormulas.cs,
/// ViewportConditionalFormatEvaluator.Thresholds.cs, and DataValidationService.cs (TryParseNumberBound
/// / ResolveListValues) -- so promoting AppliesTo without a compensating rewrite silently changes
/// which cells every relative reference resolves to.
/// <para>
/// Covers all four sibling promotion call sites: ShiftRuleRowsDown (DeleteRowsCommand),
/// ShiftRuleColumnsDown (DeleteColumnsCommand), AdjustRulesDeleteShiftUp and
/// AdjustRulesDeleteShiftLeft (DeleteCellsCommand, band-scoped Delete-Cells paths).
/// </para>
/// </summary>
public sealed class R135_CfDvPromoteAnchorFormulaTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static GridRange Range(SheetId id, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(id, r1, c1), new CellAddress(id, r2, c2));

    // ══════════════════════════════════════════════════════════════════════════
    // ShiftRuleRowsDown (DeleteRowsCommand) — CF FormulaText
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteRows_PromotedCf_FormulaText_ReAnchoredByPromotionDelta()
    {
        // Primary A10:A12, additional C1:C3 (entirely above the deleted band, so it survives
        // UNSHIFTED and is promoted verbatim). FormulaText 'B15>10' is relative to the OLD anchor
        // A10 (row+5, col+1). Deleting rows 10-12 fully consumes the primary, promoting C1:C3
        // (anchor C1) to AppliesTo. The formula must be re-anchored by the anchor delta
        // (row -9, col +2): B15 -> D6, so the rule still means "5 rows down, 1 col right of the
        // evaluated cell" exactly as it did before the promotion.
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 10, 1, 12, 1),          // A10:A12
            AdditionalRanges = [Range(sheet.Id, 1, 3, 3, 3)],   // C1:C3
            RuleType = CfRuleType.Formula,
            FormulaText = "B15>10"
        };
        sheet.ConditionalFormats.Add(cf);

        new DeleteRowsCommand(sheet.Id, startRow: 10, count: 3).Apply(ctx).Success.Should().BeTrue();

        sheet.ConditionalFormats.Should().ContainSingle(because: "C1:C3 was not consumed by the delete and survives via promotion");
        var survivor = sheet.ConditionalFormats[0];
        survivor.AppliesTo.Should().Be(Range(sheet.Id, 1, 3, 3, 3), because: "C1:C3 is entirely above the deleted band and promotes unshifted");
        survivor.FormulaText.Should().Be("D6>10",
            because: "the anchor moved from A10 to C1 (row -9, col +2); B15 must be re-anchored to D6 to keep referencing the same relative cell");
    }

    [Fact]
    public void DeleteRows_PromotedCf_FormulaText_Undo_RestoresOriginalFormula()
    {
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 10, 1, 12, 1),
            AdditionalRanges = [Range(sheet.Id, 1, 3, 3, 3)],
            RuleType = CfRuleType.Formula,
            FormulaText = "B15>10"
        };
        sheet.ConditionalFormats.Add(cf);

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 10, count: 3);
        cmd.Apply(ctx).Success.Should().BeTrue();
        cmd.Revert(ctx);

        sheet.ConditionalFormats.Should().ContainSingle();
        var restored = sheet.ConditionalFormats[0];
        restored.AppliesTo.Should().Be(Range(sheet.Id, 10, 1, 12, 1), because: "undo restores the original primary area");
        restored.FormulaText.Should().Be("B15>10", because: "undo must restore the original, un-re-anchored formula text");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ShiftRuleColumnsDown (DeleteColumnsCommand) — CF FormulaText
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteColumns_PromotedCf_FormulaText_ReAnchoredByPromotionDelta()
    {
        // Primary J1:L1 (cols 10-12), additional A1:A3 (entirely left of the deleted band, so it
        // survives unshifted). FormulaText 'N5>10' relative to old anchor J1 (col N=14, row+4,
        // col+4). Deleting columns 10-12 promotes A1:A3 (anchor A1); the anchor delta is
        // (row 0, col -9), so N5 -> E5.
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 1, 10, 1, 12),          // J1:L1
            AdditionalRanges = [Range(sheet.Id, 1, 1, 3, 1)],   // A1:A3
            RuleType = CfRuleType.Formula,
            FormulaText = "N5>10"
        };
        sheet.ConditionalFormats.Add(cf);

        new DeleteColumnsCommand(sheet.Id, startCol: 10, count: 3).Apply(ctx).Success.Should().BeTrue();

        sheet.ConditionalFormats.Should().ContainSingle(because: "A1:A3 was not consumed by the delete and survives via promotion");
        var survivor = sheet.ConditionalFormats[0];
        survivor.AppliesTo.Should().Be(Range(sheet.Id, 1, 1, 3, 1), because: "A1:A3 is entirely left of the deleted band and promotes unshifted");
        survivor.FormulaText.Should().Be("E5>10",
            because: "the anchor moved from J1 to A1 (col -9); N5 must be re-anchored to E5 to keep referencing the same relative cell");
    }

    [Fact]
    public void DeleteColumns_PromotedCf_FormulaText_Undo_RestoresOriginalFormula()
    {
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 1, 10, 1, 12),
            AdditionalRanges = [Range(sheet.Id, 1, 1, 3, 1)],
            RuleType = CfRuleType.Formula,
            FormulaText = "N5>10"
        };
        sheet.ConditionalFormats.Add(cf);

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 10, count: 3);
        cmd.Apply(ctx).Success.Should().BeTrue();
        cmd.Revert(ctx);

        sheet.ConditionalFormats.Should().ContainSingle();
        var restored = sheet.ConditionalFormats[0];
        restored.AppliesTo.Should().Be(Range(sheet.Id, 1, 10, 1, 12), because: "undo restores the original primary area");
        restored.FormulaText.Should().Be("N5>10", because: "undo must restore the original, un-re-anchored formula text");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AdjustRulesDeleteShiftUp (DeleteCellsCommand, Shift-Up) — DV Formula1
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteCellsShiftUp_PromotedDv_Formula1_ReAnchoredByPromotionDelta()
    {
        // Primary A1:A5, additional A20:A25 (col1, entirely below the deleted rows -- shifts up 5
        // to A15:A20 and is promoted). Formula1 'B1' is relative to old anchor A1 (col+1, row+0).
        // The anchor moves from A1 to A15 (row +14, col 0), so B1 -> B15.
        var (_, sheet, ctx) = Setup();
        var dv = new DataValidation
        {
            AppliesTo = Range(sheet.Id, 1, 1, 5, 1),   // A1:A5
            Type = DvType.Custom,
            Formula1 = "B1"
        };
        dv.AdditionalRanges.Add(Range(sheet.Id, 20, 1, 25, 1)); // A20:A25
        sheet.DataValidations.Add(dv);

        var cmd = new DeleteCellsCommand(sheet.Id, Range(sheet.Id, 1, 1, 5, 1), DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().ContainSingle(because: "A20:A25 was not consumed by the delete and survives via promotion");
        var survivor = sheet.DataValidations[0];
        survivor.AppliesTo.Should().Be(Range(sheet.Id, 15, 1, 20, 1), because: "A20:A25 shifts up 5 to A15:A20 and is promoted to AppliesTo");
        survivor.Formula1.Should().Be("B15",
            because: "the anchor moved from A1 to A15 (row +14); B1 must be re-anchored to B15 to keep referencing the same relative cell");
    }

    [Fact]
    public void DeleteCellsShiftUp_PromotedDv_Formula1_Undo_RestoresOriginalFormula()
    {
        var (_, sheet, ctx) = Setup();
        var dv = new DataValidation
        {
            AppliesTo = Range(sheet.Id, 1, 1, 5, 1),
            Type = DvType.Custom,
            Formula1 = "B1"
        };
        dv.AdditionalRanges.Add(Range(sheet.Id, 20, 1, 25, 1));
        sheet.DataValidations.Add(dv);

        var cmd = new DeleteCellsCommand(sheet.Id, Range(sheet.Id, 1, 1, 5, 1), DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();
        cmd.Revert(ctx);

        sheet.DataValidations.Should().ContainSingle();
        var restored = sheet.DataValidations[0];
        restored.AppliesTo.Should().Be(Range(sheet.Id, 1, 1, 5, 1), because: "undo restores the original primary area");
        restored.Formula1.Should().Be("B1", because: "undo must restore the original, un-re-anchored Formula1");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AdjustRulesDeleteShiftLeft (DeleteCellsCommand, Shift-Left) — CF colorScale threshold
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteCellsShiftLeft_PromotedCf_ColorScaleMinThreshold_ReAnchoredByPromotionDelta()
    {
        // Primary A1:E1 (row1, cols 1-5), additional row1 cols 20-25 (entirely right of the
        // deleted band -- shifts left 5 to cols 15-20 and is promoted). The colorScale Min
        // threshold's Formula value 'B50' targets row 50 (outside the band row, so the later
        // structural Delete-Cells rewrite pass leaves it untouched -- isolating the anchor-delta
        // rewrite alone). The anchor moves from col1 to col15 (col +14, row 0), so B50 -> P50.
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 1, 1, 1, 5),             // A1:E1
            AdditionalRanges = [Range(sheet.Id, 1, 20, 1, 25)],  // T1:Y1
            RuleType = CfRuleType.ColorScale,
            MinThresholdType = CfThresholdType.Formula,
            MinThresholdValue = "B50"
        };
        sheet.ConditionalFormats.Add(cf);

        var cmd = new DeleteCellsCommand(sheet.Id, Range(sheet.Id, 1, 1, 1, 5), DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.ConditionalFormats.Should().ContainSingle(because: "the additional area was not consumed by the delete and survives via promotion");
        var survivor = sheet.ConditionalFormats[0];
        survivor.AppliesTo.Should().Be(Range(sheet.Id, 1, 15, 1, 20), because: "cols 20-25 shift left 5 to cols 15-20 and are promoted to AppliesTo");
        survivor.MinThresholdValue.Should().Be("P50",
            because: "the anchor moved from col1 to col15 (col +14); B50 must be re-anchored to P50 to keep referencing the same relative cell");
    }

    [Fact]
    public void DeleteCellsShiftLeft_PromotedCf_ColorScaleMinThreshold_Undo_RestoresOriginalThreshold()
    {
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 1, 1, 1, 5),
            AdditionalRanges = [Range(sheet.Id, 1, 20, 1, 25)],
            RuleType = CfRuleType.ColorScale,
            MinThresholdType = CfThresholdType.Formula,
            MinThresholdValue = "B50"
        };
        sheet.ConditionalFormats.Add(cf);

        var cmd = new DeleteCellsCommand(sheet.Id, Range(sheet.Id, 1, 1, 1, 5), DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();
        cmd.Revert(ctx);

        sheet.ConditionalFormats.Should().ContainSingle();
        var restored = sheet.ConditionalFormats[0];
        restored.AppliesTo.Should().Be(Range(sheet.Id, 1, 1, 1, 5), because: "undo restores the original primary area");
        restored.MinThresholdValue.Should().Be("B50", because: "undo must restore the original, un-re-anchored threshold formula");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Sibling no-regression: a rule that is merely SHIFTED (not promoted) in the same
    // operation must keep following the pre-existing DeleteRowsOp-driven rewrite path only,
    // proving the new promotion-anchor snapshot dictionary does not cross-contaminate it.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteRows_PromotedAndOrdinarilyShiftedCf_BothRewrittenIndependently_NoCrossContamination()
    {
        var (_, sheet, ctx) = Setup();

        // Rule 1: promoted (primary fully consumed, additional survives above the band).
        var promoted = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 10, 1, 12, 1),          // A10:A12
            AdditionalRanges = [Range(sheet.Id, 1, 3, 3, 3)],   // C1:C3
            RuleType = CfRuleType.Formula,
            FormulaText = "B15>10"
        };
        sheet.ConditionalFormats.Add(promoted);

        // Rule 2: entirely below the deleted band -- ordinary whole-rule shift, no promotion.
        // Mirrors RuleFormulaShiftTests.DeleteRows_ShiftsCFFormulaTextUp's existing coverage.
        var shifted = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 20, 1, 25, 1),          // A20:A25
            RuleType = CfRuleType.Formula,
            FormulaText = "$A20>0"
        };
        sheet.ConditionalFormats.Add(shifted);

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 10, count: 3);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.ConditionalFormats.Should().HaveCount(2);
        var promotedAfter = sheet.ConditionalFormats.Single(r => ReferenceEquals(r, promoted));
        var shiftedAfter = sheet.ConditionalFormats.Single(r => ReferenceEquals(r, shifted));

        promotedAfter.AppliesTo.Should().Be(Range(sheet.Id, 1, 3, 3, 3));
        promotedAfter.FormulaText.Should().Be("D6>10", because: "promotion anchor-delta rewrite still applies correctly alongside another rule in the same operation");

        shiftedAfter.AppliesTo.Should().Be(Range(sheet.Id, 17, 1, 22, 1), because: "A20:A25 shifts up 3 (rows 10-12 deleted) to A17:A22");
        shiftedAfter.FormulaText.Should().Be("$A17>0", because: "the ordinary (non-promoted) rule follows only the pre-existing DeleteRowsOp-driven rewrite, unaffected by the new promotion snapshot");

        cmd.Revert(ctx);

        sheet.ConditionalFormats.Should().HaveCount(2);
        var promotedRestored = sheet.ConditionalFormats.Single(r => ReferenceEquals(r, promoted));
        var shiftedRestored = sheet.ConditionalFormats.Single(r => ReferenceEquals(r, shifted));
        promotedRestored.AppliesTo.Should().Be(Range(sheet.Id, 10, 1, 12, 1));
        promotedRestored.FormulaText.Should().Be("B15>10");
        shiftedRestored.AppliesTo.Should().Be(Range(sheet.Id, 20, 1, 25, 1));
        shiftedRestored.FormulaText.Should().Be("$A20>0");
    }
}
