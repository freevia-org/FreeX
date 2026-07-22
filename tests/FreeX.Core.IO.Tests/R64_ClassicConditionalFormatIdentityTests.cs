using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R64-io-conditional-format-6-1/6-2: ClosedXML COALESCES two or more separately-added classic
/// (CellIs/Expression) conditional-format rules whose criteria+style end up byte-identical into a
/// single physical &lt;cfRule&gt; whose &lt;conditionalFormatting&gt; sqref is the union of every
/// contributing rule's ranges -- silently merging what the model represents as N distinct
/// <see cref="ConditionalFormat"/> rules (each with its own range and Priority) into one, and (because
/// <see cref="XlsxAdvancedConditionalFormatWriter"/>'s old RealignClassicRulePriorities zipped the
/// model's classic-rule priorities POSITIONALLY against the physical cfRule elements ClosedXML actually
/// wrote) desyncing the priority of every classic rule that follows the coalesced pair too.
/// </summary>
public sealed class R64_ClassicConditionalFormatIdentityTests
{
    [Fact]
    public void Save_TwoIdenticalClassicRulesOnDifferentRanges_RoundTripsAsTwoDistinctRules()
    {
        var wb = new Workbook("R64CfIdentityBook");
        var sheet = wb.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row)); // A
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new NumberValue(row)); // E
        }

        var ruleA = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 3,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "2",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        var ruleE = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 5, 5)),
            Priority = 7,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "2",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(ruleA);
        sheet.ConditionalFormats.Add(ruleE);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        var rules = reloaded.GetSheetAt(0).ConditionalFormats;

        // Must be TWO distinct rules, not one merged A+E rule.
        rules.Should().HaveCount(2, "the two identical rules on A1:A5 and E1:E5 must not be merged into one");

        var aRule = rules.Single(r => r.AppliesTo.Start.Col == 1);
        var eRule = rules.Single(r => r.AppliesTo.Start.Col == 5);

        aRule.AdditionalRanges.Should().BeNull("the A1:A5 rule must not have absorbed E1:E5 as an additional range");
        eRule.AdditionalRanges.Should().BeNull("the E1:E5 rule must not have absorbed A1:A5 as an additional range");
        aRule.AppliesTo.ToString().Should().Be(ruleA.AppliesTo.ToString());
        eRule.AppliesTo.ToString().Should().Be(ruleE.AppliesTo.ToString());
        aRule.Priority.Should().Be(3, "the A1:A5 rule's real priority must survive, not be renumbered");
        eRule.Priority.Should().Be(7, "the E1:E5 rule's real priority must survive, not be renumbered");
    }

    [Fact]
    public void Save_TwoIdenticalClassicRulesPlusColorScaleAndDistinctClassicRule_PreservesAllIdentitiesAndPriorities()
    {
        var wb = new Workbook("R64CfIdentityBook2");
        var sheet = wb.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row)); // A
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row)); // C (color scale)
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new NumberValue(row)); // E
            sheet.SetCell(new CellAddress(sheet.Id, row, 7), new NumberValue(row)); // G (cfC)
        }

        var ruleA = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 3,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "2",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        var colorScale = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3)),
            Priority = 5,
            RuleType = CfRuleType.ColorScale,
            MinColor = new RgbColor(255, 0, 0),
            MaxColor = new RgbColor(0, 255, 0)
        };
        var ruleE = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 5, 5)),
            Priority = 7,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "2",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        var ruleC = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 7), new CellAddress(sheet.Id, 5, 7)),
            Priority = 9,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.LessThan,
            Value1 = "999",
            FormatIfTrue = new CellStyle { Italic = true }
        };
        sheet.ConditionalFormats.Add(ruleA);
        sheet.ConditionalFormats.Add(colorScale);
        sheet.ConditionalFormats.Add(ruleE);
        sheet.ConditionalFormats.Add(ruleC);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        var rules = reloaded.GetSheetAt(0).ConditionalFormats;
        rules.Should().HaveCount(4, "A, ColorScale, E and cfC must all survive as four distinct rules");

        var aRule = rules.Single(r => r.RuleType == CfRuleType.CellValue && r.AppliesTo.Start.Col == 1);
        var scaleRule = rules.Single(r => r.RuleType == CfRuleType.ColorScale);
        var eRule = rules.Single(r => r.RuleType == CfRuleType.CellValue && r.AppliesTo.Start.Col == 5);
        var cRule = rules.Single(r => r.RuleType == CfRuleType.CellValue && r.AppliesTo.Start.Col == 7);

        aRule.Priority.Should().Be(3);
        scaleRule.Priority.Should().Be(5);
        eRule.Priority.Should().Be(7);
        cRule.Priority.Should().Be(9, "cfC's own real priority must survive -- a positional desync from " +
            "the coalesced A/E pair must not leak an unrelated priority (e.g. 7) onto cfC");

        aRule.AdditionalRanges.Should().BeNull();
        eRule.AdditionalRanges.Should().BeNull();
        cRule.Operator.Should().Be(CfOperator.LessThan);
        cRule.FormatIfTrue!.Italic.Should().BeTrue();
    }

    [Fact]
    public void Save_SingleClassicRulePlusSingleAdvancedRule_StillRoundTripsPrioritiesCorrectly()
    {
        // Sibling no-regression: a single classic rule + a single advanced (DataBar) rule -- the case
        // R51's ConditionalFormatSharedPriorityTests/R51_ConditionalFormatNullFormatIfTrueTests already
        // cover -- must still round-trip priorities correctly after this fix.
        var wb = new Workbook("R64CfSiblingBook");
        var sheet = wb.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row));
        }

        var classicRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        var dataBarRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3)),
            Priority = 2,
            RuleType = CfRuleType.DataBar
        };
        sheet.ConditionalFormats.Add(classicRule);
        sheet.ConditionalFormats.Add(dataBarRule);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        var rules = reloaded.GetSheetAt(0).ConditionalFormats;
        rules.Should().HaveCount(2);

        var classic = rules.Single(r => r.RuleType == CfRuleType.CellValue);
        var dataBar = rules.Single(r => r.RuleType == CfRuleType.DataBar);
        classic.Priority.Should().BeLessThan(dataBar.Priority,
            "the classic rule was first (priority 1) in the original file order");
    }
}
