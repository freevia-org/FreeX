using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// spill-overlay-root F2/F3/F4: CellFillColorFilterCommand, CellNoFillColorFilterCommand, and
/// CellFontColorFilterCommand all fetch the filter column's cell via <c>sheet.GetCell(row, col)</c>,
/// which is null for a non-anchor member of a live dynamic-array spill (its value lives only in
/// Sheet's spill overlay -- see SortCommand.CaptureCellPayload's identical fallback). Before this
/// fix, that null cell was passed to SortCommand.GetEffectiveColor without the optional
/// effectiveValue parameter, so a value-keyed conditional-format rule was evaluated against
/// BlankValue.Instance instead of the spill member's real value, and a CF-driven color on that row
/// was silently invisible to color-based filtering (mirrors the SortCommand fix documented in
/// R149Remediation_SortCommandColorIconSpillTests, which never covered these three FilterCommand
/// commands).
/// </summary>
public sealed class R150_FilterCommandColorFilterSpillCFTests
{
    // A1: header row (col A "Value", col B "Label").
    // A2 (row 2): the spill's ANCHOR -- a real stored Cell with value 20. Does not match the rule.
    // A3 (row 3): a non-anchor spill member (no Cell in Sheet's _cells) with LIVE value 200, which
    //   DOES match the CellValue > 100 CF rule below -- this is the row the bug misses.
    // A4 (row 4): a non-anchor spill member with LIVE value 10. Does not match either, in BOTH the
    //   buggy and fixed code (adjacent no-regression case: an unmatched spill member must stay
    //   unmatched, not spuriously start matching).
    private static (Workbook workbook, Sheet sheet, GridRange range) BuildSpillWithCfRule(
        CellColor ruleColor, bool fillRule)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Label"));

        var anchor = new CellAddress(sheet.Id, 2, 1); // A2
        sheet.SetFormula(anchor, "{20;200;10}");
        sheet.GetCell(anchor)!.Value = new NumberValue(20);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[3, 1]
        {
            { new NumberValue(20) },  // row 0 (anchor slot) -- SetSpillRange ignores this element
            { new NumberValue(200) }, // A3 -- should match CellValue > 100
            { new NumberValue(10) },  // A4 -- should not match
        }));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("Anchor20")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new TextValue("Spill200")));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), Cell.FromValue(new TextValue("Spill10")));

        var cfRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 1));
        var formatIfTrue = new CellStyle();
        if (fillRule)
            formatIfTrue.FillColor = ruleColor;
        else
            formatIfTrue.FontColor = ruleColor;
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = cfRange,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = formatIfTrue
        });

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        return (wb, sheet, range);
    }

    [Fact]
    public void F2_CellFillColorFilterCommand_MatchesSpillMemberWhoseFillComesFromCF()
    {
        var red = new CellColor(255, 0, 0);
        var (wb, sheet, range) = BuildSpillWithCfRule(red, fillRule: true);
        var ctx = new TestCommandContext(wb);

        new CellFillColorFilterCommand(sheet.Id, range, filterColOffset: 0, red)
            .Apply(ctx).Success.Should().BeTrue();

        // Row 3 (A3, spill member, real value 200) satisfies the CF fill rule and must be kept
        // visible by a "filter by red" filter -- before the fix it was always hidden because
        // GetEffectiveColor evaluated the rule against BlankValue instead of 200.
        sheet.FilterHiddenRows.Should().NotContain(3u);
        // Adjacent no-regression case: the anchor (row 2, value 20) and the other non-matching
        // spill member (row 4, value 10) must still be hidden -- neither's real value satisfies
        // the rule, in either the buggy or the fixed code.
        sheet.FilterHiddenRows.Should().Contain([2u, 4u]);
    }

    [Fact]
    public void F3_CellNoFillColorFilterCommand_ExcludesSpillMemberWhoseFillComesFromCF()
    {
        var red = new CellColor(255, 0, 0);
        var (wb, sheet, range) = BuildSpillWithCfRule(red, fillRule: true);
        var ctx = new TestCommandContext(wb);

        new CellNoFillColorFilterCommand(sheet.Id, range, filterColOffset: 0)
            .Apply(ctx).Success.Should().BeTrue();

        // Row 3 (A3) visibly has a CF-driven red fill, so "No Fill" must hide it -- before the fix
        // its CF fill was never detected (fillColor always read null for it), so it was wrongly
        // kept visible as if unfilled.
        sheet.FilterHiddenRows.Should().Contain(3u);
        // Adjacent no-regression case: rows 2 and 4 genuinely have no fill (their real values don't
        // satisfy the CF rule) and must stay visible under "No Fill".
        sheet.FilterHiddenRows.Should().NotContain([2u, 4u]);
    }

    [Fact]
    public void F4_CellFontColorFilterCommand_MatchesSpillMemberWhoseFontColorComesFromCF()
    {
        var blue = new CellColor(0, 0, 255);
        var (wb, sheet, range) = BuildSpillWithCfRule(blue, fillRule: false);
        var ctx = new TestCommandContext(wb);

        new CellFontColorFilterCommand(sheet.Id, range, filterColOffset: 0, blue)
            .Apply(ctx).Success.Should().BeTrue();

        // Row 3 (A3, spill member, real value 200) satisfies the CF font-color rule and must be
        // kept visible by a "filter by blue font" filter -- before the fix it was always hidden.
        sheet.FilterHiddenRows.Should().NotContain(3u);
        // Adjacent no-regression case: rows 2 and 4 don't satisfy the rule and must stay hidden.
        sheet.FilterHiddenRows.Should().Contain([2u, 4u]);
    }

    [Fact]
    public void NoRegression_OrdinaryAnchorCellCfMatch_StillWorksWithoutASpill()
    {
        // Sibling no-regression case named by the finding's own scope note (R149-remediation
        // comment on FilterCommand): an ORDINARY stored Cell (not a spill member) driving a CF
        // match must keep working exactly as before -- this path never depended on effectiveValue
        // (cell.Value was already the real value), so passing it now must be a no-op here.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var red = new CellColor(255, 0, 0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(10));
        var cfRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = cfRange,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { FillColor = red }
        });
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        var ctx = new TestCommandContext(wb);

        new CellFillColorFilterCommand(sheet.Id, range, filterColOffset: 0, red)
            .Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);
    }
}
