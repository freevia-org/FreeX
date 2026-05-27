using FluentAssertions;
using System.IO;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewPointerCursorTests
{
    [Fact]
    public void MouseMoveUsesObjectDragCursorOverSelectedObject()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var hoverCursorBlock = source[
            source.IndexOf("var (target, _, _) = HitTestResize(pos);", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest", StringComparison.Ordinal)];

        hoverCursorBlock.Should().Contain("selectedObjectDragKind = HitTestObjectHandle(pos, GetSelectedObjectRect());");
        hoverCursorBlock.Should().Contain("Cursor = selectedObjectDragKind != ObjectDragKind.None ? ObjectDragCursor(selectedObjectDragKind)");
    }

    [Fact]
    public void MouseMoveUsesMoveCursorOverUnselectedObjectBody()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var hoverCursorBlock = source[
            source.IndexOf("var (target, _, _) = HitTestResize(pos);", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest", StringComparison.Ordinal)];

        hoverCursorBlock.Should().Contain("var hoveringObjectBody = selectedObjectDragKind == ObjectDragKind.None");
        hoverCursorBlock.Should().Contain("HitTestDrawingObject(pos).Id != Guid.Empty");
        hoverCursorBlock.Should().Contain(": hoveringObjectBody ? Cursors.SizeAll");
    }

    [Fact]
    public void RightClickObjectRoutesContextMenuToObjectAnchor()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var objectDragSource = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.ObjectDrag.cs"));
        var rightClickBlock = inputSource[
            inputSource.IndexOf("protected override void OnMouseRightButtonDown", StringComparison.Ordinal)..];

        objectDragSource.Should().Contain("Rect Rect, CellAddress Anchor");
        rightClickBlock.Should().Contain("var objectHit = HitTestDrawingObject(pos);");
        rightClickBlock.Should().Contain("SelectedObjectId = objectHit.Id;");
        rightClickBlock.Should().Contain("SelectedObjectKind = objectHit.Kind;");
        rightClickBlock.Should().Contain("ContextMenuRequested?.Invoke(objectHit.Anchor, pos);");
    }

    [Fact]
    public void SplitPaneScrollbarDragPreservesOrientationCursor()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var dragBlock = source[
            source.IndexOf("if (_splitPaneScrollbarDragging)", StringComparison.Ordinal)..
            source.IndexOf("if (_autofillDragging", StringComparison.Ordinal)];

        dragBlock.Should().Contain("_splitPaneScrollbarDragSource?.Orientation == SplitPaneScrollbarOrientation.Horizontal");
        dragBlock.Should().Contain("? Cursors.SizeWE");
        dragBlock.Should().Contain("_splitPaneScrollbarDragSource?.Orientation == SplitPaneScrollbarOrientation.Vertical");
        dragBlock.Should().Contain("? Cursors.SizeNS");
    }

    [Fact]
    public void AutofillDragMouseMoveKeepsCrossCursorAndHandlesEvent()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var dragBlock = source[
            source.IndexOf("if (_autofillDragging && Viewport != null && _autofillSourceRange.HasValue)", StringComparison.Ordinal)..
            source.IndexOf("if (_resizeTarget == ResizeTarget.Column)", StringComparison.Ordinal)];

        dragBlock.Should().Contain("Cursor = Cursors.Cross;");
        dragBlock.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void AutofillMouseUpInvalidatesAfterClearingPreview()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var releaseStart = source.LastIndexOf("if (_autofillDragging)", StringComparison.Ordinal);
        var resizeStart = source.IndexOf("if (_resizeTarget != ResizeTarget.None)", releaseStart, StringComparison.Ordinal);
        var releaseBlock = source[releaseStart..resizeStart];

        releaseBlock.Should().Contain("_autofillSourceRange = null;");
        releaseBlock.Should().Contain("_autofillTarget      = null;");
        releaseBlock.Should().Contain("InvalidateVisual();");
        releaseBlock.IndexOf("InvalidateVisual();", StringComparison.Ordinal)
            .Should().BeGreaterThan(releaseBlock.IndexOf("_autofillTarget      = null;", StringComparison.Ordinal));
    }

    [Fact]
    public void MouseLeavePreservesCursorDuringCapturedDrags()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var mouseLeave = source[
            source.IndexOf("protected override void OnMouseLeave", StringComparison.Ordinal)..];

        mouseLeave.Should().Contain("_objectDragKind == ObjectDragKind.None");
        mouseLeave.Should().Contain("!_autofillDragging");
        mouseLeave.Should().Contain("_resizeTarget == ResizeTarget.None");
        mouseLeave.Should().Contain("Cursor = null;");
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
