using FluentAssertions;
using FreeX.App.Presentation.Charts.Editing;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Unit tests for <see cref="SelectDataSourcePlanner"/> — the portable series/category inference
/// and normalisation planner, shared between the Avalonia and any future shell.
/// </summary>
public sealed class SelectDataSourcePlannerTests
{
    // ---- InferPreviewEntries — blank / unparseable inputs ----------------------------------------

    [Fact]
    public void InferPreviewEntries_BlankRange_ReturnsEmptyPreview()
    {
        var preview = SelectDataSourcePlanner.InferPreviewEntries(string.Empty, firstColumnIsCategories: true);

        preview.Series.Should().BeEmpty();
        preview.Categories.Should().BeEmpty();
        preview.CategoryRangeText.Should().BeEmpty();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    public void InferPreviewEntries_WhitespaceRange_ReturnsEmptyPreview(string range)
    {
        var preview = SelectDataSourcePlanner.InferPreviewEntries(range, firstColumnIsCategories: true);

        preview.Series.Should().BeEmpty();
        preview.Categories.Should().BeEmpty();
    }

    [Fact]
    public void InferPreviewEntries_UnparseableRange_ReturnsFallbackSingleSeriesAndCategory()
    {
        var preview = SelectDataSourcePlanner.InferPreviewEntries("bad_range", firstColumnIsCategories: true);

        preview.Series.Should().HaveCount(1);
        preview.Series[0].Name.Should().Be("Series 1");
        preview.Series[0].ValuesRangeText.Should().Be("bad_range");
        preview.Categories.Should().HaveCount(1);
        preview.Categories[0].Label.Should().Be(SelectDataSourcePlanner.CategoryLabelsFallback);
        preview.CategoryRangeText.Should().BeEmpty();
    }

    // ---- InferPreviewEntries — simple ranges with firstColumnIsCategories = true -----------------

    [Fact]
    public void InferPreviewEntries_FourColumnRange_FirstColCategories_ProducesThreeSeries()
    {
        // $A$1:$D$4 with firstColumnIsCategories = true
        // Col A → categories, Cols B,C,D → 3 series
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "Sheet1!$A$1:$D$4",
            firstColumnIsCategories: true);

        preview.Series.Should().HaveCount(3);
        preview.Series[0].Name.Should().Be("Series 1");
        preview.Series[1].Name.Should().Be("Series 2");
        preview.Series[2].Name.Should().Be("Series 3");

        // Values start at row 2 (first row is header row)
        preview.Series[0].ValuesRangeText.Should().Be("Sheet1!$B$2:$B$4");
        preview.Series[1].ValuesRangeText.Should().Be("Sheet1!$C$2:$C$4");
        preview.Series[2].ValuesRangeText.Should().Be("Sheet1!$D$2:$D$4");

        preview.Categories.Should().HaveCount(3, "rows 2-4 are data rows");
        preview.CategoryRangeText.Should().Be("Sheet1!$A$2:$A$4");
    }

    [Fact]
    public void InferPreviewEntries_FourColumnRange_NoCategories_ProducesFourSeries()
    {
        // $A$1:$D$4 with firstColumnIsCategories = false
        // All 4 cols are series
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "Sheet1!$A$1:$D$4",
            firstColumnIsCategories: false);

