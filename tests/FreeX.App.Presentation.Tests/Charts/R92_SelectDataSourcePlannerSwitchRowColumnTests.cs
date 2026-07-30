using FluentAssertions;
using FreeX.App.Presentation.Charts.Editing;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// R92-app-chart-data-edit-5-2: toggling the Select Data dialog's "Switch Row/Column" checkbox used
/// to leave the Series/Axis-Labels preview lists showing the pre-toggle (non-transposed) inference
/// forever -- only OK's ChangeChartSourceCommand actually flipped chart.SeriesInRows, with no live
/// preview of what the transposed chart would look like. <see cref="SelectDataSourcePlanner.InferPreviewEntries"/>
/// now accepts a switchRowColumn flag that transposes series/categories (series from rows, categories
/// from columns) -- the literal row&lt;-&gt;col swap of the existing column-major inference.
/// </summary>
public sealed class R92_SelectDataSourcePlannerSwitchRowColumnTests
{
    [Fact]
    public void InferPreviewEntries_SwitchRowColumn_ProducesSeriesPerRowAndCategoriesPerColumn()
    {
        // Sheet1!$A$1:$D$3 : 4 cols x 3 rows. firstColumnIsCategories=true skips row 1 (categories)
        // and col A (series "header" column) in the transposed view -> 2 series (rows 2,3), each
        // spanning cols B..D, and 3 categories (cols B,C,D).
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "Sheet1!$A$1:$D$3",
            firstColumnIsCategories: true,
            switchRowColumn: true);

        preview.Series.Should().HaveCount(2);
        preview.Series[0].ValuesRangeText.Should().Be("Sheet1!$B$2:$D$2");
        preview.Series[1].ValuesRangeText.Should().Be("Sheet1!$B$3:$D$3");
        preview.Categories.Should().HaveCount(3);
        preview.CategoryRangeText.Should().Be("Sheet1!$B$1:$D$1");
    }

    [Fact]
    public void InferPreviewEntries_SwitchRowColumn_NoCategories_UsesWholeRangeForSeriesAndCategories()
    {
        // Same range, but firstColumnIsCategories=false: nothing is skipped on either axis.
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "Sheet1!$A$1:$D$3",
            firstColumnIsCategories: false,
            switchRowColumn: true);

        preview.Series.Should().HaveCount(3); // one per row: 1,2,3
        preview.Series[0].ValuesRangeText.Should().Be("Sheet1!$A$1:$D$1");
        preview.Categories.Should().HaveCount(4); // one per column: A,B,C,D
        preview.CategoryRangeText.Should().BeEmpty();
    }

    [Fact]
    public void InferPreviewEntries_SwitchRowColumnFalse_MatchesNonTransposedBehavior()
    {
        // No-regression sibling: passing switchRowColumn explicitly as false must reproduce exactly
        // the pre-existing (default) column-major inference.
        var transposedOff = SelectDataSourcePlanner.InferPreviewEntries(
            "Sheet1!$A$1:$D$4",
            firstColumnIsCategories: true,
            switchRowColumn: false);
        var defaulted = SelectDataSourcePlanner.InferPreviewEntries(
            "Sheet1!$A$1:$D$4",
            firstColumnIsCategories: true);

        transposedOff.Series.Should().BeEquivalentTo(defaulted.Series);
        transposedOff.Categories.Should().BeEquivalentTo(defaulted.Categories);
        transposedOff.CategoryRangeText.Should().Be(defaulted.CategoryRangeText);
        transposedOff.Series.Should().HaveCount(3);
        transposedOff.Series[0].ValuesRangeText.Should().Be("Sheet1!$B$2:$B$4");
    }

    [Fact]
    public void InferPreviewEntries_SwitchRowColumn_UnparseableRange_UsesFallbackSeries()
    {
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "garbage",
            firstColumnIsCategories: true,
            switchRowColumn: true);

        preview.Series.Should().HaveCount(1);
        preview.Series[0].ValuesRangeText.Should().Be("garbage");
    }
}
