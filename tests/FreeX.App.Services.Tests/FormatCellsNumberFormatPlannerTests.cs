using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class FormatCellsNumberFormatPlannerTests
{
    [Theory]
    [InlineData("Number", true, false, true, true)]
    [InlineData("Currency", true, true, true, true)]
    [InlineData("Accounting", true, true, false, true)]
    [InlineData("Percentage", true, false, false, true)]
    [InlineData("Scientific", true, false, false, true)]
    [InlineData("Date", false, false, false, false)]
    [InlineData("Custom", false, false, false, false)]
    [InlineData(null, false, false, false, false)]
    public void ControlPlanner_MatchesExcelNumberCategoryControlAvailability(
        string? category,
        bool usesDecimals,
        bool usesSymbol,
        bool usesNegativeOptions,
        bool generatesFormat)
    {
        FormatCellsNumberControlPlanner.Plan(category)
            .Should()
            .Be(new FormatCellsNumberControlAvailability(
                usesDecimals,
                usesSymbol,
                usesNegativeOptions,
                generatesFormat));
    }

    [Theory]
    [InlineData("Zip Code", "00000")]
    [InlineData("Accounting ($#,##0.00)", "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)")]
    [InlineData("Long time ([$-F400])", "[$-F400]")]
    public void ResolveNumberFormat_MapsExcelLikeLabelsToCodes(string label, string expected)
    {
        FormatCellsNumberFormatPlanner.ResolveNumberFormat(label, 0)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData("Number", "#,##0.00", "0", "None", 0, "#,##0")]
    [InlineData("Currency", "$#,##0.00", "3", "EUR", 2, "EUR#,##0.000;(EUR#,##0.000)")]
    [InlineData("Accounting", "$#,##0.00", "2", "GBP", 0, "_(GBP* #,##0.00_);_(GBP* (#,##0.00);_(GBP* \"-\"??_);_(@_)")]
    [InlineData("Percentage", "0.00%", "1", "None", 0, "0.0%")]
    public void ResolveNumberFormat_ComposesGeneratedCategories(
        string category,
        string selectedFormat,
        string decimalPlaces,
        string symbol,
        int negativeIndex,
        string expected)
    {
        FormatCellsNumberFormatPlanner.ResolveNumberFormat(selectedFormat, 0, category, decimalPlaces, symbol, negativeIndex)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData("Number", "0", "None", 0, "#,##0")]
    [InlineData("Number", "2", "None", 3, "#,##0.00;[Red](#,##0.00)")]
    [InlineData("Currency", "2", "$", 1, "$#,##0.00;[Red]-$#,##0.00")]
    [InlineData("Scientific", "4", "None", 0, "0.0000E+00")]
    [InlineData("Percentage", "0", "None", 0, "0%")]
    public void ResolveSelectedNumberFormat_BuildsCodeFromControlsForGeneratedCategories(
        string category,
        string decimalPlaces,
        string symbol,
        int negativeIndex,
        string expected)
    {
        FormatCellsNumberFormatPlanner.ResolveSelectedNumberFormat(category, "", 0, decimalPlaces, symbol, negativeIndex)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void ResolveSelectedNumberFormat_FallsBackToCatalogCodeForNonGeneratedCategory()
    {
        FormatCellsNumberFormatPlanner.ResolveSelectedNumberFormat("Date", "d-mmm-yy", 0, "2", "None", 0)
            .Should()
            .Be("d-mmm-yy");
    }

    [Theory]
    [InlineData("#,##0.0000", 4)]
    [InlineData("#,##0;[Red](#,##0)", 0)]
    [InlineData(null, 2)]
    public void DecimalPlacesForFormat_MatchesExcelDecimalControls(string? format, int expected)
    {
        FormatCellsNumberFormatPlanner.DecimalPlacesForFormat(format)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void Categories_ExposesTheExcelNumberCategoryFamilies()
    {
        FormatCellsNumberFormatPlanner.Categories
            .Should()
            .ContainInOrder("General", "Number", "Currency", "Accounting", "Date", "Time", "Percentage")
            .And.Contain(new[] { "Fraction", "Scientific", "Text", "Special", "Custom" });
    }

    [Fact]
    public void LabelsForCategory_ReturnsDistinctTypeLabelsForTheCategory()
    {
        var labels = FormatCellsNumberFormatPlanner.LabelsForCategory("Currency");

        labels.Should().OnlyHaveUniqueItems();
        labels.Should().Contain("Currency ($#,##0.00)");
    }

    [Theory]
    [InlineData("0.00", "1234.56")]
    [InlineData("0%", "123456%")]
    [InlineData("m/d/yyyy", "5/21/2026")]
    public void PreviewForFormat_RendersTheSampleValueThroughTheFormatter(string format, string expected)
    {
        FormatCellsNumberFormatPlanner.PreviewForFormat(format)
            .Should()
            .Be(expected);
    }
}
