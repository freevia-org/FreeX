using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed class SpellCheckWorkflowPlannerTests
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
    public void FilterIssues_RemovesIgnoredWordsAndSpecificIgnoredIssues()
    {
        var sheet = SheetId.New();
        var ignoredAddress = new CellAddress(sheet, 2, 1);
        var ignoredIssue = Issue(ignoredAddress, "teh", "teh value");
        var kept = Issue(new CellAddress(sheet, 3, 1), "teh", "teh item");

        var filtered = SpellCheckWorkflowPlanner.FilterIssues(
            [
                Issue(new CellAddress(sheet, 1, 1), "adn", "adn value"),
                ignoredIssue,
                kept
            ],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "adn" },
            new HashSet<SpellingIssueKey> { SpellCheckWorkflowPlanner.CreateIssueKey(ignoredIssue) });

        filtered.Should().ContainSingle().Which.Should().Be(kept);
    }

    [Fact]
    public void FilterIssues_RemovesIgnoredWordsCaseInsensitively()
    {
        var sheet = SheetId.New();
        var kept = Issue(new CellAddress(sheet, 2, 1), "adn", "adn value");

        var filtered = SpellCheckWorkflowPlanner.FilterIssues(
            [
                Issue(new CellAddress(sheet, 1, 1), "TEH", "TEH value"),
                kept
            ],
            new HashSet<string> { "teh" },
            new HashSet<SpellingIssueKey>());

        filtered.Should().ContainSingle().Which.Should().Be(kept);
    }

    [Fact]
    public void FilterIssues_IgnoreOnceKeepsOtherSourcesAtSameAddress()
    {
        var sheet = SheetId.New();
        var address = new CellAddress(sheet, 1, 1);
        var cellIssue = Issue(address, "teh", "teh cell", SpellingIssueSource.CellText, startIndex: 0);
        var noteIssue = Issue(address, "teh", "teh note", SpellingIssueSource.Note, startIndex: 0);

        var filtered = SpellCheckWorkflowPlanner.FilterIssues(
            [cellIssue, noteIssue],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<SpellingIssueKey> { SpellCheckWorkflowPlanner.CreateIssueKey(cellIssue) });

        filtered.Should().ContainSingle().Which.Should().Be(noteIssue);
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

    [Fact]
    public void FilterIssues_ScansLargeIssueListsInOnePass()
    {
        var sheet = SheetId.New();
        var issues = Enumerable.Range(0, 5_000)
            .Select(index => Issue(
                new CellAddress(sheet, (uint)(index + 1), 1),
                index % 5 == 0 ? "adn" : "teh",
                $"{index} issue"))
            .ToArray();
        var ignoredIssues = issues
            .Where((_, index) => index % 7 == 0)
            .Select(SpellCheckWorkflowPlanner.CreateIssueKey)
            .ToHashSet();

        var filtered = SpellCheckWorkflowPlanner.FilterIssues(
            issues,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "adn" },
            ignoredIssues);

        filtered.Should().HaveCount(3_428);
        foreach (var issue in filtered)
        {
            issue.Word.Equals("adn", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
            ignoredIssues.Contains(SpellCheckWorkflowPlanner.CreateIssueKey(issue)).Should().BeFalse();
        }
    }

    [Fact]
    public void BuildReplacementEdit_AppliesCorrectionAsTextCellEdit()
    {
        var address = new CellAddress(SheetId.New(), 4, 2);

        var edit = SpellCheckWorkflowPlanner.BuildReplacementEdit(
            Issue(address, "Teh", "Teh value"),
            "the");

        edit.Address.Should().Be(address);
        edit.NewCell.Value.Should().Be(new TextValue("The value"));
    }

    [Fact]
    public void BuildReplaceAllEdits_GroupsDuplicateIssuesByCell()
    {
        var sheet = SheetId.New();
        var firstAddress = new CellAddress(sheet, 1, 1);
        var secondAddress = new CellAddress(sheet, 2, 1);

        var edits = SpellCheckWorkflowPlanner.BuildReplaceAllEdits(
            [
                Issue(firstAddress, "teh", "teh and teh"),
                Issue(firstAddress, "teh", "teh and teh"),
                Issue(secondAddress, "TEH", "TEH value"),
                Issue(new CellAddress(sheet, 3, 1), "adn", "adn value")
            ],
            "teh",
            "the");

        edits.Should().HaveCount(2);
        edits.Select(edit => edit.Address).Should().Equal(firstAddress, secondAddress);
        edits[0].NewCell.Value.Should().Be(new TextValue("the and the"));
        edits[1].NewCell.Value.Should().Be(new TextValue("THE value"));
    }

    [Fact]
    public void BuildReplaceAllEdits_PreservesPerOccurrenceCasingWithinCells()
    {
        var sheet = SheetId.New();
        var address = new CellAddress(sheet, 1, 1);
        var issues = SpellCheckService.FindIssuesInCell(address, "teh TEH Teh");

        var edits = SpellCheckWorkflowPlanner.BuildReplaceAllEdits(issues, "teh", "the");

        edits.Should().ContainSingle();
        edits[0].Address.Should().Be(address);
        edits[0].NewCell.Value.Should().Be(new TextValue("the THE The"));
    }

    [Fact]
    public void BuildReplaceAllEdits_CollapsesRepeatedWordRunsWithinCells()
    {
        var sheet = SheetId.New();
        var address = new CellAddress(sheet, 1, 1);
        var issues = SpellCheckService.FindIssuesInCell(address, "the the the and The The file");

        var edits = SpellCheckWorkflowPlanner.BuildReplaceAllEdits(issues, "the the", "the");

        edits.Should().ContainSingle();
        edits[0].Address.Should().Be(address);
        edits[0].NewCell.Value.Should().Be(new TextValue("the and The file"));
    }

    [Fact]
    public void BuildReplaceAllEdits_ScansLargeIssueListsWithoutGroupingAllocation()
    {
        var sheet = SheetId.New();
        var issues = Enumerable.Range(0, 5_000)
            .Select(index =>
            {
                var address = new CellAddress(sheet, (uint)(index / 2 + 1), 1);
                return Issue(address, index % 3 == 0 ? "TEH" : "adn", $"{index} TEH value");
            })
            .ToArray();

        var edits = SpellCheckWorkflowPlanner.BuildReplaceAllEdits(issues, "teh", "the");

        edits.Should().HaveCount(1_667);
        edits.Select(edit => edit.Address).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildReplaceAllCommand_UpdatesCellsNotesAndThreadedCommentText()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("teh cell and teh total"));
        sheet.Comments[a1] = "teh note and teh note";
        sheet.ThreadedComments[b1] = new ThreadedComment("teh root")
        {
            Replies =
            [
                new CommentReply("teh reply and teh reply"),
                new CommentReply("adn other reply")
            ]
        };
        var issues = SpellCheckService.FindIssues(workbook, sheet.Id);
        var context = new SimpleCtx(workbook);

        var command = SpellCheckWorkflowPlanner.BuildReplaceAllCommand(issues, "teh", "the");
        var outcome = command!.Apply(context);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("the cell and the total"));
        sheet.Comments[a1].Should().Be("the note and the note");
        sheet.ThreadedComments[b1].Text.Should().Be("the root");
        sheet.ThreadedComments[b1].Replies[0].Text.Should().Be("the reply and the reply");
        sheet.ThreadedComments[b1].Replies[1].Text.Should().Be("adn other reply");

        command.Revert(context);

        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("teh cell and teh total"));
        sheet.Comments[a1].Should().Be("teh note and teh note");
        sheet.ThreadedComments[b1].Text.Should().Be("teh root");
        sheet.ThreadedComments[b1].Replies[0].Text.Should().Be("teh reply and teh reply");
        sheet.ThreadedComments[b1].Replies[1].Text.Should().Be("adn other reply");
    }

    [Fact]
    public void BuildReplacementCommand_UpdatesThreadedCommentReplyText()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.ThreadedComments[address] = new ThreadedComment("clean root")
        {
            Replies = [new CommentReply("Fix teh reply")]
        };
        var issue = SpellCheckService.FindIssues(workbook, sheet.Id).Single();
        var context = new SimpleCtx(workbook);

        var command = SpellCheckWorkflowPlanner.BuildReplacementCommand(issue, "the");
        var outcome = command.Apply(context);

        outcome.Success.Should().BeTrue();
        sheet.ThreadedComments[address].Replies[0].Text.Should().Be("Fix the reply");

        command.Revert(context);

        sheet.ThreadedComments[address].Replies[0].Text.Should().Be("Fix teh reply");
    }

    [Fact]
    public void BuildReplaceAllEdits_UsesSinglePassAddressDeduplication()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "SpellCheckWorkflowPlanner.cs"));

        source.Should().Contain("var filtered = new List<SpellingIssue>();");
        source.Should().Contain("var editedAddresses = new HashSet<CellAddress>();");
        source.Should().Contain("var editedTargets = new HashSet<SpellingIssueTargetKey>();");
        source.Should().NotContain(".Where(");
        source.Should().NotContain(".GroupBy(");
    }

    private static SpellingIssue Issue(
        CellAddress address,
        string word,
        string cellText,
        SpellingIssueSource source = SpellingIssueSource.CellText,
        int replyIndex = -1,
        int startIndex = -1) =>
        new(
            address,
            word,
            word.Equals("adn", StringComparison.OrdinalIgnoreCase) ? "and" : "the",
            cellText,
            startIndex,
            startIndex >= 0 ? word.Length : 0,
            source,
            replyIndex);

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
