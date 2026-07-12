using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R31-io-drawing-anchor-deep-2: an <c>xdr:absoluteAnchor</c> (used for pictures/shapes/text boxes that are
/// pinned to a fixed sheet pixel position rather than tracking a cell) carries its position in
/// <see cref="XlsxDrawingAnchor.AbsoluteLeft"/>/<see cref="XlsxDrawingAnchor.AbsoluteTop"/>, with
/// <c>FromRowZeroBased</c>/<c>FromColumnZeroBased</c>/<c>FromRowOffset</c>/<c>FromColumnOffset</c> all pinned to
/// zero by the reader (see <c>XlsxWorksheetDrawingPartReader.TryReadAbsoluteAnchor</c>). Before this fix,
/// <see cref="XlsxDrawingAnchorApplier.ApplyToPicture"/>/<c>ApplyToTextBox</c>/<c>ApplyToShape</c> read only
/// <c>FromColumnOffset</c>/<c>FromRowOffset</c> (always 0 for an absolute anchor) and ignored
/// <c>AbsoluteLeft</c>/<c>AbsoluteTop</c> entirely, so every absolutely-anchored picture/shape/text box
/// collapsed onto the sheet origin (cell A1, offset 0,0) instead of its authored position -- unlike
/// <see cref="XlsxDrawingAnchorApplier.ApplyToChart"/>, which already read <c>AbsoluteLeft</c>/<c>AbsoluteTop</c>
/// correctly. Since the caller (<c>XlsxFileAdapter.LoadSheetXmlLayoutApplication</c>, out of this file's edit
/// scope) always anchors these object kinds to cell A1 (pixel origin (0,0)) for an absolute anchor, applying
/// the anchor's absolute pixel position as the sub-cell offset reproduces the correct on-sheet position.
/// </summary>
public sealed class XlsxDrawingAnchorApplierAbsoluteAnchorTests
{
    private static Sheet CreateSheet()
    {
        var workbook = new Workbook("AbsoluteAnchor");
        return workbook.AddSheet("Sheet1");
    }

    [Fact]
    public void ApplyToPicture_AbsoluteAnchor_UsesAbsolutePixelPositionAsOffset_InsteadOfCollapsingToOrigin()
    {
        var sheet = CreateSheet();
        var picture = new PictureModel();
        var anchor = new XlsxDrawingAnchor(
            ChartDrawingAnchorKind.Absolute,
            FromRowZeroBased: 0,
            FromColumnZeroBased: 0,
            FromRowOffset: 0,
            FromColumnOffset: 0,
            AbsoluteLeft: 500,
            AbsoluteTop: 300,
            ToRowZeroBased: null,
            ToColumnZeroBased: null,
            ToRowOffset: null,
            ToColumnOffset: null,
            Width: 120,
            Height: 80);

        XlsxDrawingAnchorApplier.ApplyToPicture(picture, anchor, sheet);

        picture.AnchorOffsetX.Should().Be(500,
            "the absolute anchor's pixel X position must be preserved, not dropped to 0 (collapsing onto A1)");
        picture.AnchorOffsetY.Should().Be(300,
            "the absolute anchor's pixel Y position must be preserved, not dropped to 0 (collapsing onto A1)");
        picture.Width.Should().Be(120);
        picture.Height.Should().Be(80);
    }

    [Fact]
    public void ApplyToPicture_CellAnchor_StillUsesFromOffset_UnaffectedBySiblingAbsoluteAnchorFix()
    {
        var sheet = CreateSheet();
        var picture = new PictureModel();
        var anchor = new XlsxDrawingAnchor(
            ChartDrawingAnchorKind.TwoCell,
            FromRowZeroBased: 2,
            FromColumnZeroBased: 3,
            FromRowOffset: 15,
            FromColumnOffset: 25,
            AbsoluteLeft: null,
            AbsoluteTop: null,
            ToRowZeroBased: 5,
            ToColumnZeroBased: 6,
            ToRowOffset: 0,
            ToColumnOffset: 0,
            Width: null,
            Height: null);

        XlsxDrawingAnchorApplier.ApplyToPicture(picture, anchor, sheet);

        picture.AnchorOffsetX.Should().Be(25, "a normal cell anchor has no AbsoluteLeft, so the from/colOff offset must still be used");
        picture.AnchorOffsetY.Should().Be(15, "a normal cell anchor has no AbsoluteTop, so the from/rowOff offset must still be used");
    }

    [Fact]
    public void ApplyToTextBox_AbsoluteAnchor_UsesAbsolutePixelPositionAsOffset()
    {
        var sheet = CreateSheet();
        var textBox = new TextBoxModel();
        var anchor = new XlsxDrawingAnchor(
            ChartDrawingAnchorKind.Absolute,
            FromRowZeroBased: 0,
            FromColumnZeroBased: 0,
            FromRowOffset: 0,
            FromColumnOffset: 0,
            AbsoluteLeft: 640,
            AbsoluteTop: 480,
            ToRowZeroBased: null,
            ToColumnZeroBased: null,
            ToRowOffset: null,
            ToColumnOffset: null,
            Width: 200,
            Height: 100);

        XlsxDrawingAnchorApplier.ApplyToTextBox(textBox, anchor, sheet);

        textBox.AnchorOffsetX.Should().Be(640);
        textBox.AnchorOffsetY.Should().Be(480);
    }

    [Fact]
    public void ApplyToShape_AbsoluteAnchor_UsesAbsolutePixelPositionAsOffset()
    {
        var sheet = CreateSheet();
        var shape = new DrawingShapeModel();
        var anchor = new XlsxDrawingAnchor(
            ChartDrawingAnchorKind.Absolute,
            FromRowZeroBased: 0,
            FromColumnZeroBased: 0,
            FromRowOffset: 0,
            FromColumnOffset: 0,
            AbsoluteLeft: 75,
            AbsoluteTop: 90,
            ToRowZeroBased: null,
            ToColumnZeroBased: null,
            ToRowOffset: null,
            ToColumnOffset: null,
            Width: 50,
            Height: 60);

        XlsxDrawingAnchorApplier.ApplyToShape(shape, anchor, sheet);

        shape.AnchorOffsetX.Should().Be(75);
        shape.AnchorOffsetY.Should().Be(90);
    }
}
