using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for review-round fixes to conditional-format data bar axis placement,
/// icon-set reverse-order combined with per-threshold icon overrides, and zero-length data bars
/// no longer falling through to a lower-priority overlapping Data Bar rule.
/// </summary>
public partial class ConditionalFormatTests
{
    // ── K6: Midpoint axis position must be a fixed 50%, distinct from Automatic ───────────────

    [Fact]
    public void DataBar_AxisPositionMiddle_UsesFixedFiftyPercentAxisOnAsymmetricRange()
    {
        // Range -10..90 (asymmetric). Automatic would place the zero-crossing axis at 10%
        // ((0-(-10))/(90-(-10)) = 0.10). Excel's "Middle" axis position instead always pins
        // the axis at exactly 50%, regardless of the min/max skew.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(-10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(90)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarAxisPosition = "middle"
        });

        var viewport = GetViewport(wb, sheet);

        var negativeCell = GetCell(viewport, 1, 1);
        negativeCell.ConditionalDataBar.Should().NotBeNull();
        negativeCell.ConditionalDataBar!.Value.AxisFraction.Should().BeApproximately(
            0.5, 0.0001, "Middle axis position is fixed at 50% regardless of range skew");

        var positiveCell = GetCell(viewport, 2, 1);
        positiveCell.ConditionalDataBar.Should().NotBeNull();
        positiveCell.ConditionalDataBar!.Value.AxisFraction.Should().BeApproximately(0.5, 0.0001);
    }

    [Fact]
    public void DataBar_AxisPositionAutomatic_StillUsesZeroCrossingOnAsymmetricRange()
    {
        // Regression guard: unset (Automatic) axis position must retain the proportional
        // zero-crossing behavior and must NOT be pulled to 50% by the Middle fix.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(-10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(90)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar
            // DataBarAxisPosition left null -> Automatic
        });

        var viewport = GetViewport(wb, sheet);

        var negativeCell = GetCell(viewport, 1, 1);
        negativeCell.ConditionalDataBar.Should().NotBeNull();
        negativeCell.ConditionalDataBar!.Value.AxisFraction.Should().BeApproximately(
            0.1, 0.0001, "Automatic axis sits at the proportional zero crossing of -10..90");
    }

    // ── K7: IconSetReverse must apply even with per-threshold custom icon overrides ───────────

    [Fact]
    public void IconSet_ReverseWithPerThresholdOverrides_MirrorsBucketToIconMapping()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(90)));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
            IconSetReverse = true
        };
        cf.IconSetThresholds.AddRange([
            new CfThresholdModel(CfThresholdType.Percent, "40"),
            new CfThresholdModel(CfThresholdType.Percent, "70")
        ]);
        cf.IconOverrides.AddRange([
            new CfIconOverride("3Arrows", 0), // bucket 0 (low) icon
            new CfIconOverride("3Arrows", 1), // bucket 1 (mid) icon
            new CfIconOverride("3Arrows", 2)  // bucket 2 (high) icon
        ]);
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        // Values 10, 50, 90 resolve to raw (unreversed) buckets 0, 1, 2. With IconSetReverse,
        // Excel mirrors the bucket->icon mapping, so the low-value cell should get the icon
        // stored for the high bucket (override index 2) and vice versa.
        GetCell(vp, 1, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3Arrows", 2, 3, true));
        GetCell(vp, 2, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3Arrows", 1, 3, true));
        GetCell(vp, 3, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3Arrows", 0, 3, true));
    }

    // ── K30: a zero-length data bar must render no bar, not fall through to a lower rule ──────

    [Fact]
    public void DataBar_ZeroLengthAtMinValue_RendersNoBarAndDoesNotFallThroughToLowerPriorityRule()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(100)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(50)));

        // Higher-priority (lower Priority number) rule over A1:A2 with default MinLength=0%,
        // so the cell at the range minimum (A1=0) computes a zero-length bar.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(0, 112, 192)
        });

        // Lower-priority, overlapping Data Bar rule covering A1:A3 with a fixed, entirely
        // negative [-100, -10] scale (via explicit Number thresholds, so it never enters the
        // zero-straddling axis branch). A1's value of 0 is above this range's max, so it would
        // resolve to a clearly non-zero, fully-filled bar if the loop incorrectly fell through
        // to this rule instead of stopping at rule 1's authoritative empty result.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 2,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(255, 0, 0),
            DataBarMinThresholdType = CfThresholdType.Number,
            DataBarMinThresholdValue = "-100",
            DataBarMaxThresholdType = CfThresholdType.Number,
            DataBarMaxThresholdValue = "-10"
        });

        var viewport = GetViewport(wb, sheet);

        var minCell = GetCell(viewport, 1, 1);
        minCell.ConditionalDataBar.Should().BeNull(
            "the highest-priority rule authoritatively resolves to a zero-length (no) bar and must not fall through");
    }

    [Fact]
    public void DataBar_ZeroLengthOnNegativeAxis_RendersNoBar()
    {
        // Range -10..10 straddling zero with a fixed Middle axis. Pinning both MinLength and
        // MaxLength to 0% collapses every resolved bar length to zero, including on the
        // negative-axis branch, which must render no bar rather than falling through.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(-10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(10)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarAxisPosition = "middle",
            DataBarMinLength = 0,
            DataBarMaxLength = 0
        });

        var viewport = GetViewport(wb, sheet);

        var negativeCell = GetCell(viewport, 1, 1);
        negativeCell.ConditionalDataBar.Should().BeNull("MinLength=MaxLength=0% collapses the bar to zero length");

        var positiveCell = GetCell(viewport, 2, 1);
        positiveCell.ConditionalDataBar.Should().BeNull("MinLength=MaxLength=0% collapses the bar to zero length");
    }
}
