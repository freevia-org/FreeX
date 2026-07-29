using FluentAssertions;
using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FormulaBar;

/// <summary>
/// R91-formula-editing-assist-5-3: the range-highlight overlay for a formula reference had no way
/// to be dragged to resize the reference (the overlay Border was constructed with
/// IsHitTestVisible = false, and no drag-resize logic existed anywhere). These tests exercise the
/// new portable planner behind the corner-drag resize directly.
/// </summary>
public sealed class R91_FormulaReferenceDragResizePlannerTests
{
    private static readonly SheetId Sheet = new(Guid.NewGuid());

    [Fact]
    public void ComputeResizedRange_DraggingBottomRightCornerOutward_ExpandsRange()
    {
        var fixedCorner = new CellAddress(Sheet, 1, 1); // A1
        var draggedTo = new CellAddress(Sheet, 3, 3);   // C3

        var range = FormulaReferenceDragResizePlanner.ComputeResizedRange(fixedCorner, draggedTo);

        range.Start.Should().Be(new CellAddress(Sheet, 1, 1));
        range.End.Should().Be(new CellAddress(Sheet, 3, 3));
    }

    [Fact]
    public void ComputeResizedRange_DraggingPastTheFixedCorner_NormalizesRegardlessOfDirection()
    {
        // Dragging the "bottom-right" handle up and to the left of the fixed top-left corner must
        // still produce a normalized range (Start <= End on both axes), matching Excel.
        var fixedCorner = new CellAddress(Sheet, 5, 5);
        var draggedTo = new CellAddress(Sheet, 2, 2);

        var range = FormulaReferenceDragResizePlanner.ComputeResizedRange(fixedCorner, draggedTo);

        range.Start.Should().Be(new CellAddress(Sheet, 2, 2));
        range.End.Should().Be(new CellAddress(Sheet, 5, 5));
    }

    [Fact]
    public void ApplyResize_ReplacesOriginalReferenceTokenWithResizedRange()
    {
        var text = "=SUM(A1:B2)";
        // "A1:B2" starts at index 5, length 5.
        var newRange = new GridRange(new CellAddress(Sheet, 1, 1), new CellAddress(Sheet, 3, 3));

        var (newText, caretIndex) = FormulaReferenceDragResizePlanner.ApplyResize(
            text, textStart: 5, textLength: 5, newRange, useR1C1ReferenceStyle: false);

        newText.Should().Be("=SUM(A1:C3)");
        caretIndex.Should().Be("=SUM(A1:C3".Length);
    }

    [Fact]
    public void ApplyResize_R1C1Style_FormatsResizedRangeAsR1C1()
    {
        var text = "=SUM(R1C1:R2C2)";
        var newRange = new GridRange(new CellAddress(Sheet, 1, 1), new CellAddress(Sheet, 3, 3));

        var (newText, _) = FormulaReferenceDragResizePlanner.ApplyResize(
            text, textStart: 5, textLength: 9, newRange, useR1C1ReferenceStyle: true);

        newText.Should().Be("=SUM(R1C1:R3C3)");
    }

    // ── No-regression sibling ────────────────────────────────────────────────

    [Fact]
    public void ApplyResize_PreservesSurroundingFormulaText_NoRegression()
    {
        var text = "=IF(SUM(A1:B2)>10,\"yes\",\"no\")";
        var newRange = new GridRange(new CellAddress(Sheet, 1, 1), new CellAddress(Sheet, 4, 2));

        var (newText, _) = FormulaReferenceDragResizePlanner.ApplyResize(
            text, textStart: 8, textLength: 5, newRange, useR1C1ReferenceStyle: false);

        newText.Should().Be("=IF(SUM(A1:B4)>10,\"yes\",\"no\")");
    }
}
