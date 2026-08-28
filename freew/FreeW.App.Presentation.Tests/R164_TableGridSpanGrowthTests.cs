using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// r164 remediation, unbounded declared quantity. A cell's horizontal span is a COUNT the file
/// declares, and both readers accepted any value above 1 -- DocxReader from <c>w:gridSpan</c>,
/// HtmlFileAdapter from <c>colspan</c>. One cell declaring <c>colspan="2000000000"</c> made
/// <see cref="TableGridProjection.TableWidth"/> report two billion columns, and the layout pass then
/// allocated one <c>double</c> per column: measured at 15.3 GB and still running after 15s.
///
/// Two layers, because the sum matters as much as the individual value: NormalizeSpan bounds one
/// cell's span (every layout path funnels through the canonical projection), and AllocateColumnWidths
/// bounds their total, which is what actually sizes the array.
/// </summary>
public sealed class R164_TableGridSpanGrowthTests
{
    private static Table TableWithSpan(int span)
    {
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].GridSpan = span;
        return table;
    }

    [Fact]
    public void TableWidth_AbsurdGridSpan_IsBounded()
    {
        TableGridProjection.TableWidth(TableWithSpan(2_000_000_000))
            .Should().BeLessThanOrEqualTo(TableGridProjection.MaximumGridSpan);
    }

    [Fact]
    public void AllocateColumnWidths_AbsurdGridSpan_AllocatesABoundedArrayInsteadOfGigabytes()
    {
        var table = TableWithSpan(2_000_000_000);

        var widths = TableColumnLayoutPlanner.AllocateColumnWidths(
            table,
            TableGridProjection.TableWidth(table),
            availableWidthDip: 600);

        widths.Length.Should().BeLessThanOrEqualTo(TableColumnLayoutPlanner.MaximumLaidOutColumns);
    }

    [Fact]
    public void AllocateColumnWidths_AbsurdColumnCountPassedDirectly_IsStillBounded()
    {
        // The planner is public and takes the count as a parameter, so it must bound its own
        // allocation even when a caller has not been through NormalizeSpan.
        var widths = TableColumnLayoutPlanner.AllocateColumnWidths(
            Table.Create(1, 1),
            columnCount: int.MaxValue,
            availableWidthDip: 600);

        widths.Length.Should().Be(TableColumnLayoutPlanner.MaximumLaidOutColumns);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(63)]
    public void OrdinarySpans_AreUnchanged(int span)
    {
        // Sibling/no-regression: the cap sits far above Word's own 63-column ceiling, so every real
        // table projects exactly as before.
        TableGridProjection.NormalizeSpan(span).Should().Be(span);
        TableGridProjection.TableWidth(TableWithSpan(span)).Should().Be(span);
    }

    [Fact]
    public void AnOrdinaryTable_StillAllocatesOneWidthPerColumn()
    {
        var table = Table.Create(2, 3);

        var widths = TableColumnLayoutPlanner.AllocateColumnWidths(
            table,
            TableGridProjection.TableWidth(table),
            availableWidthDip: 600);

        widths.Should().HaveCount(3);
        widths.Sum().Should().BeApproximately(600, 0.001);
    }
}
