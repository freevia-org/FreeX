using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R51-io-cf-priority-order-3-1: a classic (CellValue/Formula) conditional-format rule with
/// FormatIfTrue == null (e.g. a NativeJsonAdapter-loaded workbook that simply omitted the
/// optional field) was silently dropped by <see cref="XlsxConditionalFormatClosedXmlMapper.Save"/>.
/// Because <see cref="XlsxAdvancedConditionalFormatWriter"/>'s RealignClassicRulePriorities zips
/// the model's classic-rule priorities positionally against the classic cfRule elements ClosedXML
/// actually wrote, dropping one rule also corrupted the priority of every classic rule written
/// after it. Both the lost rule and the priority corruption must be fixed.
/// </summary>
public sealed class R51_ConditionalFormatNullFormatIfTrueTests
{
    [Fact]
    public void Save_ClassicRuleWithNullFormatIfTrue_IsNotDroppedAndDoesNotCorruptLaterPriorities()
    {
        var wb = new Workbook("R51CfBook");
        var sheet = wb.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));   // A
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row));   // C
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new NumberValue(row));   // E
        }

        // Rule 1: classic CellValue on A1:A5, Priority=1, FormatIfTrue == null.
        var ruleWithNullFormat = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = null
        };

        // Rule 2: advanced DataBar on C1:C5, Priority=2.
        var dataBarRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3)),
            Priority = 2,
            RuleType = CfRuleType.DataBar
        };

        // Rule 3: classic CellValue on E1:E5, Priority=3, with a real style.
        var ruleWithFormat = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 5, 5)),
            Priority = 3,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { Bold = true }
        };

        sheet.ConditionalFormats.Add(ruleWithNullFormat);
        sheet.ConditionalFormats.Add(dataBarRule);
        sheet.ConditionalFormats.Add(ruleWithFormat);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        var rules = reloaded.GetSheetAt(0).ConditionalFormats;

        // The null-FormatIfTrue rule on A1:A5 must survive the round trip -- not be dropped.
        rules.Should().Contain(
            r => r.RuleType == CfRuleType.CellValue && r.AppliesTo.Start.Col == 1,
            "the classic rule with a null FormatIfTrue must still be written, not silently dropped");

        var aRule = rules.Single(r => r.RuleType == CfRuleType.CellValue && r.AppliesTo.Start.Col == 1);
        var dataBar = rules.Single(r => r.RuleType == CfRuleType.DataBar);
        var eRule = rules.Single(r => r.RuleType == CfRuleType.CellValue && r.AppliesTo.Start.Col == 5);

        // True file-order priorities must be preserved: A-rule < DataBar < E-rule.
        aRule.Priority.Should().BeLessThan(dataBar.Priority,
            "the A1:A5 rule was first (priority 1) in the original file order");
        dataBar.Priority.Should().BeLessThan(eRule.Priority,
            "the E1:E5 rule was last (priority 3); it must not be corrupted to look like it precedes the data bar");
    }

    [Fact]
    public void Save_ClassicRuleWithFormatIfTrue_StillAppliesStyleAndRoundTrips()
    {
        // Sibling no-regression test: a normal classic rule (non-null FormatIfTrue) must still
        // save/reload correctly with its style intact -- the fix must not affect this path.
        var wb = new Workbook("R51CfSiblingBook");
        var sheet = wb.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(rule);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        var reloadedRule = reloaded.GetSheetAt(0).ConditionalFormats.Single();
        reloadedRule.RuleType.Should().Be(CfRuleType.CellValue);
        reloadedRule.FormatIfTrue.Should().NotBeNull();
        reloadedRule.FormatIfTrue!.Bold.Should().BeTrue("the rule's style must still round-trip when FormatIfTrue is non-null");
    }
}
