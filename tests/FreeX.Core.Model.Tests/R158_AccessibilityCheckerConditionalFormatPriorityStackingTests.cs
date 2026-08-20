using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

// Round-158 findings freex-conditional-format-priority F2/F3:
//
// F2: AccessibilityCheckerService.Contrast.cs resolved the "effective" conditional-format style
// for a cell by unconditionally overwriting `style = rule.FormatIfTrue!;` on every matching rule
// in priority order, instead of differentially stacking them the way
// ViewportConditionalFormatEvaluator.StackDifferentialStyle does (first/highest-priority rule to
// set a given property wins). So whenever two or more matching rules with StopIfTrue=false applied
// to a cell, only the LAST (lowest-priority) matching rule's FormatIfTrue was used for the contrast
// check -- a higher-priority rule's dark fill was silently discarded by a lower-priority rule that
// only set an unrelated property (e.g. Bold) and never touched color. This hit all three overwrite
// sites in the file: GetAlwaysTrueTextValueStyle (the shared-applies-to-range "always true"
// fast path for NoBlanks/NoErrors rules), GetEffectiveContrastStyleForApplicableRules (the
// shared-applies-to-range path for other rule types), and the general per-cell fallback loop in
// GetEffectiveContrastStyle. All three now stack via the new StackConditionalFormatContrastStyle
// helper instead of overwriting.
//
// F3: every containment check in this file tested only `rule.AppliesTo.Contains(address)`, never
// `rule.AllRanges` (which also folds in AdditionalRanges, the extra regions of a multi-region
// sqref such as "A1:A10 C1:C10"). A rule that paints a second/third region on-screen (via
// ViewportConditionalFormatEvaluator/ConditionalFormatRenderEvaluator, which both correctly use
// AllRanges) was therefore invisible to the accessibility contrast checker for every region after
// the first. TryGetSharedAppliesToRange's fast path had a related gap: it decided "all rules share
// one range" purely by comparing AppliesTo, so a rule with AdditionalRanges could feed the fast
// path and have its additional-region cells silently skipped entirely (the fast path returns the
// base style untouched for any address outside the single SharedAppliesToRange). It now bails out
// of the fast path whenever any rule carries AdditionalRanges.
public sealed class R158_AccessibilityCheckerConditionalFormatPriorityStackingTests
{
    // F2 -- exercises GetEffectiveContrastStyleForApplicableRules (the shared-applies-to-range
    // path for a non-"always true" rule type: both rules apply to the exact same single-cell
    // range, so TryGetSharedAppliesToRange's fast path is used, but CellValue is not NoBlanks/
    // NoErrors so GetAlwaysTrueTextValueStyle bails out and the per-applicable-rule loop runs).
    [Fact]
    public void FindIssues_FlagsLowContrastCellText_SharedRangeHigherPriorityFillSurvivesLowerPriorityColorlessMatch()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new Cell { Value = new NumberValue(150) });
        var range = new GridRange(address, address);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(40, 40, 40) }
        });
        // Lower-priority rule matches the same cell too, but only sets Bold -- no color at all.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 2,
            StopIfTrue = false,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { Bold = true }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("B2");
    }

    // F2 -- exercises the general per-cell fallback loop directly: the two rules apply to
    // different (non-identical) ranges that both cover the same cell, so
    // TryGetSharedAppliesToRange's fast path never engages and GetEffectiveContrastStyle's own
    // foreach loop over conditionalContrastRules.Rules resolves the effective style.
    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FallbackLoopHigherPriorityFillSurvivesLowerPriorityColorlessMatch()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new Cell { Value = new NumberValue(150) });

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(40, 40, 40) }
        });
        // Different (wider) AppliesTo range so the shared-range fast path does not apply.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, new CellAddress(sheet.Id, 3, 3)),
            Priority = 2,
            StopIfTrue = false,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { Bold = true }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("B2");
    }

    // F2 -- the literal reproduction from the finding: both rules are NoBlanks (so both are
    // "always true for scanned text") and share the exact same AppliesTo range, which routes
    // through GetAlwaysTrueTextValueStyle's own precomputed-style overwrite loop.
    [Fact]
    public void FindIssues_FlagsLowContrastCellText_SharedAlwaysTrueRangeHigherPriorityFillSurvivesLowerPriorityColorlessMatch()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new TextValue("At risk"));
        var range = new GridRange(address, address);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.NoBlanks,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(40, 40, 40) }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 2,
            StopIfTrue = false,
            RuleType = CfRuleType.NoBlanks,
            FormatIfTrue = new CellStyle { Bold = true }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("B2");
    }

    // Sibling/no-regression for F2: a higher-priority StopIfTrue rule must still suppress every
    // lower-priority rule exactly as before -- stacking must not turn a genuine Stop-If-True
    // short-circuit into "keep scanning and merge anyway".
    [Fact]
    public void FindIssues_DoesNotFlagLowContrastCellText_WhenHigherPriorityStopIfTrueColorlessRuleSuppressesLowerPriorityFillRule()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new Cell { Value = new NumberValue(150) });
        var range = new GridRange(address, address);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            StopIfTrue = true,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { Bold = true }
        });
        // Would fail contrast if it were ever applied, but the Stop-If-True rule above must hide it.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 2,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(40, 40, 40) }
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    // F3 -- a single rule whose sqref covers two non-contiguous regions (AppliesTo + a second
    // region in AdditionalRanges) must be contrast-checked in BOTH regions, exactly as the real
    // renderers (ViewportConditionalFormatEvaluator, ConditionalFormatRenderEvaluator) paint both.
    [Fact]
    public void FindIssues_FlagsLowContrastCellText_InAdditionalRangesRegionAsWellAsAppliesTo()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1); // A1
        var second = new CellAddress(sheet.Id, 1, 2); // B1
        sheet.SetCell(first, new TextValue("At risk"));
        sheet.SetCell(second, new TextValue("Also at risk"));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, first),
            AdditionalRanges = [new GridRange(second, second)],
            RuleType = CfRuleType.NoBlanks,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(40, 40, 40) }
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.LowContrastCellText)
            .Select(i => i.Location)
            .ToList();

        issues.Should().BeEquivalentTo(["A1", "B1"]);
    }

    // Sibling/no-regression for F3: a cell that falls in neither AppliesTo nor AdditionalRanges
    // must still be left alone -- AllRanges must not over-match beyond the rule's real regions.
    [Fact]
    public void FindIssues_DoesNotFlagLowContrastCellText_ForCellOutsideAppliesToAndAdditionalRanges()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1); // A1
        var second = new CellAddress(sheet.Id, 1, 2); // B1
        var outside = new CellAddress(sheet.Id, 1, 3); // C1
        sheet.SetCell(first, new TextValue("At risk"));
        sheet.SetCell(second, new TextValue("Also at risk"));
        sheet.SetCell(outside, new TextValue("Untouched"));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, first),
            AdditionalRanges = [new GridRange(second, second)],
            RuleType = CfRuleType.NoBlanks,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(40, 40, 40) }
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.LowContrastCellText)
            .Select(i => i.Location)
            .ToList();

        issues.Should().BeEquivalentTo(["A1", "B1"]);
        issues.Should().NotContain("C1");
    }
}
