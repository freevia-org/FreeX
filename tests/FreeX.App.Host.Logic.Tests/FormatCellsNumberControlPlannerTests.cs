using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FormatCellsNumberControlPlannerTests
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
    public void Plan_MatchesExcelNumberCategoryControlAvailability(
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
    [InlineData("#,##0.0000", 4)]
    [InlineData("#,##0;[Red](#,##0)", 0)]
    [InlineData(null, 2)]
    public void DecimalPlacesForFormat_MatchesExcelDecimalControls(string? format, int expected)
    {
        FormatCellsNumberFormatPlanner.DecimalPlacesForFormat(format)
            .Should()
            .Be(expected);
    }
}
