using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r215: the two colour commands, which look like siblings and are not quite.
/// <para>
/// Both have two independently-gated blocks (fill, outline) and both clear <c>IsSourceLoaded</c> --
/// but the drawing shape clears it only when something is actually being updated, while the text box
/// clears it unconditionally. Copying one mirror to the other would report a no-op for a
/// source-loaded text box with nothing else to change, silently keeping its stale source XML.
/// </para>
/// </summary>
public sealed class R215_ColorCommandNoOpTests
{
    private static (Sheet Sheet, DrawingShapeModel Shape, TextBoxModel TextBox, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 3, 1) };
        sheet.DrawingShapes.Add(shape);
        sheet.TextBoxes.Add(textBox);
        return (sheet, shape, textBox, new TestCommandContext(workbook));
    }

    [Fact]
    public void ReApplyingAShapesOwnFill_ReportsNoOp()
    {
        var (sheet, shape, _, ctx) = Fixture();
        shape.HasFill = true;
        shape.FillColor = new CellColor(255, 0, 0);
        shape.GradientFillDirection = DrawingShapeGradientDirection.DiagonalDown;

        new SetDrawingShapeColorsCommand(
                sheet.Id, shape.Id, updateFill: true, fillColor: new CellColor(255, 0, 0),
                updateOutline: false, outlineColor: null)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void AShapeWithNothingToUpdate_ReportsNoOpEvenWhenSourceLoaded()
    {
        // The shape gates its IsSourceLoaded clear on (updateFill || updateOutline). With neither
        // requested, nothing happens at all -- so a source-loaded shape is still a no-op here.
        var (sheet, shape, _, ctx) = Fixture();
        shape.IsSourceLoaded = true;

        new SetDrawingShapeColorsCommand(
                sheet.Id, shape.Id, updateFill: false, fillColor: null,
                updateOutline: false, outlineColor: null)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
        shape.IsSourceLoaded.Should().BeTrue("nothing was updated, so the source XML still stands");
    }

    [Fact]
    public void ReApplyingAShapesOwnFillWhileSourceLoaded_DoesNotReportNoOp()
    {
        var (sheet, shape, _, ctx) = Fixture();
        shape.HasFill = true;
        shape.FillColor = new CellColor(255, 0, 0);
        shape.GradientFillDirection = DrawingShapeGradientDirection.DiagonalDown;
        shape.IsSourceLoaded = true;

        var outcome = new SetDrawingShapeColorsCommand(
                sheet.Id, shape.Id, updateFill: true, fillColor: new CellColor(255, 0, 0),
                updateOutline: false, outlineColor: null)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        shape.IsSourceLoaded.Should().BeFalse();
    }

    [Fact]
    public void ReApplyingATextBoxesOwnFill_ReportsNoOp()
    {
        var (sheet, _, textBox, ctx) = Fixture();
        textBox.HasFill = true;
        textBox.FillColor = new CellColor(0, 128, 0);

        new SetTextBoxColorsCommand(
                sheet.Id, textBox.Id, updateFill: true, fillColor: new CellColor(0, 128, 0),
                updateOutline: false, outlineColor: null)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ATextBoxWithNothingToUpdate_StillClearsItsSourceFlag()
    {
        // The asymmetry: unlike the shape, the text box clears IsSourceLoaded even when neither
        // block runs, so a source-loaded text box is NOT a no-op.
        var (sheet, _, textBox, ctx) = Fixture();
        textBox.IsSourceLoaded = true;

        var outcome = new SetTextBoxColorsCommand(
                sheet.Id, textBox.Id, updateFill: false, fillColor: null,
                updateOutline: false, outlineColor: null)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse("the text box clears IsSourceLoaded unconditionally");
        textBox.IsSourceLoaded.Should().BeFalse();
    }
}
