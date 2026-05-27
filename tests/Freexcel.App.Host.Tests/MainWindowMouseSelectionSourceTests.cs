using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowMouseSelectionSourceTests
{
    [Fact]
    public void DragSelectionRequestsEdgeAutoScrollDuringMouseMove()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.Selection.cs"));

        var mouseMoveStart = source.IndexOf("private void SheetGrid_MouseMove", StringComparison.Ordinal);
        var helperStart = source.IndexOf("private void RequestSelectionDragAutoScroll", StringComparison.Ordinal);
        var previewStart = source.IndexOf("private void UpdateCommentPreview", StringComparison.Ordinal);

        mouseMoveStart.Should().BeGreaterThanOrEqualTo(0);
        helperStart.Should().BeGreaterThan(mouseMoveStart);
        previewStart.Should().BeGreaterThan(helperStart);

        var mouseMove = source[mouseMoveStart..helperStart];
        mouseMove.Should().Contain("var pos = e.GetPosition(SheetGrid);");
        mouseMove.Should().Contain("var hitAddr = HitTestCell(pos);");
        mouseMove.Should().Contain("RequestSelectionDragAutoScroll(pos);");

        var helper = source[helperStart..previewStart];
        helper.Should().Contain("Freexcel.App.UI.GridView.CalculateAutofillEdgeScrollIntent");
        helper.Should().Contain("SheetGrid.ActualRowHeaderWidth");
        helper.Should().Contain("SheetGrid.EffectiveColHeaderHeight");
        helper.Should().Contain("if (request.HasAnyDirection)");
        helper.Should().Contain("OnAutofillEdgeScrollRequested(request);");
    }
}
