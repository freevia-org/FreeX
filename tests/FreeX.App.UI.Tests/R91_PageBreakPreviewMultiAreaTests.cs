using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R91-render-frozen-print-titles-5-2: the WPF Page Break Preview / Page Layout overlay
/// (GridView.Overlays.cs) used to compute its preview range as <c>PrintArea ?? PagePreviewRange</c>
/// -- where <c>GridView.PrintArea</c> only ever carries the FIRST configured
/// <c>_xlnm.Print_Area</c> region (<c>Sheet.PrintArea</c> is the first of <c>Sheet.PrintAreas</c>) --
/// so a sheet with more than one configured print area got its later regions dimmed/excluded from the
/// overlay even though they DO print and appear in the real PDF/print export
/// (<c>WorkbookExportPrintPlanner</c>) and in the Avalonia shell's own overlay. These tests cover
/// <see cref="GridView.ResolvePageBreakPreviewRanges"/>, the exact range-selection logic
/// <c>RenderWorksheetViewOverlay</c> feeds into <c>PageBreakPreviewLayoutPlanner.Calculate</c>.
/// </summary>
public sealed class R91_PageBreakPreviewMultiAreaTests
{
    private static GridRange Range(uint fromRow, uint fromCol, uint toRow, uint toCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(new CellAddress(sheetId, fromRow, fromCol), new CellAddress(sheetId, toRow, toCol));
    }

    [Fact]
    public void MultiAreaPrintAreas_PrefersFullListOverSingleRangeFallbacks()
    {
        var areaA = Range(1, 1, 10, 4);
        var areaB = Range(1, 6, 10, 8);
        var singlePrintArea = Range(1, 1, 5, 5);

        var resolved = GridView.ResolvePageBreakPreviewRanges(
            printAreas: [areaA, areaB],
            printArea: singlePrintArea,
            pagePreviewRange: null);

        resolved.Should().BeEquivalentTo([areaA, areaB],
            "with a configured multi-area print range every area must be paginated/un-masked, not just the first");
    }

    [Fact]
    public void NoMultiAreaList_FallsBackToSinglePrintArea_NoRegression()
    {
        var singlePrintArea = Range(1, 1, 5, 5);

        var resolved = GridView.ResolvePageBreakPreviewRanges(
            printAreas: null,
            printArea: singlePrintArea,
            pagePreviewRange: null);

        resolved.Should().ContainSingle().Which.Should().Be(singlePrintArea);
    }

    [Fact]
    public void EmptyMultiAreaList_FallsBackToSinglePrintArea_NoRegression()
    {
        // Sheet.PrintAreas is never null (empty list when unset) -- confirm an empty (as opposed to
        // null) list is treated the same as "no multi-area list configured".
        var singlePrintArea = Range(1, 1, 5, 5);

        var resolved = GridView.ResolvePageBreakPreviewRanges(
            printAreas: [],
            printArea: singlePrintArea,
            pagePreviewRange: null);

        resolved.Should().ContainSingle().Which.Should().Be(singlePrintArea);
    }

    [Fact]
    public void NoPrintAreaEither_FallsBackToPagePreviewRange_NoRegression()
    {
        var pagePreviewRange = Range(1, 1, 20, 20);

        var resolved = GridView.ResolvePageBreakPreviewRanges(
            printAreas: null,
            printArea: null,
            pagePreviewRange: pagePreviewRange);

        resolved.Should().ContainSingle().Which.Should().Be(pagePreviewRange);
    }

    [Fact]
    public void NothingConfigured_ReturnsNull_NoRegression()
    {
        GridView.ResolvePageBreakPreviewRanges(printAreas: null, printArea: null, pagePreviewRange: null)
            .Should().BeNull();
    }
}
