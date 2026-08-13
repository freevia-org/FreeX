using System.IO;
using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed class GridViewPivotHeaderDropdownSourceTests
{
    [Fact]
    public void GridView_ExposesAndRendersPivotHeaderDropdownButtons()
    {
        var propertiesPath = WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Properties.cs");
        var uiDirectory = Path.GetDirectoryName(propertiesPath)!;
        var properties = File.ReadAllText(propertiesPath);
        var eventsSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Events.cs"));
        var renderDispatch = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var rendering = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Rendering.AutoFilter.cs"));

        properties.Should().Contain("public static readonly DependencyProperty PivotHeaderDropdownsProperty");
        properties.Should().Contain("public IReadOnlyList<PivotHeaderDropdownTarget>? PivotHeaderDropdowns");
        properties.Should().Contain("public static readonly DependencyProperty PivotRowLabelAdornmentsProperty");
        properties.Should().Contain("public IReadOnlyList<PivotRowLabelAdornment>? PivotRowLabelAdornments");
        eventsSource.Should().Contain("public event Action<CellAddress, System.Windows.Point>? PivotHeaderDropdownRequested;");
        renderDispatch.Should().Contain("RenderPivotHeaderDropdownButtons(dc);");
        renderDispatch.Should().Contain("RenderPivotRowLabelAdornments(dc);");
        rendering.Should().Contain("private void RenderPivotHeaderDropdownButtons(DrawingContext dc)");
        rendering.Should().Contain("DrawPivotHeaderDropdownGlyph(dc, rect, button.IsActive)");
        rendering.Should().Contain("private const double PivotExpandCollapseButtonSize = 8;");
        rendering.Should().Contain("private const double PivotExpandCollapseButtonReserve = 15;");
        rendering.Should().Contain("private void RenderPivotRowLabelAdornments(DrawingContext dc)");
        rendering.Should().Contain("DrawPivotExpandCollapseButton(dc, rect, adornment.IsExpanded);");
        rendering.Should().Contain("private bool TryHitTestPivotHeaderDropdownButton(Point pos, out CellAddress headerCell)");

        File.Exists(Path.Combine(uiDirectory, "PivotHeaderDropdownButton.cs"))
            .Should().BeFalse("the WPF renderer should consume the presentation-owned header record");
        File.Exists(Path.Combine(uiDirectory, "PivotRowLabelAdornment.cs"))
            .Should().BeFalse("the WPF renderer should consume the presentation-owned row adornment record");
    }

    [Fact]
    public void PivotHeaderDropdownButtonRect_UsesExcelSizedPivotChromeWithoutChangingAutoFilterChrome()
    {
        var row = new RowMetric(4, Height: 20, TopOffset: 60);
        var col = new ColMetric(2, Width: 72, LeftOffset: 96);

        var rect = GridView.GetPivotHeaderDropdownButtonRect(
            row,
            col,
            rowHeaderWidth: 40,
            columnHeaderHeight: 24);

        rect.Should().Be(new Rect(191, 86.5, 15, 15));
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
