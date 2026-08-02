using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R114: <see cref="ChartLayoutRequestBuilder.TryBuild"/>'s embedded-data fallback (used when a
/// chart's series formulas are unresolvable named/cross-sheet ranges, see the r113 note on
/// <c>TryBuild</c>) must pick the LONGEST non-empty cached category list across all embedded series,
/// not merely the first one that happens to be non-empty. A too-short pick leaves later series'
/// extra points without real labels (and, via <c>ChartLayoutEngine.ResolveCategoryCount</c>, some
/// geometry loops bound the point count itself by the category count, dropping the extra points
/// entirely) even though a sibling series cached enough category text to cover them all.
/// </summary>
public sealed class R114_ChartLayoutRequestBuilderEmbeddedCategoryLengthTests
{
    private sealed class FakeTextMeasurer : ITextMeasurer
    {
        public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic) =>
            string.IsNullOrEmpty(text) ? TextSize.Empty : new TextSize(text.Length * fontSize * 0.5, fontSize);
    }

    private static readonly PlotRect Plot = new(8, 12, 360, 240);

    // Cell accessor is never consulted by the embedded-data fallback path, but TryBuild's
    // delegate parameter is non-nullable.
    private static bool NeverCalled(uint row, uint col, out double value, out string displayText)
    {
        value = 0;
        displayText = "";
        return false;
    }

    [Fact]
    public void TryBuild_EmbeddedFallback_PicksLongestCategoryCacheAcrossSeries_NotFirstNonEmpty()
    {
        // Series 0's own cached category list (2 entries) comes first in the list and is
        // non-empty, so the old "first non-empty" pick took it -- even though series 1 cached 5
        // category entries alongside its 5 values. Real Excel writes one shared <c:cat> formula
        // per chart, but a truncated/short individual series cache is exactly the disagreement
        // this fallback must tolerate (see ChartLayoutRequestBuilder.BuildFromEmbeddedData).
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(default, 1, 1), new CellAddress(default, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "Sales", ["North", "South"], [10d, 20d]),
                new ChartEmbeddedSeriesData(1, "Costs", ["North", "South", "East", "West", "Central"],
                    [5d, 15d, 25d, 35d, 45d]),
            ],
        };

        var request = ChartLayoutRequestBuilder.TryBuild(chart, Plot, NeverCalled, new FakeTextMeasurer());

        request.Should().NotBeNull();
        request!.Categories.Should().Equal("North", "South", "East", "West", "Central");
        request.Series.Should().HaveCount(2);
        request.Series[1].Values.Should().Equal(5d, 15d, 25d, 35d, 45d);
    }

    [Fact]
    public void TryBuild_EmbeddedFallback_AllSeriesAgreeOnCategoryCount_UsesSharedCategories()
    {
        // No-regression sibling: when every series' cached category list already agrees (the
        // ordinary case -- all series read the same <c:cat> formula), the longest-cache pick must
        // still yield exactly that shared list, unchanged from the prior first-non-empty behavior.
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(default, 1, 1), new CellAddress(default, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "Sales", ["North", "South", "East"], [10d, 20d, 30d]),
                new ChartEmbeddedSeriesData(1, "Costs", ["North", "South", "East"], [5d, 15d, 25d]),
            ],
        };

        var request = ChartLayoutRequestBuilder.TryBuild(chart, Plot, NeverCalled, new FakeTextMeasurer());

        request.Should().NotBeNull();
        request!.Categories.Should().Equal("North", "South", "East");
        request.Series[0].Values.Should().Equal(10d, 20d, 30d);
        request.Series[1].Values.Should().Equal(5d, 15d, 25d);
    }
}
