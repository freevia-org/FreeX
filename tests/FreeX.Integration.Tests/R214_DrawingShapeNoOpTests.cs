using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r214: the first two of the tier-2 FreeX group -- drawing-shape effect and gradient. Both write
/// more than the property the user chose, and the tests below pin the extras.
/// <para>
/// The one that matters is <c>IsSourceLoaded</c>. Both commands clear it unconditionally, and that
/// flag decides whether a shape's original XML is replayed verbatim on save. A comparison that
/// looked only at the visible property would call a source-loaded shape "unchanged" and leave the
/// flag set, silently keeping the old XML -- a suppressed real edit, not just a missed no-op.
/// </para>
/// </summary>
public sealed class R214_DrawingShapeNoOpTests
{
    private static (Sheet Sheet, DrawingShapeModel Shape, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            // Kind defaults to Rectangle, which is what the gradient guard requires (it rejects Line).
        };
        sheet.DrawingShapes.Add(shape);
        return (sheet, shape, new TestCommandContext(workbook));
    }

    [Fact]
    public void ReApplyingAShapesOwnEffect_ReportsNoOp()
    {
        var (sheet, shape, ctx) = Fixture();
        shape.EffectPreset = DrawingShapeEffectPreset.Shadow;
        shape.HasShadowEffect = true;

        new SetDrawingShapeEffectCommand(sheet.Id, shape.Id, DrawingShapeEffectPreset.Shadow)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ReApplyingTheSameEffectOnASourceLoadedShape_DoesNotReportNoOp()
    {
        // The trap: the effect matches, but Apply still clears IsSourceLoaded, which decides whether
        // the shape's original XML is replayed on save.
        var (sheet, shape, ctx) = Fixture();
        shape.EffectPreset = DrawingShapeEffectPreset.Shadow;
        shape.HasShadowEffect = true;
        shape.IsSourceLoaded = true;

        var outcome = new SetDrawingShapeEffectCommand(
                sheet.Id, shape.Id, DrawingShapeEffectPreset.Shadow)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        shape.IsSourceLoaded.Should().BeFalse("the source XML must stop being replayed");
    }

    [Fact]
    public void ChangingAShapesEffect_DoesNotReportNoOp()
    {
        var (sheet, shape, ctx) = Fixture();
        shape.EffectPreset = DrawingShapeEffectPreset.None;

        var outcome = new SetDrawingShapeEffectCommand(
                sheet.Id, shape.Id, DrawingShapeEffectPreset.Shadow)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        shape.HasShadowEffect.Should().BeTrue();
    }

    [Fact]
    public void ReApplyingAShapesOwnGradient_ReportsNoOp()
    {
        var (sheet, shape, ctx) = Fixture();
        shape.HasFill = true;
        shape.FillColor = new CellColor(255, 0, 0);
        shape.GradientFillEndColor = new CellColor(0, 0, 255);
        shape.GradientFillDirection = DrawingShapeGradientDirection.Horizontal;

        new SetDrawingShapeGradientCommand(
                sheet.Id,
                shape.Id,
                new CellColor(255, 0, 0),
                new CellColor(0, 0, 255),
                DrawingShapeGradientDirection.Horizontal)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ReApplyingTheSameGradientOverAThemeFill_DoesNotReportNoOp()
    {
        // Apply also forces FillThemeColor to null, so a theme-linked fill is a real change even
        // when all three gradient values already match.
        var (sheet, shape, ctx) = Fixture();
        shape.HasFill = true;
        shape.FillColor = new CellColor(255, 0, 0);
        shape.GradientFillEndColor = new CellColor(0, 0, 255);
        shape.GradientFillDirection = DrawingShapeGradientDirection.Horizontal;
        shape.FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1);

        var outcome = new SetDrawingShapeGradientCommand(
                sheet.Id,
                shape.Id,
                new CellColor(255, 0, 0),
                new CellColor(0, 0, 255),
                DrawingShapeGradientDirection.Horizontal)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        shape.FillThemeColor.Should().BeNull();
    }
}
