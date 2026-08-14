using System.IO;

public sealed class SelectionPaneOwnershipTests
{
    [Fact]
    public void AvaloniaSelectionPaneOwnsOnlyNativeProjectionEventsAndLifecycle()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "SelectionPane.cs"));

        source.Should().Contain("new PresentationSelectionPaneSession(editor)");
        source.Should().Contain("PresentationSelectionPaneFormSession<Control>");
        source.Should().Contain("PresentationSelectionPaneItemFormSession(");
        source.Should().Contain("_formSession.ApplyTransition");
        source.Should().Contain("itemForm.CommitRename(rename.Text,");
        source.Should().Contain("itemForm.Select()");
        source.Should().Contain("itemForm.CancelRename()");
        source.Should().Contain("itemForm.ToggleVisibility()");
        source.Should().Contain("itemForm.MoveTowardFront()");
        source.Should().Contain("itemForm.MoveTowardBack()");
        source.Should().Contain("item.VisibilityActionText");
        source.Should().Contain("PresentationPaneAccessibilityAdapter.ApplyItem(");
        source.Should().Contain("itemForm.AccessibilityPlan");
        source.Should().Contain("Avalonia.Controls");
        source.Should().Contain("rename.LostFocus");
        source.Should().Contain("Key.Enter");
        source.Should().Contain("Key.Escape");
        source.Should().NotContain("var committed");
        source.Should().NotContain("_session.SelectShape(");
        source.Should().NotContain("_session.RenameShape(");
        source.Should().NotContain("_session.ToggleShapeVisibility(");
        source.Should().NotContain("_session.MoveShapeInReadingOrder(");
        source.Should().NotContain("itemSession.Select()");
        source.Should().NotContain("itemSession.CommitRename(");
        source.Should().NotContain("PresentationPaneAccessibilityPlanner.PlanItem(");
        source.Should().NotContain("PresentationPaneAccessibilityPlanner.BuildShapeKey(");
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
