using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r245: the two commands r216 re-ranked, and the round that made them decidable.
/// <para>
/// r216 moved SetColumnWidth and SetRowHeight from tier 2 to tier 3 after noticing that
/// <c>DrawingAnchorResizeHelper.ResizeFor*Range</c> RESIZES every anchored shape, picture and text
/// box while reading like a snapshot line -- so deciding "no change" meant predicting that resize,
/// and the field-count ranking that put them in tier 2 was wrong about them.
/// </para>
/// <para>
/// It is not a prediction any more. The resize helper hands back each object WITH its old size, so
/// what looked like the obstacle is in fact the record: comparing what the helper returned against
/// what the objects now hold is exact. The drawing tests below are the point of the round -- a guard
/// that compared only column widths would pass the first two and fail these.
/// </para>
/// </summary>
public sealed class R245_SheetLayoutNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void SettingAColumnToTheWidthItAlreadyHas_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        new SetColumnWidthCommand(sheet.Id, 2, 4, 90).Apply(ctx)
            .IsNoOp.Should().BeFalse("the first resize is a real edit");

        new SetColumnWidthCommand(sheet.Id, 2, 4, 90).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ChangingAColumnWidth_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        new SetColumnWidthCommand(sheet.Id, 2, 4, 90).Apply(ctx);

        var outcome = new SetColumnWidthCommand(sheet.Id, 2, 4, 120).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.ColumnWidths[3].Should().Be(120);
    }

    [Fact]
    public void ReSettingTheSameWidthWithAShapeAnchoredInRange_StillReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 3),
            Width = 50,
            Height = 20,
        });
        new SetColumnWidthCommand(sheet.Id, 2, 4, 90).Apply(ctx);
        var widthAfterFirst = sheet.DrawingShapes[0].Width;

        new SetColumnWidthCommand(sheet.Id, 2, 4, 90).Apply(ctx).IsNoOp.Should().BeTrue();

        sheet.DrawingShapes[0].Width.Should().Be(widthAfterFirst);
    }

    [Fact]
    public void ChangingTheWidthWithAShapeAnchoredInRange_IsARealEdit()
    {
        // The case the r216 note was worried about: the shape is resized by the helper, and that is
        // a change even where the guard's other clauses might not notice.
        var (sheet, ctx) = Fixture();
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 3),
            Width = 50,
            Height = 20,
        });
        new SetColumnWidthCommand(sheet.Id, 2, 4, 90).Apply(ctx);

        new SetColumnWidthCommand(sheet.Id, 2, 4, 200).Apply(ctx).IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void SettingARowToTheHeightItAlreadyHas_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        new SetRowHeightCommand(sheet.Id, 2, 4, 30).Apply(ctx);

        new SetRowHeightCommand(sheet.Id, 2, 4, 30).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ChangingARowHeight_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        new SetRowHeightCommand(sheet.Id, 2, 4, 30).Apply(ctx);

        new SetRowHeightCommand(sheet.Id, 2, 4, 45).Apply(ctx).IsNoOp.Should().BeFalse();
    }
}
