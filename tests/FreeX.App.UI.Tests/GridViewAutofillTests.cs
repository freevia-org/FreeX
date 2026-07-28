using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

// The pure fill-handle/autofill intent math now lives in the shared
// FreeX.App.Presentation.GridInteraction.GridAutofillPlanner and is covered by
// FreeX.App.Presentation.Tests.GridInteraction.GridAutofillPlannerTests. These tests cover the
// thin App.UI wrappers (GridView.ConstrainAutofillTarget / GridView.CalculateAutofillEdgeScrollIntent)
// and the host cursor wiring that delegate to that shared planner.
public sealed class GridViewAutofillTests
{
    [Fact]
    public void ConstrainAutofillTarget_PrefersVerticalAxisWhenDragExtendsFartherDown()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        var target = GridView.ConstrainAutofillTarget(
            source,
            new CellAddress(sheet, 8, 6));

        target.Should().Be(new CellAddress(sheet, 8, 3));
        GridAutofillPlanner.ConstrainTarget(source, new CellAddress(sheet, 8, 6))
            .Should()
            .Be(target);
    }

    [Fact]
    public void ConstrainAutofillTarget_PrefersHorizontalAxisWhenDragExtendsFartherRight()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        var target = GridView.ConstrainAutofillTarget(
            source,
            new CellAddress(sheet, 5, 9));

        target.Should().Be(new CellAddress(sheet, 3, 9));
    }

    [Fact]
    public void ConstrainAutofillTarget_SupportsDraggingAboveOrLeftOfSource()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 4, 4),
            new CellAddress(sheet, 6, 6));

        GridView.ConstrainAutofillTarget(source, new CellAddress(sheet, 1, 5))
            .Should()
            .Be(new CellAddress(sheet, 1, 6));

        GridView.ConstrainAutofillTarget(source, new CellAddress(sheet, 5, 1))
            .Should()
            .Be(new CellAddress(sheet, 6, 1));
    }

    [Fact]
    public void CalculateAutofillEdgeScrollIntent_RequestsHorizontalScrollNearRightEdge()
    {
        GridView.CalculateAutofillEdgeScrollIntent(
                pointerX: 795,
                pointerY: 120,
                width: 800,
                height: 600,
                rowHeaderWidth: 48,
                columnHeaderHeight: 24)
            .Should()
            .Be(new GridAutoScrollRequest(1, 0));
    }

    [Fact]
    public void CalculateAutofillEdgeScrollIntent_IgnoresPointerAwayFromEdges()
    {
        GridView.CalculateAutofillEdgeScrollIntent(
                pointerX: 400,
                pointerY: 300,
                width: 800,
                height: 600,
                rowHeaderWidth: 48,
                columnHeaderHeight: 24)
            .Should()
            .Be(new GridAutoScrollRequest(0, 0));
    }

    [Fact]
    public void GridViewMouseMove_UsesCrossCursorOverAutofillHandle()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var cursorAssignment = source[
            source.IndexOf("var (target, _, _, _) = HitTestResize(pos);", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest", StringComparison.Ordinal)];

        cursorAssignment.Should().Contain("IsOnAutofillHandle(pos) ? Cursors.Cross");
    }

    [Fact]
    public void GridViewGestureHitTestsAreGatedBySharedFillHandleOption()
    {
        var hitTesting = AppUiSourceTestSupport.ReadAppUiSources("GridView.HitTesting.cs");
        var input = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.Selection.cs");

        hitTesting.Should().Contain("EnableFillHandleAndCellDragAndDrop && GridAutofillPlanner.IsOnHandle");
        input.Should().Contain("EnableFillHandleAndCellDragAndDrop &&");
        rendering.Should().Contain("drawHandle: EnableFillHandleAndCellDragAndDrop");
    }
}
