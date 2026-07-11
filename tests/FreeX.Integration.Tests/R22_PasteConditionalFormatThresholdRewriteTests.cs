using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R22-cell-reference-rewrite-3: Pasting a color-scale/data-bar conditional format rewrote the
/// rule's own FormulaText for the new anchor (RewriteFormulaText, CloneRuleForDestination) but
/// copied every Formula-type cfvo threshold (MinThresholdValue/MidThresholdValue/MaxThresholdValue,
/// DataBarMinThresholdValue/DataBarMaxThresholdValue) verbatim, even though these fields hold a
/// relative cell reference exactly like a "Formula is" rule's FormulaText when their ThresholdType
/// is CfThresholdType.Formula. Real Excel shifts these the same way it shifts FormulaText -- mirrors
/// RowColumnShiftHelpers.Rules.cs's RewriteThreshold, which already does this for insert/delete rows/cols.
/// </summary>
public sealed class R22_PasteConditionalFormatThresholdRewriteTests
{
    [Fact]
    public void PasteConditionalFormatsCommand_RewritesColorScaleFormulaThresholds_ForNewAnchor()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // Two-color scale on A1:A10 whose min/max bounds are computed from formula-type cfvo
        // references B1/B2 (an Excel-supported color-scale feature), anchored at A1.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1)),
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = false,
            MinThresholdType = CfThresholdType.Formula,
            MinThresholdValue = "B1",
            MaxThresholdType = CfThresholdType.Formula,
            MaxThresholdValue = "B2",
            MinColor = new RgbColor(255, 0, 0),
            MaxColor = new RgbColor(0, 255, 0)
        });

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));
        var destination = new CellAddress(sheet.Id, 1, 3); // column C, colDelta = 2

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, destination, transpose: false)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();

        // The Formula-type thresholds must be shifted by the same colDelta as FormulaText, not
        // copied verbatim (which would leave the pasted rule at C1:C10 thresholding against the
        // original, now-unrelated cells B1/B2).
        pasted.MinThresholdValue.Should().Be("D1");
        pasted.MaxThresholdValue.Should().Be("D2");
    }

    [Fact]
    public void PasteConditionalFormatsCommand_RewritesDataBarFormulaThresholds_ForNewAnchor()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1)),
            RuleType = CfRuleType.DataBar,
            DataBarMinThresholdType = CfThresholdType.Formula,
            DataBarMinThresholdValue = "B1",
            DataBarMaxThresholdType = CfThresholdType.Formula,
            DataBarMaxThresholdValue = "B2",
            DataBarColor = new RgbColor(0, 0, 255)
        });

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));
        var destination = new CellAddress(sheet.Id, 1, 3); // column C, colDelta = 2

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, destination, transpose: false)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();

        pasted.DataBarMinThresholdValue.Should().Be("D1");
        pasted.DataBarMaxThresholdValue.Should().Be("D2");
    }

    [Fact]
    public void PasteConditionalFormatsCommand_NonFormulaThresholds_AreCopiedVerbatim()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // Min/Max (non-Formula) thresholds hold a literal number, not a cell reference, and must
        // never be run through the formula rewriter.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1)),
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = false,
            MinThresholdType = CfThresholdType.Number,
            MinThresholdValue = "10",
            MaxThresholdType = CfThresholdType.Percent,
            MaxThresholdValue = "90",
            MinColor = new RgbColor(255, 0, 0),
            MaxColor = new RgbColor(0, 255, 0)
        });

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));
        var destination = new CellAddress(sheet.Id, 1, 3);

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, destination, transpose: false)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();

        pasted.MinThresholdValue.Should().Be("10");
        pasted.MaxThresholdValue.Should().Be("90");
    }
}
