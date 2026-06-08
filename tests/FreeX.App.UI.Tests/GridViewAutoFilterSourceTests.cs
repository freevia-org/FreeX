using System.IO;
using FluentAssertions;

namespace FreeX.App.UI.Tests;

public sealed class GridViewAutoFilterSourceTests
{
    [Fact]
    public void GridView_ExposesAutoFilterRangeAndRendersHeaderDropdownButtons()
    {
        var properties = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var renderDispatch = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var rendering = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Rendering.AutoFilter.cs"));

        properties.Should().Contain("public static readonly DependencyProperty AutoFilterRangeProperty");
        renderDispatch.Should().Contain("RenderAutoFilterButtons(dc);");
        rendering.Should().Contain("private void RenderAutoFilterButtons(DrawingContext dc)");
        rendering.Should().Contain("if (Viewport is null || AutoFilterRange is not { } range)");
        properties.Should().Contain("public static readonly DependencyProperty ActiveAutoFilterColumnsProperty");
        rendering.Should().Contain("ActiveAutoFilterColumns?.Contains(column.Col - range.Start.Col) == true");
        rendering.Should().Contain("DrawAutoFilterGlyph(dc, rect, isActive)");
        rendering.Should().Contain("ActiveAutoFilterGlyphBrush");
    }

    [Fact]
    public void GridView_ClicksRenderedAutoFilterButtonBeforeSelectionGestures()
    {
        var input = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Input.cs"));
        var eventsSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Events.cs"));
        var mouseDown = input[
            input.IndexOf("protected override void OnMouseLeftButtonDown", StringComparison.Ordinal)..];

        eventsSource.Should().Contain("public event Action<CellAddress, System.Windows.Point>? AutoFilterDropdownRequested;");
        mouseDown.Should().Contain("TryHitTestAutoFilterButton(pos, out var autoFilterHeaderCell)");
        mouseDown.Should().Contain("AutoFilterDropdownRequested?.Invoke(autoFilterHeaderCell, pos);");
        mouseDown.IndexOf("TryHitTestAutoFilterButton(pos, out var autoFilterHeaderCell)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseDown.IndexOf("if (SelectedRange.HasValue && IsOnAutofillHandle(pos))", StringComparison.Ordinal));
    }
}
