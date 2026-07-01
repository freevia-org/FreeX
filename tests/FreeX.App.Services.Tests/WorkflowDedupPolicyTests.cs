using System.Globalization;
using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class WorkflowDedupPolicyTests
{
    [Fact]
    public void DialogNumericTextPolicy_FormatsParsesAndReadsOptionalPointValues()
    {
        DialogNumericTextPolicy.FormatPoints(12.345, CultureInfo.InvariantCulture)
            .Should().Be("12.35");

        DialogNumericTextPolicy.TryParsePositiveDouble("12.5", CultureInfo.InvariantCulture, out var positive)
            .Should().BeTrue();
        positive.Should().Be(12.5);

        DialogNumericTextPolicy.TryParsePositiveDouble("0", CultureInfo.InvariantCulture, out _)
            .Should().BeFalse();

        DialogNumericTextPolicy.TryParseOptionalNonNegativeDouble(
                isChecked: false,
                text: "invalid",
                CultureInfo.InvariantCulture,
                out var optional)
            .Should().BeTrue();
        optional.Should().BeNull();
    }

    [Fact]
    public void ChartDataGridPlanner_PadsTrimsAndSnapshotsDetachedRectangularData()
    {
        var planner = ChartDataGridPlanner.Create(
            new[] { "Q1", "Q2", "Q3" },
            new[] { "Sales", "Budget" },
            new[]
            {
                new double?[] { 1, 2 },
                new double?[] { 4, null, 6, 99 },
            });

        planner.ValuesSnapshot()[0].Should().Equal(new double?[] { 1, 2, null });
        planner.ValuesSnapshot()[1].Should().Equal(new double?[] { 4, null, 6 });

        planner.AddSeries("Series 3");
        planner.AddCategory("Q4");
        planner.SetValue(2, 3, 12);

        var categories = planner.CategoriesSnapshot();
        var values = planner.ValuesSnapshot();
        planner.SetCategory(3, "Changed");
        planner.SetValue(2, 3, 99);

        categories.Should().Equal("Q1", "Q2", "Q3", "Q4");
        values[2].Should().Equal(new double?[] { null, null, null, 12 });
    }

    [Fact]
    public void TableGridGeometryPlanner_ResolvesMergedContinuationsToAnchorAndBuildsSpannedRects()
    {
        var geometry = new TableGridGeometry(
            new[] { 10.0, 20.0, 30.0 },
            new[] { 5.0, 7.0 },
            new IReadOnlyList<TableGridCell>[]
            {
                new[]
                {
                    new TableGridCell(GridSpan: 2, RowSpan: 2, HMerge: false, VMerge: false),
                    new TableGridCell(GridSpan: 1, RowSpan: 1, HMerge: true, VMerge: false),
                    new TableGridCell(GridSpan: 1, RowSpan: 1, HMerge: false, VMerge: false),
                },
                new[]
                {
                    new TableGridCell(GridSpan: 1, RowSpan: 1, HMerge: false, VMerge: true),
                    new TableGridCell(GridSpan: 1, RowSpan: 1, HMerge: false, VMerge: true),
                    new TableGridCell(GridSpan: 1, RowSpan: 1, HMerge: false, VMerge: false),
                },
            });

        TableGridGeometryPlanner.HitTest(geometry, originX: 100, originY: 50, x: 115, y: 58)
            .Should().Be(new TableGridHit(0, 0));

        TableGridGeometryPlanner.GetCellRect(geometry, originX: 100, originY: 50, row: 0, col: 0)
            .Should().Be(new TableGridRect(100, 50, 30, 12));
    }

    [Fact]
    public void WorkflowCommandCatalogPolicy_ReturnsDescriptorByIdAndRejectsUnknownIds()
    {
        var descriptors = new[]
        {
            new TestDescriptor(1, "One"),
            new TestDescriptor(2, "Two"),
        };

        WorkflowCommandCatalogPolicy.GetById(descriptors, 2, descriptor => descriptor.Id)
            .Label.Should().Be("Two");

        Action act = () => WorkflowCommandCatalogPolicy.GetById(descriptors, 99, descriptor => descriptor.Id);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed record TestDescriptor(int Id, string Label);
}
