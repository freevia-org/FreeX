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

    [Fact]
    public void FunctionArgumentsDialog_ExposesExcelLikeArgumentMetadataForCommonFunctions()
    {
        FunctionArgumentsDialog.GetArgumentSpecs("IF")
            .Select(argument => argument.Name)
            .Should()
            .Equal("Logical_test", "Value_if_true", "Value_if_false");

        FunctionArgumentsDialog.GetArgumentSpecs("XLOOKUP")
            .Select(argument => argument.Name)
            .Should()
            .StartWith(["Lookup_value", "Lookup_array", "Return_array"]);

        FunctionArgumentsDialog.GetArgumentSpecs("COUNTIF")
            .Select(argument => argument.Name)
            .Should()
            .Equal("Range", "Criteria");

        FunctionArgumentsDialog.GetArgumentSpecs("INDEX")
            .Select(argument => argument.Name)
            .Should()
            .Equal("Array", "Row_num", "Column_num");

        FunctionArgumentsDialog.GetArgumentSpecs("TEXT")
            .Select(argument => argument.Name)
            .Should()
            .Equal("Value", "Format_text");
    }

    [Fact]
    public void FunctionArgumentsDialog_CreateFormula_UsesProvidedArgumentsAndTrimsTrailingBlanks()
    {
        FunctionArgumentsDialog.CreateFormula(" if ", ["A1>0", "\"Yes\"", ""])
            .Should()
            .Be("IF(A1>0, \"Yes\")");
    }

    [Fact]
    public void FunctionArgumentsDialogOpenedFromKeyboard_FocusesFirstArgumentBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FunctionArgumentsDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_argumentBoxes.FirstOrDefault()");
        source.Should().Contain("firstArgument.Focus();");
        source.Should().Contain("firstArgument.SelectAll();");
        source.Should().Contain("Keyboard.Focus(firstArgument);");
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
        source.Should().Contain("Content = \"_Help on this function\"");
        source.Should().Contain("ShowFunctionHelp");
        source.Should().NotContain("SystemSounds.Asterisk.Play");
        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("Content = \"_Cancel\"");
    }

    [Fact]
    public void Dialog_ExposesExcelLikeSearchResultsAndHelpAffordances()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "InsertFunctionDialog.cs"));

        source.Should().Contain("Search for a _function:");
        source.Should().Contain("Or select a _category:");
        source.Should().Contain("MostRecentlyUsedCategory");
        source.Should().Contain("_categoryBox.SelectedItem = MostRecentlyUsedCategory");
        source.Should().Contain("_Go");
        source.Should().Contain("Select a _function:");
        source.Should().Contain("Formula syntax and help");
        source.Should().Contain("_Help on this function");
        source.Should().Contain("FunctionArgumentsDialog");
        source.Should().Contain("argumentsDialog.ResultFormula");
    }
}
