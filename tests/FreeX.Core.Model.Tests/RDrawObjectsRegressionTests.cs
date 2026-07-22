using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for group R-draw-objects findings:
/// K21 (RepositionShapeCommand must reset the stale sub-cell AnchorOffsetX/Y when the shape
/// moves to a new anchor cell, matching Excel's snap-to-new-cell-origin behavior instead of
/// leaving the old cell's fractional offset applied to the new cell), and
/// K22 (Duplicate Sheet must deep-copy every shape/textbox/picture property — including
/// AnchorOffsetX/Y, FlipHorizontal/Vertical, outline/arrowhead/WordArt styling, and shape text —
/// not just the small subset it previously copied).
/// </summary>
public sealed class RDrawObjectsRegressionTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // K21 — RepositionShapeCommand clears stale sub-cell offset on anchor change
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RepositionShapeCommand_MovingToNewAnchor_ResetsSubCellOffset()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var originalAnchor = new CellAddress(sheet.Id, 2, 2);
        var shape = new DrawingShapeModel
        {
            Anchor = originalAnchor,
            AnchorOffsetX = 37.5,
            AnchorOffsetY = 12.0
        };
        sheet.DrawingShapes.Add(shape);

        var newAnchor = new CellAddress(sheet.Id, 5, 5);
        var command = new RepositionShapeCommand(sheet.Id, shape.Id, newAnchor);
        command.Apply(ctx).Success.Should().BeTrue();

        shape.Anchor.Should().Be(newAnchor);
        shape.AnchorOffsetX.Should().Be(0,
            because: "the old cell's fractional pixel offset must not drift onto the new anchor cell");
        shape.AnchorOffsetY.Should().Be(0);
    }

    [Fact]
    public void RepositionShapeCommand_MovingToSameAnchor_PreservesSubCellOffset()
    {
        // A no-op reposition (same anchor cell, e.g. a resize that doesn't cross a cell boundary)
        // must not clobber an existing sub-cell offset.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var anchor = new CellAddress(sheet.Id, 2, 2);
        var shape = new DrawingShapeModel
        {
            Anchor = anchor,
            AnchorOffsetX = 37.5,
            AnchorOffsetY = 12.0
        };
        sheet.DrawingShapes.Add(shape);

        var command = new RepositionShapeCommand(sheet.Id, shape.Id, anchor);
        command.Apply(ctx).Success.Should().BeTrue();

        shape.AnchorOffsetX.Should().Be(37.5);
        shape.AnchorOffsetY.Should().Be(12.0);
    }

    [Fact]
    public void RepositionShapeCommandRevert_RestoresPreviousAnchorAndOffset()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var originalAnchor = new CellAddress(sheet.Id, 2, 2);
        var shape = new DrawingShapeModel
        {
            Anchor = originalAnchor,
            AnchorOffsetX = 37.5,
            AnchorOffsetY = 12.0
        };
        sheet.DrawingShapes.Add(shape);

        var newAnchor = new CellAddress(sheet.Id, 5, 5);
        var command = new RepositionShapeCommand(sheet.Id, shape.Id, newAnchor);
        command.Apply(ctx);
        command.Revert(ctx);

        shape.Anchor.Should().Be(originalAnchor);
        shape.AnchorOffsetX.Should().Be(37.5,
            because: "undo must restore the exact pre-move sub-cell offset, not just the anchor cell");
        shape.AnchorOffsetY.Should().Be(12.0);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // K22 — Duplicate Sheet deep-copies shape/textbox/picture properties
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DuplicateSheet_CopiesShapeSubCellOffsetAndFlipState()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            AnchorOffsetX = 22.0,
            AnchorOffsetY = 8.0,
            FlipHorizontal = true,
            FlipVertical = true
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copiedShape = wb.Sheets[1].DrawingShapes.Should().ContainSingle().Subject;
        copiedShape.AnchorOffsetX.Should().Be(22.0);
        copiedShape.AnchorOffsetY.Should().Be(8.0);
        copiedShape.FlipHorizontal.Should().BeTrue();
        copiedShape.FlipVertical.Should().BeTrue();
    }

    [Fact]
    public void DuplicateSheet_CopiesShapeTextAndWordArtAndOutlineProperties()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var textColor = new CellColor(10, 20, 30);
        var gradientEnd = new CellColor(200, 100, 50);
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            ShapeText = "Hello",
            ShapeTextFontSizePoints = 18,
            ShapeTextBold = true,
            ShapeTextItalic = true,
            ShapeTextUnderline = true,
            ShapeTextColor = textColor,
            ShapeTextHAlign = DrawingShapeTextHAlign.Right,
            ShapeTextVAnchor = DrawingShapeTextVAnchor.Bottom,
            ShapeTextWrap = false,
            IsWordArt = true,
            WarpPreset = "textWave1",
            ShapeTextGradientEndColor = gradientEnd,
            ShapeTextGradientAngle = 2700000,
            ShapeTextOutlineColor = textColor,
            ShapeTextOutlineWidthPoints = 1.5,
            OutlineWidthPoints = 3.0,
            OutlineHasNoFill = true,
            OutlineDash = DrawingShapeOutlineDash.DashDot,
            HeadArrowhead = new DrawingArrowhead(DrawingArrowheadType.Triangle, DrawingArrowheadSize.Large),
            TailArrowhead = new DrawingArrowhead(DrawingArrowheadType.Oval, DrawingArrowheadSize.Small)
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copiedShape = wb.Sheets[1].DrawingShapes.Should().ContainSingle().Subject;
        copiedShape.ShapeText.Should().Be("Hello");
        copiedShape.ShapeTextFontSizePoints.Should().Be(18);
        copiedShape.ShapeTextBold.Should().BeTrue();
        copiedShape.ShapeTextItalic.Should().BeTrue();
        copiedShape.ShapeTextUnderline.Should().BeTrue();
        copiedShape.ShapeTextColor.Should().Be(textColor);
        copiedShape.ShapeTextHAlign.Should().Be(DrawingShapeTextHAlign.Right);
        copiedShape.ShapeTextVAnchor.Should().Be(DrawingShapeTextVAnchor.Bottom);
        copiedShape.ShapeTextWrap.Should().BeFalse();
        copiedShape.IsWordArt.Should().BeTrue();
        copiedShape.WarpPreset.Should().Be("textWave1");
        copiedShape.ShapeTextGradientEndColor.Should().Be(gradientEnd);
        copiedShape.ShapeTextGradientAngle.Should().Be(2700000);
        copiedShape.ShapeTextOutlineColor.Should().Be(textColor);
        copiedShape.ShapeTextOutlineWidthPoints.Should().Be(1.5);
        copiedShape.OutlineWidthPoints.Should().Be(3.0);
        copiedShape.OutlineHasNoFill.Should().BeTrue();
        copiedShape.OutlineDash.Should().Be(DrawingShapeOutlineDash.DashDot);
        copiedShape.HeadArrowhead.Should().Be(new DrawingArrowhead(DrawingArrowheadType.Triangle, DrawingArrowheadSize.Large));
        copiedShape.TailArrowhead.Should().Be(new DrawingArrowhead(DrawingArrowheadType.Oval, DrawingArrowheadSize.Small));
    }

    [Fact]
    public void DuplicateSheet_CopiesTextBoxSubCellOffsetAndFlipState()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "Note",
            AnchorOffsetX = 15.0,
            AnchorOffsetY = 5.0,
            FlipHorizontal = true,
            FlipVertical = true
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copiedTextBox = wb.Sheets[1].TextBoxes.Should().ContainSingle().Subject;
        copiedTextBox.Text.Should().Be("Note");
        copiedTextBox.AnchorOffsetX.Should().Be(15.0);
        copiedTextBox.AnchorOffsetY.Should().Be(5.0);
        copiedTextBox.FlipHorizontal.Should().BeTrue();
        copiedTextBox.FlipVertical.Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // backlog textbox-6-2 -- Duplicate Sheet deep-copies a text box's rich-text formatting
    // (font size/bold/italic/color/alignment), mirroring the identical DuplicateSheet_
    // CopiesShapeTextAndWordArtAndOutlineProperties fix already shipped for DrawingShapeModel above.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DuplicateSheet_CopiesTextBoxTextFormattingProperties()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var textColor = new CellColor(10, 20, 30);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "Warning",
            TextFontFamily = "Georgia",
            TextFontSizePoints = 18,
            TextBold = true,
            TextItalic = true,
            TextColor = textColor,
            TextHAlign = DrawingShapeTextHAlign.Right,
            TextVAnchor = DrawingShapeTextVAnchor.Bottom
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copiedTextBox = wb.Sheets[1].TextBoxes.Should().ContainSingle().Subject;
        copiedTextBox.Text.Should().Be("Warning");
        copiedTextBox.TextFontFamily.Should().Be("Georgia");
        copiedTextBox.TextFontSizePoints.Should().Be(18);
        copiedTextBox.TextBold.Should().BeTrue();
        copiedTextBox.TextItalic.Should().BeTrue();
        copiedTextBox.TextColor.Should().Be(textColor);
        copiedTextBox.TextHAlign.Should().Be(DrawingShapeTextHAlign.Right);
        copiedTextBox.TextVAnchor.Should().Be(DrawingShapeTextVAnchor.Bottom);
    }

    [Fact]
    public void DuplicateSheet_CopiesPlainUnformattedTextBox_NoRegression()
    {
        // No-regression sibling: a text box that never had any of the new formatting fields set
        // must still duplicate cleanly, with those fields left at their harmless defaults on the
        // copy (not e.g. thrown exceptions or garbage values).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "Plain note"
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copiedTextBox = wb.Sheets[1].TextBoxes.Should().ContainSingle().Subject;
        copiedTextBox.Text.Should().Be("Plain note");
        copiedTextBox.TextFontFamily.Should().BeNull();
        copiedTextBox.TextFontSizePoints.Should().Be(0);
        copiedTextBox.TextBold.Should().BeFalse();
        copiedTextBox.TextItalic.Should().BeFalse();
        copiedTextBox.TextColor.Should().BeNull();
        copiedTextBox.TextHAlign.Should().Be(DrawingShapeTextHAlign.Left);
        copiedTextBox.TextVAnchor.Should().Be(DrawingShapeTextVAnchor.Top);
    }

    [Fact]
    public void DuplicateSheet_CopiesPictureSubCellOffsetAndFlipState()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            AnchorOffsetX = 9.0,
            AnchorOffsetY = 3.0,
            FlipHorizontal = true,
            FlipVertical = true
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copiedPicture = wb.Sheets[1].Pictures.Should().ContainSingle().Subject;
        copiedPicture.AnchorOffsetX.Should().Be(9.0);
        copiedPicture.AnchorOffsetY.Should().Be(3.0);
        copiedPicture.FlipHorizontal.Should().BeTrue();
        copiedPicture.FlipVertical.Should().BeTrue();
    }
}
