using System.IO;
using FluentAssertions;

namespace FreeX.App.UI.Tests;

public sealed class GridViewPivotHeaderDropdownSourceTests
{
    [Fact]
    public void GridView_ExposesAndRendersPivotHeaderDropdownButtons()
    {
        var properties = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var eventsSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Events.cs"));
        var renderDispatch = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var rendering = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Rendering.AutoFilter.cs"));

        properties.Should().Contain("public static readonly DependencyProperty PivotHeaderDropdownsProperty");
        properties.Should().Contain("public IReadOnlyList<PivotHeaderDropdownButton>? PivotHeaderDropdowns");
        properties.Should().Contain("public static readonly DependencyProperty PivotRowLabelAdornmentsProperty");
        properties.Should().Contain("public IReadOnlyList<PivotRowLabelAdornment>? PivotRowLabelAdornments");
        eventsSource.Should().Contain("public event Action<CellAddress, System.Windows.Point>? PivotHeaderDropdownRequested;");
        renderDispatch.Should().Contain("RenderPivotHeaderDropdownButtons(dc);");
        renderDispatch.Should().Contain("RenderPivotRowLabelAdornments(dc);");
        rendering.Should().Contain("private void RenderPivotHeaderDropdownButtons(DrawingContext dc)");
        rendering.Should().Contain("DrawAutoFilterGlyph(dc, rect, button.IsActive)");
        rendering.Should().Contain("private void RenderPivotRowLabelAdornments(DrawingContext dc)");
        rendering.Should().Contain("DrawPivotExpandCollapseButton(dc, rect, adornment.IsExpanded);");
        rendering.Should().Contain("private bool TryHitTestPivotHeaderDropdownButton(Point pos, out CellAddress headerCell)");
    }

    [Fact]
    public void GridView_ClicksRenderedPivotHeaderDropdownBeforeSelectionGestures()
    {
        var input = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Input.cs"));
        var mouseDown = input[
            input.IndexOf("protected override void OnMouseLeftButtonDown", StringComparison.Ordinal)..];

        mouseDown.Should().Contain("TryHitTestPivotHeaderDropdownButton(pos, out var pivotHeaderCell)");
        mouseDown.Should().Contain("PivotHeaderDropdownRequested?.Invoke(pivotHeaderCell, pos);");
        mouseDown.IndexOf("TryHitTestPivotHeaderDropdownButton(pos, out var pivotHeaderCell)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseDown.IndexOf("if (SelectedRange.HasValue && IsOnAutofillHandle(pos))", StringComparison.Ordinal));
    }
}
