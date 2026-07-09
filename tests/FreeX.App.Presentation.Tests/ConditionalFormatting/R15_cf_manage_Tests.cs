using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

/// <summary>
/// Round 15 regression tests for the Manage-Rules dialog's AllRanges-vs-AppliesTo mismatch
/// (R15-cf-rule-management-1) and the ApplyRuleRange stale-AdditionalRanges bug
/// (R15-cf-rule-management-2).
/// </summary>
public sealed class R15_cf_manage_Tests
{
    [Fact]
    public void BuildResultRules_UsesAllRangesOverlapSoNoOpCommitPreservesBothRulesShownViaAdditionalRange()
    {
        var sheetId = SheetId.New();

        // R1's primary AppliesTo (B1:B10) does NOT overlap the selection, but its AdditionalRanges
        // entry (A1:A5) does -> the dialog (PopulateRules, which filters on AllRanges) shows R1.
        var r1 = CreateRule(sheetId, appliesToRow: 1, appliesToCol: 2, appliesToEndRow: 10, priority: 1);
        r1.AdditionalRanges = [new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 1))];

        var r2 = CreateRule(sheetId, appliesToRow: 1, appliesToCol: 1, appliesToEndRow: 5, priority: 2);

        var selection = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 1));

        // Committing with no edit: the dialog would pass back the same rules it showed, in the
        // same (display) order -> [r1, r2], since both overlap the selection via AllRanges.
        var result = ManageConditionalFormatsPlanner.BuildResultRules(
            sheetRules: [r1, r2],
            selection: selection,
            filterToSelection: true,
            editedRules: [r1, r2]);

        result.Should().HaveCount(2, "a no-op commit must not drop or duplicate rules");
        result.Select(r => r.Id).Should().Equal(r1.Id, r2.Id);
        result.Single(r => r.Id == r1.Id).AppliesTo.Should().Be(r1.AppliesTo);
        result.Single(r => r.Id == r1.Id).AdditionalRanges.Should().NotBeNull().And.HaveCount(1);
        result.Single(r => r.Id == r2.Id).AppliesTo.Should().Be(r2.AppliesTo);
    }

    [Fact]
    public void ApplyRuleRange_NarrowingAppliesToClearsAdditionalRanges()
    {
        var sheetId = SheetId.New();

        var r1 = CreateRule(sheetId, appliesToRow: 1, appliesToCol: 2, appliesToEndRow: 10, priority: 1);
        r1.AdditionalRanges = [new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 1))];

        var newAppliesTo = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 1));

        var result = ManageConditionalFormatsPlanner.ApplyRuleRange([r1], r1.Id, newAppliesTo);

        result.Should().ContainSingle();
        result[0].AppliesTo.Should().Be(newAppliesTo);
        result[0].AdditionalRanges.Should().BeNullOrEmpty(
            "narrowing Applies-to in the dialog replaces the entire sqref, matching Excel semantics");
    }

    private static ConditionalFormat CreateRule(
        SheetId sheetId,
        uint appliesToRow,
        uint appliesToCol,
        uint appliesToEndRow,
        int priority,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            AppliesTo = new GridRange(
                new CellAddress(sheetId, appliesToRow, appliesToCol),
                new CellAddress(sheetId, appliesToEndRow, appliesToCol)),
            Priority = priority,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "1",
            FormatIfTrue = new CellStyle { Italic = true }
        };
}
