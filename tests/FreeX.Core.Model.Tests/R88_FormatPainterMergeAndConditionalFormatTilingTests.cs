using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R88-commands-styles-formatpainter-5-2 / R88-commands-styles-formatpainter-5-3:
// (a) Painting FROM a merged source cell must recreate the merge in the target, not just carry the
//     anchor's style uniformly across unmerged target cells.
// (b) A multi-cell source pattern must tile a conditional-format rule the same way it already tiles
//     direct style and data validation, instead of only populating the first source-sized tile.
public sealed class R88_FormatPainterMergeAndConditionalFormatTilingTests
{
    [Fact]
    public void CreateApplyFormatPainterCommand_MergedSourceOntoSingleTargetCell_RecreatesMergeInTarget()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var a1 = new CellAddress(sheet.Id, 1, 1); // A1
        var c1 = new CellAddress(sheet.Id, 1, 3); // C1
        var mergeRange = new GridRange(a1, c1); // A1:C1, a 1x3 merge
        var target = new CellAddress(sheet.Id, 1, 5); // E1, a single unmerged target cell

        var anchorStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(a1.Row, a1.Col, anchorStyle);
        sheet.AddMergedRegion(mergeRange);

        var command = FormatPainterCommandFactory.Create(wb, sheet, mergeRange, new GridRange(target, target));

        command.Apply(ctx).Success.Should().BeTrue();

        // Excel merges E1:G1 to match the source's 1x3 span before applying the anchor's formatting.
        var expectedMerge = new GridRange(target, new CellAddress(sheet.Id, 1, 7));
        sheet.MergedRegions.Should().Contain(expectedMerge);
    }

    [Fact]
    public void CreateApplyFormatPainterCommand_UnmergedSingleCellSource_DoesNotCreateMerge_NoRegression()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1); // A1, never merged
        var target = new CellAddress(sheet.Id, 1, 5); // E1

        var anchorStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(source.Row, source.Col, anchorStyle);

        var command = FormatPainterCommandFactory.Create(wb, sheet, source, new GridRange(target, target));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.MergedRegions.Should().BeEmpty();
        wb.GetStyle(sheet.GetCell(target)?.StyleId ?? sheet.GetStyleOnly(target.Row, target.Col)!.Value)
            .Bold.Should().BeTrue();
    }

    [Fact]
    public void CreateApplyFormatPainterCommand_MultiCellSourceConditionalFormat_TilesAcrossTargetRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // Source is a 2x2 block A1:B2; column A (A1:A2) carries a 3-color-scale conditional format.
        var sourceTopLeft = new CellAddress(sheet.Id, 1, 1); // A1
        var sourceBottomRight = new CellAddress(sheet.Id, 2, 2); // B2
        var sourceRange = new GridRange(sourceTopLeft, sourceBottomRight);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(sourceTopLeft, new CellAddress(sheet.Id, 2, 1)), // A1:A2
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinColor = new RgbColor(248, 105, 107),
            MidColor = new RgbColor(255, 235, 132),
            MaxColor = new RgbColor(99, 190, 123)
        });

        // Target D1:E4 is a 4-row x 2-col block: an exact 2x vertical tiling of the 2x2 source.
        var targetTopLeft = new CellAddress(sheet.Id, 1, 4); // D1
        var targetBottomRight = new CellAddress(sheet.Id, 4, 5); // E4
        var targetRange = new GridRange(targetTopLeft, targetBottomRight);

        var command = FormatPainterCommandFactory.Create(wb, sheet, sourceRange, targetRange);

        command.Apply(ctx).Success.Should().BeTrue();

        // First tile: D1:D2.
        sheet.ConditionalFormats.Should().Contain(rule =>
            rule.AppliesTo == new GridRange(
                new CellAddress(sheet.Id, 1, 4),
                new CellAddress(sheet.Id, 2, 4)) &&
            rule.RuleType == CfRuleType.ColorScale);

        // Second (repeated) tile: D3:D4 -- this is the tile the bug silently dropped.
        sheet.ConditionalFormats.Should().Contain(rule =>
            rule.AppliesTo == new GridRange(
                new CellAddress(sheet.Id, 3, 4),
                new CellAddress(sheet.Id, 4, 4)) &&
            rule.RuleType == CfRuleType.ColorScale);
    }

    [Fact]
    public void CreateApplyFormatPainterCommand_MultiCellSourceNoConditionalFormat_PaintsNoConditionalFormat_NoRegression()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceTopLeft = new CellAddress(sheet.Id, 1, 1);
        var sourceBottomRight = new CellAddress(sheet.Id, 2, 2);
        var sourceRange = new GridRange(sourceTopLeft, sourceBottomRight);
        var red = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 199, 206) });
        sheet.SetStyleOnly(1, 1, red);

        var targetTopLeft = new CellAddress(sheet.Id, 4, 4);
        var targetBottomRight = new CellAddress(sheet.Id, 7, 5);
        var targetRange = new GridRange(targetTopLeft, targetBottomRight);

        var command = FormatPainterCommandFactory.Create(wb, sheet, sourceRange, targetRange);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.ConditionalFormats.Should().BeEmpty();
    }
}
