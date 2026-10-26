using FluentAssertions;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// r566: an icon-set conditional format stores its bucket thresholds as TEXT in the xlsx, and
/// <c>SortCommand.GetEffectiveIcon</c> parses them with a bare <c>double.TryParse</c>. That path
/// does NOT go through <c>ConditionalFormatEvaluationMath.TryParseInvariant</c>, which r564
/// guarded -- so r564 fixed the cell-value rules and left the icon-set rules open.
///
/// <para>A non-finite threshold silently collapses the whole scale: every comparison against NaN
/// is false so every cell falls to the LOWEST icon, and a -Infinity threshold is matched by every
/// cell so they all take the HIGHEST. The sheet still renders, with one icon everywhere.</para>
///
/// <para>Found by reading the r565 census rather than by following r564 outward: the sibling is in
/// another project (Core.Commands, not Core.Model) under another name.</para>
/// </summary>
public sealed class R566_IconSetThresholdsAreFiniteTests
{
    private static (Workbook Workbook, Sheet Sheet) SheetWithIconRule(string secondThreshold)
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");

        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
            Priority = 1,
            // The resolver skips any rule whose ranges do not contain the address. My first draft
            // omitted this, and the NON-VACUITY test is what caught it: every hostile case
            // "passed" by returning null for a reason unrelated to the threshold.
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 10)),
        };
        rule.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "0"));
        rule.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, secondThreshold));
        rule.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "100"));
        sheet.ConditionalFormats.Add(rule);

        return (workbook, sheet);
    }

    private static int? IconIdFor(string secondThreshold, double cellValue)
    {
        var (workbook, sheet) = SheetWithIconRule(secondThreshold);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new NumberValue(cellValue));

        return SortCommand
            .GetEffectiveIcon(workbook, sheet, address, sheet.GetCell(address), null)
            ?.IconId;
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]
    public void An_unusable_threshold_does_not_decide_the_icon(string hostile)
    {
        // A threshold that is not a finite number cannot place a value on the scale, so the rule
        // must not resolve an icon at all -- the same answer the resolver already gives when the
        // threshold text does not parse.
        IconIdFor(hostile, 50).Should().BeNull();
    }

    [Fact]
    public void Ordinary_thresholds_still_place_the_value_on_the_scale()
    {
        // Non-vacuity across the whole scale: a guard that rejected every threshold would satisfy
        // the assertions above while silently removing every icon in the workbook.
        IconIdFor("50", 10).Should().NotBeNull();
        IconIdFor("50", 75).Should().NotBeNull();

        // And the buckets must still differ, so the scale is genuinely being applied rather than
        // collapsed to a single icon.
        IconIdFor("50", 10).Should().NotBe(IconIdFor("50", 75));
    }

    [Fact]
    public void An_unparseable_threshold_was_already_rejected()
    {
        // Pins the pre-existing contract the fix reuses, so the two paths cannot be separated.
        IconIdFor("not a number", 50).Should().BeNull();
    }
}
