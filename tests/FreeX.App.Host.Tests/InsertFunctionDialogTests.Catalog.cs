using FluentAssertions;
using FreeX.Core.Formula;

namespace FreeX.App.Host.Tests;

public sealed partial class InsertFunctionDialogTests
{
    [Fact]
    public void BuildCatalog_UsesImplementedFormulaRegistry()
    {
        var catalog = InsertFunctionDialog.BuildCatalog();

        catalog.Select(entry => entry.Name)
            .Should()
            .Contain(BuiltInFunctions.Names);
    }

    [Fact]
    public void FilterCatalog_FiltersByCategoryAndSearchText()
    {
        var catalog = InsertFunctionDialog.BuildCatalog();

        var results = InsertFunctionDialog.FilterCatalog(catalog, "Lookup & Reference", "match");

        results.Select(entry => entry.Name).Should().Contain(["MATCH", "XMATCH"]);
        results.Should().OnlyContain(entry => entry.Category == "Lookup & Reference");
    }

    [Fact]
    public void CategoryChoices_StartWithExcelMostRecentlyUsedThenAll()
    {
        var categories = InsertFunctionDialog.BuildCategoryChoices(InsertFunctionDialog.BuildCatalog());

        categories.Take(2).Should().Equal("Most Recently Used", "All");
    }

    [Fact]
    public void FilterCatalog_DefaultMostRecentlyUsedShowsRecommendedFunctionsButSearchSpansCatalog()
    {
        var catalog = InsertFunctionDialog.BuildCatalog();

        var recent = InsertFunctionDialog.FilterCatalog(catalog, "Most Recently Used", "");

        recent.Select(entry => entry.Name).Should().StartWith(["SUM", "AVERAGE", "COUNT"]);
        recent.Select(entry => entry.Name).Should().Contain(["IF", "XLOOKUP"]);

        var searched = InsertFunctionDialog.FilterCatalog(catalog, "Most Recently Used", "match");

        searched.Select(entry => entry.Name).Should().Contain(["MATCH", "XMATCH"]);
    }

    [Fact]
    public void CreateFormula_UsesSelectedFunctionName()
    {
        InsertFunctionDialog.CreateFormula(" xlookup ").Should().Be("XLOOKUP()");
    }
}
