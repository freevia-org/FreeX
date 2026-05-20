using System.IO;
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

    [Fact]
    public void DialogCommands_ExposeKeyboardAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "InsertFunctionDialog.cs"));

        source.Should().Contain("Content = \"_Category:\"");
        source.Should().Contain("Target = _categoryBox");
        source.Should().Contain("Content = \"_Search:\"");
        source.Should().Contain("Target = _searchBox");
        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("Content = \"_Cancel\"");
    }
}
