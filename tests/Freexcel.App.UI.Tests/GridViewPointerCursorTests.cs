using FluentAssertions;
using System.IO;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewPointerCursorTests
{
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
