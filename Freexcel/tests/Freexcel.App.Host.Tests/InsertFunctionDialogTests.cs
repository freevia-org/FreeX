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

        source.Should().Contain("Content = \"Or select a _category:\"");
        source.Should().Contain("Target = _categoryBox");
        source.Should().Contain("Content = \"Search for a _function:\"");
        source.Should().Contain("Target = _searchBox");
        source.Should().Contain("Content = \"Select a _function:\"");
        source.Should().Contain("Target = _listBox");
        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("Content = \"_Cancel\"");
    }

    [Fact]
    public void Dialog_ExposesExcelLikeSearchResultsAndHelpAffordances()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "InsertFunctionDialog.cs"));

        source.Should().Contain("Search for a _function:");
        source.Should().Contain("Or select a _category:");
        source.Should().Contain("_Go");
        source.Should().Contain("Select a _function:");
        source.Should().Contain("Formula syntax and help");
        source.Should().Contain("Help on this function");
    }
}
