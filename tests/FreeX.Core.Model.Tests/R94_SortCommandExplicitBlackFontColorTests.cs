using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R94: <see cref="SortCommand.GetEffectiveColor"/> (Sort/Filter On Font Color) used to resolve a
/// conditional-format rule's font color as <c>fmt.FontColor != CellColor.Black ? fmt.FontColor :
/// null</c>, treating an EXPLICITLY-authored CF black font color identically to a rule that never
/// touches font color at all. That meant an explicit-black rule was silently skipped in favor of a
/// lower-priority rule (or the base style) for Sort On Font Color, disagreeing with what
/// FreeX.Core.Calc.ViewportConditionalFormatEvaluator actually renders in the grid — which now
/// distinguishes "explicitly set (including black)" from "unset" via the tri-state
/// <see cref="CellStyle.DxfFontColor"/> field (populated by the xlsx dxf reader). GetEffectiveColor
/// now consults DxfFontColor the same way, so the two tiers agree.
/// </summary>
public sealed class R94_SortCommandExplicitBlackFontColorTests
{
    [Fact]
    public void SortByFontColor_ExplicitBlackHigherPriorityRule_WinsOverLowerPriorityRedRule()
    {
        // Two CF rules apply to every cell in the range (CellValue > 0, always true for these
        // positive numbers). Rule 1 (Priority 1, higher precedence) explicitly sets font color to
        // BLACK, with DxfFontColor recording that this was an explicit author choice (mirroring
        // what XlsxDifferentialStyleReader now populates from a real <color rgb="FF000000"/> dxf).
        // Rule 2 (Priority 2, lower precedence) sets font color to RED and matches every cell too.
        //
        // Row 1 ("OnlyRed") is only covered by rule 2, so its effective font color is Red.
        // Row 2 ("BlackThenRed") is covered by both rules; rule 1's explicit black must win because
        // it has higher precedence — it must NOT fall through to rule 2's red just because black
        // looks like "unset" under the old FontColor != Black heuristic.
        //
        // Sorting On Font Color with TargetColor = Black, ascending, must therefore pull
        // "BlackThenRed" to the front (it matches the black target) and leave "OnlyRed" behind it.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("OnlyRed") });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new TextValue("BlackThenRed") });
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        // Both rules key off CfRuleType.NoBlanks (matches any non-blank cell) so the sorted
        // column's own text values drive the match — no separate numeric helper column needed.
        var explicitBlackRule = new ConditionalFormat
        {
            // Only row 2 ("BlackThenRed") is covered by the higher-precedence explicit-black rule.
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.NoBlanks,
            FormatIfTrue = new CellStyle { FontColor = CellColor.Black, DxfFontColor = CellColor.Black }
        };
        var redRule = new ConditionalFormat
        {
            // Both rows are covered by the lower-precedence red rule.
            AppliesTo = range,
            Priority = 2,
            RuleType = CfRuleType.NoBlanks,
            FormatIfTrue = new CellStyle { FontColor = new CellColor(255, 0, 0) }
        };
        sheet.ConditionalFormats.Add(explicitBlackRule);
        sheet.ConditionalFormats.Add(redRule);

        var command = new SortCommand(
            sheet.Id, range,
            [new SortKey(0, true, SortOn.FontColor, TargetColor: CellColor.Black)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("BlackThenRed"),
            "the explicit-black rule has higher precedence and must win Sort On Font Color, not fall through to the lower-priority red rule");
        sheet.GetValue(2, 1).Should().Be(new TextValue("OnlyRed"));
    }

    [Fact]
    public void SortByFontColor_NonBlackTargetColor_StillMatches_NoRegression()
    {
        // Sibling no-regression case: an ordinary non-black CF font color (which never went
        // through the DxfFontColor tri-state ambiguity) still sorts correctly by target color.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("NoMatch") });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new TextValue("RedMatch") });
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        var redRule = new ConditionalFormat
        {
            // Only row 2 ("RedMatch") is covered by the rule; row 1 has no matching CF at all and
            // falls back to the base style's default (black) font color.
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.NoBlanks,
            FormatIfTrue = new CellStyle { FontColor = new CellColor(255, 0, 0) }
        };
        sheet.ConditionalFormats.Add(redRule);

        var command = new SortCommand(
            sheet.Id, range,
            [new SortKey(0, true, SortOn.FontColor, TargetColor: new CellColor(255, 0, 0))]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("RedMatch"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("NoMatch"));
    }
}
