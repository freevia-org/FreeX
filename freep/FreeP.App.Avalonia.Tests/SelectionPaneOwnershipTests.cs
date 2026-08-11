using System.IO;

public sealed class SelectionPaneOwnershipTests
{
    [Fact]
    public void AvaloniaSelectionPaneOwnsOnlyNativeProjectionEventsAndLifecycle()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "SelectionPane.cs"));

        source.Should().Contain("new PresentationSelectionPaneSession(editor)");
        source.Should().Contain("_session.CreateItemSession(item.ShapeId)");
        source.Should().Contain("itemSession.CommitRename(rename.Text)");
        source.Should().Contain("ApplyTransition(itemSession.Select())");
        source.Should().Contain("ApplyTransition(itemSession.CancelRename())");
        source.Should().Contain("itemSession.ToggleVisibility()");
        source.Should().Contain("itemSession.MoveTowardFront()");
        source.Should().Contain("itemSession.MoveTowardBack()");
        source.Should().Contain("item.VisibilityActionText");
        source.Should().Contain("PresentationPaneAccessibilityAdapter.ApplyItem(");
        source.Should().Contain("PresentationPaneAccessibilityPlanner.PlanItem(");
        source.Should().Contain("PresentationPaneAccessibilityPlanner.BuildShapeKey(item.ShapeId)");
        source.Should().Contain("Avalonia.Controls");
        source.Should().Contain("rename.LostFocus");
        source.Should().Contain("Key.Enter");
        source.Should().Contain("Key.Escape");
        source.Should().NotContain("var committed");
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

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
