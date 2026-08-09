using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for round-131 finding "Data-validation bound formula re-anchors against the
/// wrong overlapping rule": DataValidationBoundsParser.TryEvaluateBoundFormula used to recover
/// the rule's anchor cell by scanning sheet.DataValidations for a rule whose AppliesTo covers the
/// cell being validated AND whose Formula1/Formula2 text matches (FindRuleAnchor). When two rules
/// overlap the same cell and happen to share identical bound text, that text-match search can
/// return the FIRST matching rule's anchor rather than the anchor of the specific rule actually
/// being validated (the <c>dv</c> parameter DataValidationService.Validate was called with),
/// shifting the relative bound formula from the wrong origin cell.
///
/// The fix threads the actual rule's own AppliesTo.Start straight through as the anchor instead
/// of rediscovering it by text match.
/// </summary>
public class R131_DvOverlappingRuleAnchorTests
{
    private static (Workbook workbook, Sheet sheet) MakeSheetWithAnchors()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10)); // A1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20)); // A2
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(5));  // A3

        return (workbook, sheet);
    }

    /// <summary>
    /// RuleA (added first, so it's found first by any text-matching scan) applies to B1:B3
    /// (anchor B1) with bound "=A1". RuleC (added second) applies to just B3 (anchor B3) with the
    /// SAME bound text "=A1", overlapping RuleA at cell B3. A wrong (text-match) anchor lookup at
    /// B3 finds RuleA first and shifts "=A1" from B1 to B3 (+2 rows) onto A3 (=5) instead of
    /// RuleC's own no-shift bound A1 (=10).
    /// </summary>
    private static (DataValidation ruleA, DataValidation ruleC) MakeOverlappingRulesSharingBoundText(
        Sheet sheet, DvOperator op)
    {
        var ruleA = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = op,
            Formula1 = "=A1",
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 2),
                new CellAddress(sheet.Id, 3, 2)), // B1:B3, anchor B1
        };

        var ruleC = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = op,
            Formula1 = "=A1",
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 3, 2),
                new CellAddress(sheet.Id, 3, 2)), // B3 only, anchor B3
        };

        // Order matters: ruleA must be added first so a text-matching anchor scan of
        // sheet.DataValidations encounters it before ruleC.
        sheet.DataValidations.Add(ruleA);
        sheet.DataValidations.Add(ruleC);

        return (ruleA, ruleC);
    }

    [Fact]
    public void Validate_OverlappingRuleSharingBoundText_AcceptsValueThatOnlyPassesItsOwnAnchorBound()
    {
        var (workbook, sheet) = MakeSheetWithAnchors();
        var (_, ruleC) = MakeOverlappingRulesSharingBoundText(sheet, DvOperator.LessThanOrEqual);

        var b3 = new CellAddress(sheet.Id, 3, 2);

        // RuleC's own anchor IS B3 (its AppliesTo is exactly B3:B3), so at cell B3 the bound
        // formula "=A1" needs no shift at all: bound = A1 = 10. 8 <= 10 must be ACCEPTED.
        //
        // Before the fix, a text-matching anchor scan found RuleA FIRST (its AppliesTo B1:B3
        // also covers B3) and returned RuleA's anchor B1 instead. Shifting "=A1" from B1 to B3
        // (two rows down) rebases it onto A3 (=5), so 8 <= 5 was wrongly REJECTED.
        DataValidationService.Validate(ruleC, new NumberValue(8), sheet, b3, workbook)
            .Should().BeNull("RuleC's own anchor (B3) means the bound is A1 (=10), and 8 <= 10 should pass");
    }

    [Fact]
    public void Validate_OverlappingRuleSharingBoundText_RejectsValueThatOnlyPassesTheWrongAnchorBound()
    {
        var (workbook, sheet) = MakeSheetWithAnchors();
        var (_, ruleC) = MakeOverlappingRulesSharingBoundText(sheet, DvOperator.GreaterThanOrEqual);

        var b3 = new CellAddress(sheet.Id, 3, 2);

        // Mirror of the test above with the operator flipped, so the discrepancy runs the other
        // direction: RuleC's own anchor (B3, no shift) means the bound is A1 (=10), and
        // 7 >= 10 is false, so this must be REJECTED.
        //
        // Before the fix, the wrongly-recovered anchor (RuleA's B1, shifted +2 rows to B3) rebased
        // the bound onto A3 (=5), and 7 >= 5 is true, so this was wrongly ACCEPTED.
        DataValidationService.Validate(ruleC, new NumberValue(7), sheet, b3, workbook)
            .Should().NotBeNull("RuleC's own anchor (B3) means the bound is A1 (=10), and 7 >= 10 should fail");
    }

    [Fact]
    public void Validate_DecoyRuleAtOverlapCell_StillShiftsFromItsOwnAnchorDespiteSharedBoundText()
    {
        var (workbook, sheet) = MakeSheetWithAnchors();
        var (ruleA, _) = MakeOverlappingRulesSharingBoundText(sheet, DvOperator.LessThanOrEqual);

        var b3 = new CellAddress(sheet.Id, 3, 2);

        // Sibling/no-regression check: RuleA itself (the decoy that a text-matching scan used to
        // match first) must still validate correctly at the overlap cell B3 using ITS OWN anchor
        // B1, i.e. "=A1" shifted two rows to A3 (=5) — the fix must not break the multi-cell
        // shifting behavior for the rule that legitimately owns that shift.
        DataValidationService.Validate(ruleA, new NumberValue(3), sheet, b3, workbook)
            .Should().BeNull("RuleA's own anchor (B1) shifted to B3 means the bound is A3 (=5), and 3 <= 5 should pass");

        DataValidationService.Validate(ruleA, new NumberValue(8), sheet, b3, workbook)
            .Should().NotBeNull("RuleA's own anchor (B1) shifted to B3 means the bound is A3 (=5), and 8 <= 5 should fail");
    }
}
