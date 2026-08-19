using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R149-remediation-sort-color-icon-spill: the r149 fix wave threaded a dynamic-array spill
/// member's LIVE value (SortCellPayload.EffectiveValue) into the Sort On: Cell Values comparator
/// (see <see cref="F1_SortCommandArraySpillMemberValueTests"/>), but SortCommand.GetEffectiveColor
/// and GetEffectiveIcon -- used by Sort On: Cell Color / Font Color / Cell Icon -- were still handed
/// only the spill member's null Cell, so a value-keyed conditional-format rule (CellValue,
/// ContainsText/BeginsWith/EndsWith, Blanks/Errors) or an IconSet rule always evaluated a spill
/// member against BlankValue.Instance instead of its real spilled value. Fixed by adding an optional
/// ScalarValue? effectiveValue parameter to both helpers (and to the private
/// TryEvaluateSimpleConditionalFormatRule it delegates to), defaulting to the previous
/// cell?.Value ?? BlankValue.Instance behavior when omitted (so FilterCommand/AutoFilterDropdown-
/// MenuPlanner/SortDialogPlanner callers -- which pass an ordinary, non-spill-aware Cell? -- are
/// byte-for-byte unaffected), and threading Payloads[index].EffectiveValue through at every
/// SortCommand call site: the top-to-bottom and left-to-right no-target-chosen comparator branches,
/// AND CompareKey's target-color/target-icon branch (not spelled out in the original gap report,
/// but the identical defect, reached the same way once a target color/icon is chosen).
/// </summary>
public sealed class R149Remediation_SortCommandColorIconSpillTests
{
    private static (Workbook workbook, Sheet sheet) BuildSpillWithCellValueRule(CellColor red)
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();

        // A1:A3 is a live dynamic-array spill: A1 is the anchor (real Cell, value 20). A2/A3 are
        // non-anchor spill members with no _cells entry of their own (values live only in the
        // _spillValues overlay) -- exactly the F1 spill shape, but this time value 200 (which
        // SHOULD match the CellValue > 100 rule below) lives on a spill member (A2), not the anchor.
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetFormula(anchor, "{20;200;10}");
        sheet.GetCell(anchor)!.Value = new NumberValue(20);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[3, 1]
        {
            { new NumberValue(20) },  // row 0 (anchor slot) -- SetSpillRange ignores this element
            { new NumberValue(200) }, // A2 -- should match CellValue > 100 -> red
            { new NumberValue(10) },  // A3 -- should not match
        }));

        // B column rides along as ordinary real cells, uniquely identifying each row post-sort.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("Low20")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("High200")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new TextValue("Low10")));

        var cfRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = cfRange,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { FillColor = red }
        });

        return (workbook, sheet);
    }

    [Fact]
    public void SortByCellColor_WithTargetColor_MatchesSpillMemberAgainstItsRealValue_NotBlank()
    {
        var red = new CellColor(255, 0, 0);
        var (workbook, sheet) = BuildSpillWithCellValueRule(red);
        var ctx = new TestCommandContext(workbook);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var command = new SortCommand(
            sheet.Id, range,
            [new SortKey(0, true, SortOn.CellColor, TargetColor: red)]);

        command.Apply(ctx).Success.Should().BeTrue();

        // A2 (spill member, value 200) is the only row whose real value satisfies the CF rule, so
        // it must be pulled to the top by the target-color match -- exercising CompareKey's
        // target-color branch (SortCommand.cs ~1436-1437), not just the no-target-chosen guard.
        // Before this fix, A2 always read as BlankValue for CF purposes, never matched, and the
        // three rows kept their original order (Low20, High200, Low10).
        sheet.GetValue(1, 2).Should().Be(new TextValue("High200"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Low20"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Low10"));
    }

    [Fact]
    public void SortByCellColor_NoTargetColor_TreatsMatchingSpillMemberAsColoredNotNoFill()
    {
        var red = new CellColor(255, 0, 0);
        var (workbook, sheet) = BuildSpillWithCellValueRule(red);
        var ctx = new TestCommandContext(workbook);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        // No target color chosen: Excel's rule is "no fill sorts last, direction-independent";
        // among differently (but each non-null) colored cells there is no ordering, so relative
        // order within the "has a color" bucket is preserved via the stable-sort tiebreak.
        var command = new SortCommand(
            sheet.Id, range,
            [new SortKey(0, true, SortOn.CellColor, TargetColor: null)]);

        command.Apply(ctx).Success.Should().BeTrue();

        // A2 (spill member, 200) is the only row whose real value matches the CF rule -> the only
        // "has a color" row -> must sort first, ahead of the two no-fill rows, which keep their
        // original relative order (A1 before A3) in the no-fill bucket behind it.
        // Before this fix, A2 was misclassified as "no fill" (its CF rule was judged against
        // BlankValue), so all three rows tied as "no fill" and kept their original order instead
        // (Low20, High200, Low10).
        sheet.GetValue(1, 2).Should().Be(new TextValue("High200"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Low20"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Low10"));
    }
}
