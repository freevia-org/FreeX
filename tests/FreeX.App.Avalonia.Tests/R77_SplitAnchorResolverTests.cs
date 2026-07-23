using FluentAssertions;

using FreeX.App.Avalonia;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Pure unit tests for <see cref="SplitAnchorResolver"/> -- the Avalonia twin of WPF's
/// SplitViewBtn_Click split-position decision (MainWindow.ViewCommands.cs, ~lines 372-418).
/// No headless window construction: this exercises only the static decision logic that
/// MainWindow.ParityWires.cs's SplitPanesAtActiveCell wires up.
/// </summary>
public sealed class R77_SplitAnchorResolverTests
{
    [Fact]
    public void Resolve_MultiCellSelectionActiveCellC5_SplitsAtRow5Col3()
    {
        var (splitRow, splitColumn) = SplitAnchorResolver.Resolve(
            activeRow: 5,
            activeCol: 3,
            wasSplit: false);

        splitRow.Should().Be(5u);
        splitColumn.Should().Be(3u);
    }

    [Fact]
    public void Resolve_ActiveCellA1_WithViewportMidpoint_FallsBackToMidpoint()
    {
        // Regression for the Avalonia parity gap: before the fix, an A1 active cell resolved
        // both splitRow/splitColumn to null with no fallback, making View > Split a silent no-op
        // (unlike Excel/WPF, whose Split command is never a no-op -- R60-commands-freeze-split-6-2).
        var rowMetrics = BuildRowMetrics(20); // midpoint index 20/2=10 -> row 11 (1-based, index 10)
        var colMetrics = BuildColMetrics(8);  // midpoint index 8/2=4 -> col 5 (1-based, index 4)

        var (splitRow, splitColumn) = SplitAnchorResolver.Resolve(
            activeRow: 1,
            activeCol: 1,
            wasSplit: false,
            viewportRowMetrics: rowMetrics,
            viewportColMetrics: colMetrics);

        splitRow.Should().Be(rowMetrics[rowMetrics.Count / 2].Row,
            "A1 gives no row/column context, so Split must fall back to the viewport's midpoint row");
        splitColumn.Should().Be(colMetrics[colMetrics.Count / 2].Col,
            "A1 gives no row/column context, so Split must fall back to the viewport's midpoint column");
    }

    [Fact]
    public void Resolve_ActiveCellA1_NoViewportInfo_ReturnsNullNullGracefully()
    {
        var (splitRow, splitColumn) = SplitAnchorResolver.Resolve(
            activeRow: 1,
            activeCol: 1,
            wasSplit: false);

        splitRow.Should().BeNull();
        splitColumn.Should().BeNull();
    }

    [Fact]
    public void Resolve_WasSplitTrue_AlwaysReturnsNullNullToClear()
    {
        // Even with a perfectly good active cell and viewport, an existing split must be cleared,
        // not recomputed.
        var (splitRow, splitColumn) = SplitAnchorResolver.Resolve(
            activeRow: 5,
            activeCol: 3,
            wasSplit: true,
            viewportRowMetrics: BuildRowMetrics(20),
            viewportColMetrics: BuildColMetrics(8));

        splitRow.Should().BeNull();
        splitColumn.Should().BeNull();
    }

    [Fact]
    public void Resolve_ActiveCellOnRow1Only_SplitsColumnOnlyNoFallback()
    {
        // Row 1 (nothing above to split off) but column > 1 -- only the column resolves, and since
        // splitColumn is non-null the A1-fallback branch must not fire at all.
        var (splitRow, splitColumn) = SplitAnchorResolver.Resolve(
            activeRow: 1,
            activeCol: 4,
            wasSplit: false,
            viewportRowMetrics: BuildRowMetrics(20),
            viewportColMetrics: BuildColMetrics(8));

        splitRow.Should().BeNull();
        splitColumn.Should().Be(4u);
    }

    [Fact]
    public void Resolve_ActiveCellA1_RowMetricsFallbackIndependentOfColMetrics()
    {
        // Mirrors WPF's independent row/column fallback checks: a viewport with >1 visible row but
        // no column metrics at all still gets a row-only split fallback.
        var rowMetrics = BuildRowMetrics(20);

        var (splitRow, splitColumn) = SplitAnchorResolver.Resolve(
            activeRow: 1,
            activeCol: 1,
            wasSplit: false,
            viewportRowMetrics: rowMetrics,
            viewportColMetrics: null);

        splitRow.Should().Be(rowMetrics[rowMetrics.Count / 2].Row);
        splitColumn.Should().BeNull();
    }

    [Fact]
    public void Resolve_ActiveCellA1_SingleRowOrColumnMetric_NoFallback()
    {
        // A viewport with only 1 visible row/column has no meaningful midpoint (mirrors WPF's
        // `RowMetrics.Count > 1` / `ColMetrics.Count > 1` guards) -- must not throw or fabricate one.
        var (splitRow, splitColumn) = SplitAnchorResolver.Resolve(
            activeRow: 1,
            activeCol: 1,
            wasSplit: false,
            viewportRowMetrics: BuildRowMetrics(1),
            viewportColMetrics: BuildColMetrics(1));

        splitRow.Should().BeNull();
        splitColumn.Should().BeNull();
    }

    private static List<RowMetric> BuildRowMetrics(int count)
    {
        var metrics = new List<RowMetric>(count);
        for (var i = 0; i < count; i++)
            metrics.Add(new RowMetric((uint)(i + 1), 20.0, i * 20.0));
        return metrics;
    }

    private static List<ColMetric> BuildColMetrics(int count)
    {
        var metrics = new List<ColMetric>(count);
        for (var i = 0; i < count; i++)
            metrics.Add(new ColMetric((uint)(i + 1), 64.0, i * 64.0));
        return metrics;
    }
}
