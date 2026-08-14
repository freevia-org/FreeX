using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class SelectionPaneOwnershipTests
{
    [Fact]
    public void WpfSelectionPaneOwnsOnlyNativeProjectionEventsAndLifecycle()
    {
        var source = ReadHostSource("SelectionPane.cs");

        AssertSharedItemSessionRouting(source);
        source.Should().Contain("System.Windows.Controls");
        source.Should().Contain("rename.LostFocus");
        source.Should().Contain("Key.Enter");
        source.Should().Contain("Key.Escape");
    }

    private static void AssertSharedItemSessionRouting(string source)
    {
        source.Should().Contain("new PresentationSelectionPaneSession(editor)");
        source.Should().Contain("PresentationSelectionPaneFormSession<UIElement>");
        source.Should().Contain("new PresentationSelectionPaneItemFormSession(");
        source.Should().Contain("_formSession.ApplyTransition");
        source.Should().Contain("itemForm.CommitRename(rename.Text, restoreName => rename.Text = restoreName)");
        source.Should().Contain("itemForm.Select()");
        source.Should().Contain("itemForm.CancelRename()");
        source.Should().Contain("itemForm.ToggleVisibility()");
        source.Should().Contain("itemForm.MoveTowardFront()");
        source.Should().Contain("itemForm.MoveTowardBack()");
        source.Should().Contain("item.VisibilityActionText");
        source.Should().Contain("PresentationPaneAccessibilityAdapter.ApplyItem(");
        source.Should().Contain("itemForm.AccessibilityPlan");
        source.Should().NotContain("var committed");
        source.Should().NotContain("item.IsSelected");
        source.Should().NotContain("PresentationPaneAccessibilityPlanner.PlanItem(");
        source.Should().NotContain("PresentationPaneAccessibilityPlanner.BuildShapeKey(item.ShapeId)");
        source.Should().NotContain("_session.SelectShape(");
        source.Should().NotContain("_session.RenameShape(");
        source.Should().NotContain("_session.ToggleShapeVisibility(");
        source.Should().NotContain("_session.MoveShapeInReadingOrder(");
        source.Should().NotContain("PresentationSelectionPaneMoveDirection");
        source.Should().NotContain("offset:");
        source.Should().NotContain("item.IsHidden ?");
        source.Should().NotContain(".SetShapeName(");
        source.Should().NotContain(".ToggleShapeHidden(");
        source.Should().NotContain(".MoveSelectedShapeInReadingOrder(");
    }

    private static string ReadHostSource(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", fileName));
    }
}
