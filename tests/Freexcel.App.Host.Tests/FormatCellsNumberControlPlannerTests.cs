using FluentAssertions;

namespace Freexcel.App.Host.Tests;

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
}
