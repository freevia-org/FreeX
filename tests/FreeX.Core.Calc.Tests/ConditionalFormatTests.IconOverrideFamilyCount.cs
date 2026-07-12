using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    [Fact]
    public void IconSet_OverrideFromDifferentFamily_UsesOverrideFamilysOwnIconCount()
    {
        // A 3-icon rule (3TrafficLights1) whose middle-bucket icon is overridden with a glyph
        // from the 5-arrow family. Excel renders that override using the 5-arrow family's own
        // shape/color (index 1 of 5 = down-diagonal), not as if it were index 1 of the rule's
        // native 3-icon bucket count (which would render as the neutral "Right" arrow).
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(90)));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1"
        };
        cf.IconSetThresholds.AddRange([
            new CfThresholdModel(CfThresholdType.Percent, "40"),
            new CfThresholdModel(CfThresholdType.Percent, "70")
        ]);
        cf.IconOverrides.AddRange([
            new CfIconOverride("3TrafficLights1", 0),
            new CfIconOverride("5Arrows", 1),
            new CfIconOverride("3TrafficLights1", 2)
        ]);
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        // Values 10, 50, 90 with thresholds at 40% (42) and 70% (66) of range [10,90]
        // → buckets 0, 1, 2 respectively. Bucket 1's override pulls IconId 1 from the 5Arrows
        // family, so the resolved icon must carry IconCount 5 (that family's own size), not 3
        // (the rule's bucket count) — otherwise downstream glyph/color/rating-bar math would
        // treat index 1 as "Right" (3-icon neutral) instead of "DownDiagonal" (5-icon slot 1).
        GetCell(vp, 2, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("5Arrows", 1, 5, true));

        // Sibling buckets whose overrides stay within the rule's own family are unaffected.
        GetCell(vp, 1, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 0, 3, true));
        GetCell(vp, 3, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 2, 3, true));
    }
}
