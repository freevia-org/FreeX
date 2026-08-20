using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class SpellCheckWorkflowPlannerTests
{
    [Fact]
    public void CreateCustomDictionary_LoadsPersistedWordsCaseInsensitively()
    {
        var options = new AppOptions
        {
            SpellCheckCustomDictionaryWords = ["  TeH  ", "", "teh", "the the"]
        };

        var customDictionary = SpellCheckWorkflowPlanner.CreateCustomDictionary(options.SpellCheckCustomDictionaryWords);

        customDictionary.Should().Equal("TeH", "the the");
        customDictionary.Contains("teh").Should().BeTrue();
    }

    [Fact]
    public void AddCustomDictionaryWord_NormalizesPersistsAndUpdatesRuntimeDictionary()
    {
        var options = new AppOptions
        {
            SpellCheckCustomDictionaryWords = ["recieve"]
        };
        var customDictionary = SpellCheckWorkflowPlanner.CreateCustomDictionary(options.SpellCheckCustomDictionaryWords);

        var added = SpellCheckWorkflowPlanner.AddCustomDictionaryWord(
            options.SpellCheckCustomDictionaryWords,
            customDictionary,
            "  TeH  ");

        added.Should().BeTrue();
        customDictionary.Contains("teh").Should().BeTrue();
        options.SpellCheckCustomDictionaryWords.Should().Equal("recieve", "TeH");
    }

    [Fact]
    public void AddCustomDictionaryWord_IgnoresBlankAndDuplicateWords()
    {
        var options = new AppOptions
        {
            SpellCheckCustomDictionaryWords = ["TeH"]
        };
        var customDictionary = SpellCheckWorkflowPlanner.CreateCustomDictionary(options.SpellCheckCustomDictionaryWords);

        SpellCheckWorkflowPlanner.AddCustomDictionaryWord(options.SpellCheckCustomDictionaryWords, customDictionary, " teh ")
            .Should()
            .BeFalse();
        SpellCheckWorkflowPlanner.AddCustomDictionaryWord(options.SpellCheckCustomDictionaryWords, customDictionary, " ")
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
        var options = new AppOptions
        {
            SpellCheckCustomDictionaryWords = ["teh"]
        };

        var issues = SpellCheckService.FindIssues(
            workbook,
            sheet.Id,
            SpellCheckWorkflowPlanner.CreateCustomDictionary(options.SpellCheckCustomDictionaryWords));

        issues.Select(issue => issue.Word).Should().Equal("adn");
    }

    [Fact]
    public void SpellCheckWorkflow_RoutesAddToDictionaryThroughPersistedCustomDictionaryScan()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");
        var controllerSource = DialogSourceTestSupport.ReadAppServicesSource("SpellCheckSessionController.cs");

        source.Should().Contain("new SpellCheckSessionController(new SpellCheckSessionAdapter(");
        source.Should().Contain("() => _options.SpellCheckCustomDictionaryWords");
        source.Should().Contain("() => MutateRuntimeOptions(options =>");
        source.Should().Contain("options.SpellCheckCustomDictionaryWords =");
        source.Should().Contain("new SpellCheckSessionController(new SpellCheckSessionAdapter(");
        // r153: the mutation used to overwrite the freshly-reloaded list with this window's
        // in-memory one, dropping a word another FreeX process had persisted in the meantime. It
        // now unions the two, so this pin follows the expression rather than the old wholesale
        // copy. The BEHAVIOUR is asserted properly by
        // R153_SpellCheckAddToDictionaryConcurrentPersistTests -- this line only keeps the wiring
        // contract that the window's own words still reach the mutation at all.
        source.Should().Contain(".Concat(_options.SpellCheckCustomDictionaryWords)");
        controllerSource.Should().Contain("case SpellCheckSessionAction.AddToDictionary:");
        controllerSource.Should().Contain("SpellCheckWorkflowPlanner.AddCustomDictionaryWord(");
        controllerSource.Should().Contain("_adapter.PersistCustomDictionary();");
    }
}
