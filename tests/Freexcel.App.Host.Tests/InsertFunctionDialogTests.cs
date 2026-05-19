using FluentAssertions;
using Freexcel.Core.Formula;

namespace Freexcel.App.Host.Tests;

public sealed class InsertFunctionDialogTests
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
    public void CreateFormula_UsesSelectedFunctionName()
    {
        InsertFunctionDialog.CreateFormula(" xlookup ").Should().Be("XLOOKUP()");
    }
}
