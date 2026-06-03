using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class SpellCheckWorkflowPlannerTests
{
    [Fact]
    public void CreateCustomDictionary_LoadsPersistedWordsCaseInsensitively()
    {
        var options = new FreeXOptions
        {
            SpellCheckCustomDictionaryWords = ["  TeH  ", "", "teh", "the the"]
        };

        var customDictionary = SpellCheckWorkflowPlanner.CreateCustomDictionary(options);

        customDictionary.Should().Equal("TeH", "the the");
        customDictionary.Contains("teh").Should().BeTrue();
    }

    [Fact]
    public void AddCustomDictionaryWord_NormalizesPersistsAndUpdatesRuntimeDictionary()
    {
        var options = new FreeXOptions
        {
            SpellCheckCustomDictionaryWords = ["recieve"]
        };
        var customDictionary = SpellCheckWorkflowPlanner.CreateCustomDictionary(options);

        var added = SpellCheckWorkflowPlanner.AddCustomDictionaryWord(options, customDictionary, "  TeH  ");

        added.Should().BeTrue();
        customDictionary.Contains("teh").Should().BeTrue();
        options.SpellCheckCustomDictionaryWords.Should().Equal("recieve", "TeH");
    }

    [Fact]
    public void AddCustomDictionaryWord_IgnoresBlankAndDuplicateWords()
    {
        var options = new FreeXOptions
        {
            SpellCheckCustomDictionaryWords = ["TeH"]
        };
        var customDictionary = SpellCheckWorkflowPlanner.CreateCustomDictionary(options);

        SpellCheckWorkflowPlanner.AddCustomDictionaryWord(options, customDictionary, " teh ")
            .Should()
            .BeFalse();
        SpellCheckWorkflowPlanner.AddCustomDictionaryWord(options, customDictionary, " ")
            .Should()
            .BeFalse();

        options.SpellCheckCustomDictionaryWords.Should().Equal("TeH");
        customDictionary.Should().ContainSingle().Which.Should().Be("TeH");
    }

    [Fact]
    public void PersistedCustomDictionarySuppressesKnownCorrectionsInScanner()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("TEH adn value"));
        var options = new FreeXOptions
        {
            SpellCheckCustomDictionaryWords = ["teh"]
        };

        var issues = SpellCheckService.FindIssues(
            workbook,
            sheet.Id,
            SpellCheckWorkflowPlanner.CreateCustomDictionary(options));

        issues.Select(issue => issue.Word).Should().Equal("adn");
    }

    [Fact]
    public void SpellCheckWorkflow_RoutesAddToDictionaryThroughPersistedCustomDictionaryScan()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("var customDictionary = SpellCheckWorkflowPlanner.CreateCustomDictionary(_options);");
        source.Should().Contain("SpellCheckService.FindIssues(_workbook, _currentSheetId, customDictionary)");
        source.Should().Contain("dialog.Result.Action == SpellCheckDialogAction.Add");
        source.Should().Contain("SpellCheckWorkflowPlanner.AddCustomDictionaryWord(_options, customDictionary, issue.Word)");
        source.Should().Contain("_options.Save();");
    }
}