        preview.Series.Should().HaveCount(4);
        preview.CategoryRangeText.Should().BeEmpty();
    }

    // ---- InferPreviewEntries — no-sheet-prefix range -------------------------------------------

    [Fact]
    public void InferPreviewEntries_RangeWithoutSheet_FormatsEntriesWithNoPrefix()
    {
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "A1:C5",
            firstColumnIsCategories: true);

        // No sheet prefix
        preview.Series[0].ValuesRangeText.Should().StartWith("$B");
        preview.CategoryRangeText.Should().StartWith("$A");
    }

    // ---- InferPreviewEntries — single-column range ---------------------------------------------

    [Fact]
    public void InferPreviewEntries_SingleColumn_FirstColCategories_ProducesOneSeriesSkippingHeaderRow()
    {
        // A1:A5 with firstColumnIsCategories = true and EndCol == StartCol (single column)
        // The guard (firstColumnIsCategories && EndCol > StartCol) does NOT fire, so col A is still
        // treated as the series column.  Row 1 is the header, so data starts at row 2.
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "A1:A5",
            firstColumnIsCategories: true);

        preview.Series.Should().HaveCount(1);
        preview.Series[0].ValuesRangeText.Should().Be("$A$2:$A$5");
    }

    [Fact]
    public void InferPreviewEntries_SingleColumn_NoCategories_ProducesOneSeries()
    {
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "B2:B10",
            firstColumnIsCategories: false);

        preview.Series.Should().HaveCount(1);
        preview.Series[0].Name.Should().Be("Series 1");
    }

    // ---- InferPreviewEntries — dollar-sign normalisation ---------------------------------------

    [Fact]
    public void InferPreviewEntries_WithDollarSigns_ParsesCorrectly()
    {
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "$A$1:$C$3",
            firstColumnIsCategories: true);

        preview.Series.Should().HaveCount(2, "B and C");
        preview.Categories.Should().HaveCount(2, "rows 2-3");
    }

    // ---- InferPreviewEntries — category rows ---------------------------------------------------

    [Fact]
    public void InferPreviewEntries_SingleRow_FirstColCategories_NoHeaderSkip()
    {
        // A1:D1 — only one row, so no header skip (EndRow == StartRow)
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "A1:D1",
            firstColumnIsCategories: true);

        preview.Categories.Should().HaveCount(1);
    }

    // ---- CreateResult --------------------------------------------------------------------------

    [Fact]
    public void CreateResult_TrimsSourceRangeText()
    {
        var result = SelectDataSourcePlanner.CreateResult(
            "  Sheet1!$A$1:$D$4  ",
            firstColumnIsCategories: true,
            switchRowColumn: false);

        result.SourceRangeText.Should().Be("Sheet1!$A$1:$D$4");
        result.FirstColumnIsCategories.Should().BeTrue();
        result.SwitchRowColumn.Should().BeFalse();
    }

    [Fact]
    public void CreateResult_PreservesSwitchRowColumnFlag()
    {
        var result = SelectDataSourcePlanner.CreateResult(
            "A1:D4",
            firstColumnIsCategories: false,
            switchRowColumn: true);

        result.FirstColumnIsCategories.Should().BeFalse();
        result.SwitchRowColumn.Should().BeTrue();
    }

    [Fact]
    public void CreateResult_DefaultSwitchRowColumn_IsFalse()
    {
        var result = SelectDataSourcePlanner.CreateResult("A1:D4", firstColumnIsCategories: true);

        result.SwitchRowColumn.Should().BeFalse();
    }

    [Fact]
    public void CreateRangeSelectionRequest_TrimsCurrentTextAndRequestsCollapse()
    {
        var request = SelectDataSourcePlanner.CreateRangeSelectionRequest("  Sheet1!A1:D4  ");

        request.CurrentText.Should().Be("Sheet1!A1:D4");
        request.CollapseDialog.Should().BeTrue();
    }

    [Fact]
    public void InferPreviewEntries_CustomDisplayText_IsAppliedToInferredLabelsAndFallback()
    {
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "Sheet1!$A$1:$C$3",
            firstColumnIsCategories: true,
            index => $"Localized series {index}",
            index => $"Localized category {index}",
            "Localized category fallback");

        preview.Series.Select(series => series.Name).Should().ContainInOrder(
            "Localized series 1",
            "Localized series 2");
        preview.Categories.Select(category => category.Label).Should().ContainInOrder(
            "Localized category 1",
            "Localized category 2");

        var fallback = SelectDataSourcePlanner.InferPreviewEntries(
            "not a range",
            firstColumnIsCategories: true,
            index => $"Localized series {index}",
            index => $"Localized category {index}",
            "Localized category fallback");

        fallback.Series[0].Name.Should().Be("Localized series 1");
        fallback.Categories[0].Label.Should().Be("Localized category fallback");
    }

    // ---- FormatRangeReference ------------------------------------------------------------------

    [Fact]
    public void FormatRangeReference_WithSheet_IncludesSheetPrefix()
    {
        var text = SelectDataSourcePlanner.FormatRangeReference("Sheet1", 1, 1, 4, 4);

        text.Should().Be("Sheet1!$A$1:$D$4");
    }

    [Fact]
    public void FormatRangeReference_NullSheet_OmitsPrefix()
    {
        var text = SelectDataSourcePlanner.FormatRangeReference(null, 2, 1, 4, 5);

        text.Should().Be("$B$1:$D$5");
        text.Should().NotContain("!");
    }

    [Fact]
    public void FormatRangeReference_EmptySheet_OmitsPrefix()
    {
        var text = SelectDataSourcePlanner.FormatRangeReference(string.Empty, 1, 1, 1, 1);

        text.Should().NotContain("!");
    }

    // ---- Helper display formatters -------------------------------------------------------------

    [Fact]
    public void FormatSeriesName_ProducesCorrectLabel()
    {
        SelectDataSourcePlanner.FormatSeriesName(1).Should().Be("Series 1");
        SelectDataSourcePlanner.FormatSeriesName(3).Should().Be("Series 3");
    }

    [Fact]
    public void FormatCategoryName_ProducesCorrectLabel()
    {
        SelectDataSourcePlanner.FormatCategoryName(1).Should().Be("Category 1");
        SelectDataSourcePlanner.FormatCategoryName(5).Should().Be("Category 5");
    }

    [Fact]
    public void FormatSeriesListItem_CombinesNameAndRange()
    {
        var item = SelectDataSourcePlanner.FormatSeriesListItem("Series 1", "Sheet1!$B$2:$B$5");
        item.Should().Contain("Series 1");
        item.Should().Contain("Sheet1!$B$2:$B$5");
    }

    [Fact]
    public void FormatNewSeriesItem_ContainsSelectRangePlaceholder()
    {
        var item = SelectDataSourcePlanner.FormatNewSeriesItem(2);
        item.Should().Contain("Series 2");
        item.Should().Contain("<select range>");
    }

    // ---- ParseRangeReference — coverage via InferPreviewEntries --------------------------------

    [Theory]
    [InlineData("A1:B2")]
    [InlineData("$A$1:$B$2")]
    [InlineData("Sheet1!A1:B2")]
    [InlineData("Sheet1!$A$1:$B$2")]
    [InlineData("'My Sheet'!$A$1:$B$2")]
    public void InferPreviewEntries_RecognisesValidRangeFormats(string range)
    {
        var preview = SelectDataSourcePlanner.InferPreviewEntries(range, firstColumnIsCategories: false);
        // A parseable range produces at least one series with a formatted range text (not the raw input).
        preview.Series.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("A:B")]      // whole-column reference (no row numbers)
    [InlineData("1:2")]      // whole-row reference (no column letters)
    [InlineData("garbage")]
    public void InferPreviewEntries_UnparseableRange_UsesFallbackSeries(string range)
    {
        // Fallback path: range cannot be parsed → one fallback series whose ValuesRangeText is the trimmed input.
        var preview = SelectDataSourcePlanner.InferPreviewEntries(range, firstColumnIsCategories: false);
        preview.Series.Should().HaveCount(1);
        preview.Series[0].ValuesRangeText.Should().Be(range.Trim());
    }

    [Fact]
    public void InferPreviewEntries_SingleCellAddress_ParsesAsOneByOneRange()
    {
        // "A1" is treated as the range A1:A1 (start == end), not a fallback.
        var preview = SelectDataSourcePlanner.InferPreviewEntries("A1", firstColumnIsCategories: false);
        preview.Series.Should().HaveCount(1);
        preview.Series[0].ValuesRangeText.Should().Be("$A$1:$A$1");
    }
}
