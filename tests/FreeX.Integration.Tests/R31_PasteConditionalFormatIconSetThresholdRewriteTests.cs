using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R31-io-conditionalformat-eval-deep-1: Pasting an icon-set conditional format rewrote the
/// rule's colorScale/dataBar Formula-type cfvo thresholds for the new anchor (RewriteThresholdValue)
/// but copied every IconSetThresholds entry verbatim via AddRange, even though a Formula-type icon-set
/// threshold holds a relative cell reference exactly like the colorScale/dataBar thresholds. Real Excel
/// shifts these the same way it shifts FormulaText -- mirrors RowColumnShiftHelpers.Rules.cs's iconSet
/// threshold loop, which already does this for insert/delete rows/cols.
/// </summary>
public sealed class R31_PasteConditionalFormatIconSetThresholdRewriteTests
{
    [Fact]
    public void PasteConditionalFormatsCommand_RewritesIconSetFormulaThreshold_ForNewAnchor()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // Icon set on B2:B11 with a Formula-type threshold referencing A1, and a Number-type
        // threshold with a literal percentile value -- mirrors a real Excel 3-icon-set rule.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 11, 2)),
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1"
        });
        sheet.ConditionalFormats[0].IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "0"));
        sheet.ConditionalFormats[0].IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Formula, "A1"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 11, 2));
        var destination = new CellAddress(sheet.Id, 9, 4); // paste B2:B11 -> D9:D18 (rowDelta=7, colDelta=2)

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, destination, transpose: false)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();

        // The Formula-type threshold ("A1") must be shifted by the same (rowDelta=7, colDelta=2)
        // offset as FormulaText, becoming "C8" -- not copied verbatim (which would leave the pasted
        // rule at D9:D18 thresholding against the original, now-unrelated cell A1).
        pasted.IconSetThresholds.Should().HaveCount(2);
        pasted.IconSetThresholds[1].Type.Should().Be(CfThresholdType.Formula);
        pasted.IconSetThresholds[1].Value.Should().Be("C8");

        // Sibling case: the Percent-type threshold holds a literal value and must be copied verbatim.
        pasted.IconSetThresholds[0].Type.Should().Be(CfThresholdType.Percent);
        pasted.IconSetThresholds[0].Value.Should().Be("0");
    }
}
