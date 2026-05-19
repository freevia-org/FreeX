using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class InsertFunctionCatalogPlannerTests
{
    [Theory]
    [InlineData("IF", "Logical")]
    [InlineData("XLOOKUP", "Lookup & Reference")]
    [InlineData("TEXT", "Text")]
    [InlineData("TODAY", "Date & Time")]
    [InlineData("AVERAGE", "Statistical")]
    [InlineData("FILTER", "Dynamic Array")]
    [InlineData("PMT", "Financial")]
    [InlineData("ISBLANK", "Information")]
    [InlineData("SUM", "Math & Trig")]
    public void GetCategory_MapsKnownFunctionFamilies(string functionName, string expectedCategory)
    {
        InsertFunctionCatalogPlanner.GetCategory(functionName).Should().Be(expectedCategory);
    }

    [Fact]
    public void GetDescription_UsesKnownDescriptionOrFallback()
    {
        InsertFunctionCatalogPlanner.GetDescription("SUM").Should().Be("Adds numbers.");
        InsertFunctionCatalogPlanner.GetDescription("CUSTOM").Should().Be("CUSTOM function.");
    }
}
