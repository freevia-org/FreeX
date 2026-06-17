using FluentAssertions;
using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Verifies the non-UI glue behind the Avalonia Insert Function / Function Arguments dialogs: the
/// built-in catalog and its category bucketing, the category-plus-search filter, the Most Recently
/// Used promotion logic, and the composed inserted-formula text. No Avalonia UI is constructed.
/// </summary>
public sealed class InsertFunctionCatalogTests
{
    [Fact]
    public void BuildCatalog_CoversCommonFunctions_WithCategoryAndDescription()
    {
        var catalog = InsertFunctionCatalog.BuildCatalog();

        catalog.Should().NotBeEmpty();
        catalog.Select(entry => entry.Name).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        catalog.Should().OnlyContain(entry => entry.Name == entry.Name.ToUpperInvariant());

        var sum = catalog.Single(entry => entry.Name == "SUM");
        sum.Category.Should().Be("Math & Trig");
        sum.Description.Should().Be("Adds numbers.");

        catalog.Single(entry => entry.Name == "VLOOKUP").Category.Should().Be("Lookup & Reference");
        catalog.Single(entry => entry.Name == "IF").Category.Should().Be("Logical");
    }

    [Fact]
    public void BuildCategoryChoices_LeadsWithMostRecentlyUsedThenAll()
    {
        var catalog = InsertFunctionCatalog.BuildCatalog();

        var choices = InsertFunctionCatalog.BuildCategoryChoices(catalog);

        choices[0].Should().Be(InsertFunctionCatalog.MostRecentlyUsedCategory);
        choices[1].Should().Be(InsertFunctionCatalog.AllCategory);
        choices.Should().Contain("Logical");
        choices.Skip(2).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void FilterCatalog_BySearchText_MatchesNameOrDescription()
    {
        var catalog = InsertFunctionCatalog.BuildCatalog();

        var byName = InsertFunctionCatalog.FilterCatalog(catalog, InsertFunctionCatalog.AllCategory, "vlook");
        byName.Should().Contain(entry => entry.Name == "VLOOKUP");
        byName.Should().OnlyContain(entry =>
            entry.Name.Contains("VLOOK", StringComparison.OrdinalIgnoreCase) ||
            entry.Description.Contains("vlook", StringComparison.OrdinalIgnoreCase));

        var byDescription = InsertFunctionCatalog.FilterCatalog(catalog, InsertFunctionCatalog.AllCategory, "average");
        byDescription.Should().Contain(entry => entry.Name == "AVERAGE");
    }

    [Fact]
    public void FilterCatalog_ByCategory_KeepsOnlyThatCategory()
    {
        var catalog = InsertFunctionCatalog.BuildCatalog();

        var logical = InsertFunctionCatalog.FilterCatalog(catalog, "Logical", searchText: null);

        logical.Should().NotBeEmpty();
        logical.Should().OnlyContain(entry => entry.Category == "Logical");
        logical.Should().Contain(entry => entry.Name == "IF");
    }

    [Fact]
    public void FilterCatalog_MostRecentlyUsed_NoSearch_KeepsRecentOrder()
    {
        var catalog = InsertFunctionCatalog.BuildCatalog();
        IReadOnlyList<string> recent = ["IF", "SUM", "VLOOKUP"];

        var filtered = InsertFunctionCatalog.FilterCatalog(
            catalog,
            InsertFunctionCatalog.MostRecentlyUsedCategory,
            searchText: null,
            recent);

        filtered.Select(entry => entry.Name).Should().Equal("IF", "SUM", "VLOOKUP");
    }

    [Fact]
    public void FilterCatalog_MostRecentlyUsed_WithSearch_SpansWholeCatalog()
    {
        var catalog = InsertFunctionCatalog.BuildCatalog();
        IReadOnlyList<string> recent = ["IF", "SUM"];

        // CONCAT is not in the recent list; searching from the MRU category should still surface it.
        var filtered = InsertFunctionCatalog.FilterCatalog(
            catalog,
            InsertFunctionCatalog.MostRecentlyUsedCategory,
            "concat",
            recent);

        filtered.Should().Contain(entry => entry.Name == "CONCAT");
    }

    [Fact]
    public void UpdateMostRecentlyUsed_PromotesToFront_AndDeduplicates()
    {
        IReadOnlyList<string> recent = ["SUM", "AVERAGE", "COUNT"];

        var updated = InsertFunctionCatalog.UpdateMostRecentlyUsed(recent, "average");

        updated.Should().Equal("AVERAGE", "SUM", "COUNT");
    }

    [Fact]
    public void UpdateMostRecentlyUsed_AddsNewFunction_NormalizedUppercase()
    {
        IReadOnlyList<string> recent = ["SUM"];

        var updated = InsertFunctionCatalog.UpdateMostRecentlyUsed(recent, "xlookup");

        updated[0].Should().Be("XLOOKUP");
        updated.Should().HaveCount(2);
    }

    [Fact]
    public void UpdateMostRecentlyUsed_CapsListLength()
    {
        IReadOnlyList<string> recent =
            ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J"];

        var updated = InsertFunctionCatalog.UpdateMostRecentlyUsed(recent, "NEW");

        updated.Should().HaveCount(10);
        updated[0].Should().Be("NEW");
        updated.Should().NotContain("J");
    }

    [Fact]
    public void CreateFormula_SeedsEmptyFunctionCall()
    {
        InsertFunctionCatalog.CreateFormula("sum").Should().Be("=SUM()");
    }

    [Fact]
    public void BuildPreview_ComposesInsertedFormulaFromArguments()
    {
        // The composed text inserted into the active cell is the live preview from the shared
        // argument catalog: trailing blanks trimmed, leading "=" present.
        FunctionArgumentCatalog.BuildPreview("SUM", ["A1", "A2"]).Should().Be("=SUM(A1, A2)");
        FunctionArgumentCatalog.BuildPreview("SUM", ["A1", ""]).Should().Be("=SUM(A1)");
        FunctionArgumentCatalog.BuildPreview("IF", ["A1>0", "\"yes\"", "\"no\""])
            .Should().Be("=IF(A1>0, \"yes\", \"no\")");
    }
}
