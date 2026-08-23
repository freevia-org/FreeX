using FluentAssertions;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Rendering;

public sealed class ViewportMetricEndpointLookupTests
{
    [Fact]
    public void Finds_first_matching_row_and_column_endpoints()
    {
        var rows = new[]
        {
            new RowMetric(2, 20, 20),
            new RowMetric(3, 20, 40),
            new RowMetric(3, 20, 400),
        };
        var columns = new[]
        {
            new ColMetric(4, 40, 40),
            new ColMetric(5, 40, 80),
            new ColMetric(5, 40, 800),
        };

        ViewportMetricEndpointLookup.TryFindRows(rows, 2, 3, out var firstRow, out var lastRow)
            .Should().BeTrue();
        ViewportMetricEndpointLookup.TryFindColumns(columns, 4, 5, out var firstColumn, out var lastColumn)
            .Should().BeTrue();

        firstRow.Should().BeSameAs(rows[0]);
        lastRow.Should().BeSameAs(rows[1]);
        firstColumn.Should().BeSameAs(columns[0]);
        lastColumn.Should().BeSameAs(columns[1]);
    }

    [Fact]
    public void Supports_a_single_endpoint_and_rejects_missing_edges()
    {
        var rows = new[] { new RowMetric(2, 20, 20) };
        var columns = new[] { new ColMetric(4, 40, 40) };

        ViewportMetricEndpointLookup.TryFindRows(rows, 2, 2, out var firstRow, out var lastRow)
            .Should().BeTrue();
        firstRow.Should().BeSameAs(lastRow);

        ViewportMetricEndpointLookup.TryFindColumns(columns, 4, 5, out _, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Autofill_and_page_margin_planners_adopt_the_shared_lookup()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var autofill = File.ReadAllText(Path.Combine(
            root, "src", "FreeX.App.Presentation", "GridInteraction", "GridAutofillPlanner.cs"));
        var margins = File.ReadAllText(Path.Combine(
            root, "src", "FreeX.App.Presentation", "PageLayout", "PageMarginGuideLayoutPlanner.cs"));

        foreach (var source in new[] { autofill, margins })
        {
            source.Should().Contain("ViewportMetricEndpointLookup.TryFindRows(")
                .And.Contain("ViewportMetricEndpointLookup.TryFindColumns(")
                .And.NotContain("private static bool TryFindRow")
                .And.NotContain("private static bool TryFindColumn");
        }
    }
}
