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
