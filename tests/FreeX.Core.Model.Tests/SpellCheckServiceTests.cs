using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class SpellCheckServiceTests
{
    [Fact]
    public void FindIssues_ReturnsKnownMisspellingsInSheetRowOrder()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a2, new TextValue("Please recieve the file."));
        sheet.SetCell(b1, new TextValue("Fix teh value."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);

        issues.Should().HaveCount(2);
        issues[0].Address.Should().Be(b1);
        issues[0].Word.Should().Be("teh");
        issues[0].Suggestion.Should().Be("the");
        issues[1].Address.Should().Be(a2);
        issues[1].Word.Should().Be("recieve");
        issues[1].Suggestion.Should().Be("receive");
    }

    [Fact]
    public void FindIssues_ReturnsKnownMisspellingsInWorkbookSheetThenRowOrder()
    {
        var wb = new Workbook("test");
        var first = wb.AddSheet("First");
        var second = wb.AddSheet("Second");

        var firstB2 = new CellAddress(first.Id, 2, 2);
        var firstA5 = new CellAddress(first.Id, 5, 1);
        var secondA1 = new CellAddress(second.Id, 1, 1);
        first.SetCell(firstA5, new TextValue("occured later"));
        first.SetCell(firstB2, new TextValue("teh earlier row"));
        second.SetCell(secondA1, new TextValue("adn next sheet"));

        var issues = SpellCheckService.FindIssues(wb);

        issues.Select(issue => issue.Address).Should().Equal(firstB2, firstA5, secondA1);
    }

    [Fact]
    public void FindIssues_ReturnsEachKnownIssueInTextCellAndSkipsFormulaCells()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        var formulaAddress = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(textAddress, new TextValue("teh value adn seperate note"));
        sheet.SetCell(formulaAddress, Cell.FromFormula("\"teh formula text\""));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);

        issues.Select(issue => issue.Word).Should().Equal("teh", "adn", "seperate");
        issues.Should().OnlyContain(issue => issue.Address == textAddress);
    }

    [Fact]
    public void FindIssues_PreservesTextOrderForMultipleIssuesInSameCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("recieve teh adn occured"));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);

        issues.Select(issue => issue.Word).Should().Equal("recieve", "teh", "adn", "occured");
    }

    [Fact]
    public void FindIssues_ReturnsKnownMisspellingsInNotesAndThreadedComments()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("adn cell"));
        sheet.Comments[a1] = "teh note";
        sheet.ThreadedComments[b1] = new ThreadedComment("recieve root")
        {
            Replies =
            [
                new CommentReply("adn reply"),
                new CommentReply("clean reply")
            ]
        };

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);

        issues.Select(issue => (issue.Address, issue.Word, issue.Source, issue.ReplyIndex)).Should().Equal(
            (a1, "adn", SpellingIssueSource.CellText, -1),
            (a1, "teh", SpellingIssueSource.Note, -1),
            (b1, "recieve", SpellingIssueSource.ThreadedComment, -1),
            (b1, "adn", SpellingIssueSource.ThreadedCommentReply, 0));
    }

    [Fact]
    public void FindIssuesInCell_IgnoresInternetEmailAndFileAddressSpans()
    {
        var address = new CellAddress(SheetId.New(), 1, 1);
        var text = "Visit https://teh.example.com, www.adn.test, email teh@example.com, or C:\\teh\\report.xlsx; recieve this note.";

        var issues = SpellCheckService.FindIssuesInCell(address, text);

        issues.Select(issue => issue.Word).Should().Equal("recieve");
    }

    [Fact]
    public void FindIssuesInCell_IgnoresExpandedAddressPathAndFilenameSpans()
    {
        var address = new CellAddress(SheetId.New(), 1, 1);
        var text = "Open mailto:teh@example.com, file:///C:/adn/report.xlsx, \\\\server\\teh\\share, /var/adn/report.csv, and teh-report.pdf before recieve.";

        var issues = SpellCheckService.FindIssuesInCell(address, text);

        issues.Select(issue => issue.Word).Should().Equal("recieve");
    }

    [Fact]
    public void FindIssuesInCell_IgnoresQuotedAndBracketedFilePathsWithSpaces()
    {
        var address = new CellAddress(SheetId.New(), 1, 1);
        var text = "Open \"C:\\teh folder\\adn report.xlsx\", '\\\\server\\recieve share\\adn file.txt', and [C:\\teh archive\\adn log.csv] before seperate.";

        var issues = SpellCheckService.FindIssuesInCell(address, text);

        issues.Select(issue => issue.Word).Should().Equal("seperate");
    }

    [Fact]
    public void FindIssuesInCell_ReturnsRepeatedWordsInTextOrder()
    {
        var address = new CellAddress(SheetId.New(), 1, 1);

        var issues = SpellCheckService.FindIssuesInCell(address, "Please recieve the the file adn receipt.");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("recieve", "receive"),
            ("the the", "the"),
            ("adn", "and"));
    }

    [Fact]
    public void FindIssuesInCell_ReturnsCasingAwareKnownCorrectionSuggestions()
    {
        var address = new CellAddress(SheetId.New(), 1, 1);

        var issues = SpellCheckService.FindIssuesInCell(address, "TEH Teh teh");

        issues.Select(issue => issue.Suggestion).Should().Equal("THE", "The", "the");
    }

    [Fact]
    public void FindIssuesInCell_UsesCustomDictionaryToSuppressKnownMisspellingsCaseInsensitively()
    {
        var address = new CellAddress(SheetId.New(), 1, 1);
        var customDictionary = new HashSet<string> { "teh" };

        var issues = SpellCheckService.FindIssuesInCell(address, "TEH Teh teh adn", customDictionary);

        issues.Select(issue => issue.Word).Should().Equal("adn");
    }

    [Fact]
    public void FindIssuesInCell_UsesCustomDictionaryToSuppressRepeatedWordPhrases()
    {
        var address = new CellAddress(SheetId.New(), 1, 1);
        var customDictionary = new HashSet<string> { "the the" };

        var issues = SpellCheckService.FindIssuesInCell(address, "the the adn", customDictionary);

        issues.Select(issue => issue.Word).Should().Equal("adn");
    }

    [Fact]
    public void FindIssues_UsesCustomDictionaryAcrossSheetScan()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("teh first"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("TEH adn second"));
        var customDictionary = new HashSet<string> { "TeH" };

        var issues = SpellCheckService.FindIssues(wb, sheet.Id, customDictionary);

        issues.Select(issue => issue.Word).Should().Equal("adn");
    }

    [Fact]
    public void FindIssuesInCell_TracksIssueTextSpansForCurrentOccurrenceActions()
    {
        var address = new CellAddress(SheetId.New(), 1, 1);

        var issues = SpellCheckService.FindIssuesInCell(address, "Fix teh and the the value.");

        issues.Select(issue => (issue.Word, issue.StartIndex, issue.Length)).Should().Equal(
            ("teh", 4, 3),
            ("the the", 12, 7));
    }

    [Fact]
    public void PlanKnownCorrections_ReplacesAllKnownWholeWordsPreservingCapitalization()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        var untouchedAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(textAddress, new TextValue("Teh cat and teh dog recieve mail."));
        sheet.SetCell(untouchedAddress, new TextValue("theme addressed"));

        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        plan.IssueCount.Should().Be(3);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].OriginalText.Should().Be("Teh cat and teh dog recieve mail.");
        plan.Edits[0].CorrectedText.Should().Be("The cat and the dog receive mail.");
        plan.Edits[0].ReplacementCount.Should().Be(3);
    }

    [Fact]
    public void BuildCorrectionCellEdits_ConvertsPlannedCorrectionsToTextCells()
    {
        var address = new CellAddress(SheetId.New(), 2, 3);
        var plan = new SpellingCorrectionPlan(
            [new SpellingCorrectionEdit(address, "teh", "the", 1)],
            IssueCount: 1);

        var edits = SpellCheckService.BuildCorrectionCellEdits(plan);

        edits.Should().ContainSingle();
        edits[0].Address.Should().Be(address);
        edits[0].NewCell.Value.Should().Be(new TextValue("the"));
    }

    [Fact]
    public void PlanKnownCorrections_PreservesKnownCorrectionCasingStyles()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(textAddress, new TextValue("TEH Teh teh"));

        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        plan.IssueCount.Should().Be(3);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].CorrectedText.Should().Be("THE The the");
        plan.Edits[0].ReplacementCount.Should().Be(3);
    }

    [Fact]
    public void PlanKnownCorrections_CoversCommonWorksheetMisspellings()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(textAddress, new TextValue("Calender recomendations for tommorow were a sucess, although wierd."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Calender", "Calendar"),
            ("recomendations", "recommendations"),
            ("tommorow", "tomorrow"),
            ("sucess", "success"),
            ("wierd", "weird"));
        plan.IssueCount.Should().Be(5);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be("Calendar recommendations for tomorrow were a success, although weird.");
        plan.Edits[0].ReplacementCount.Should().Be(5);
    }

    [Fact]
    public void PlanKnownCorrections_CoversExpandedCommonProofingMisspellings()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Buisness begining excelent recieved acheived beleived reports described an enviroment, enviromental review, occurence, occurrance, sucessful, succesful accomodate, accomodation, caluclation, and calcuation."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Buisness", "Business"),
            ("begining", "beginning"),
            ("excelent", "excellent"),
            ("recieved", "received"),
            ("acheived", "achieved"),
            ("beleived", "believed"),
            ("enviroment", "environment"),
            ("enviromental", "environmental"),
            ("occurence", "occurrence"),
            ("occurrance", "occurrence"),
            ("sucessful", "successful"),
            ("succesful", "successful"),
            ("accomodate", "accommodate"),
            ("accomodation", "accommodation"),
            ("caluclation", "calculation"),
            ("calcuation", "calculation"));
        plan.IssueCount.Should().Be(16);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Business beginning excellent received achieved believed reports described an environment, environmental review, occurrence, occurrence, successful, successful accommodate, accommodation, calculation, and calculation.");
        plan.Edits[0].ReplacementCount.Should().Be(16);
    }

    [Fact]
    public void PlanKnownCorrections_CoversBusinessSpreadsheetMisspellings()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Forcast revenu expence sumary colum formular percentatge percenatge quater varience analaysis analsys."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Forcast", "Forecast"),
            ("revenu", "revenue"),
            ("expence", "expense"),
            ("sumary", "summary"),
            ("colum", "column"),
            ("formular", "formula"),
            ("percentatge", "percentage"),
            ("percenatge", "percentage"),
            ("quater", "quarter"),
            ("varience", "variance"),
            ("analaysis", "analysis"),
            ("analsys", "analysis"));
        plan.IssueCount.Should().Be(12);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Forecast revenue expense summary column formula percentage percentage quarter variance analysis analysis.");
        plan.Edits[0].ReplacementCount.Should().Be(12);
    }

    [Fact]
    public void PlanKnownCorrections_DoesNotRewriteIgnoredAddressSpansButCorrectsProse()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Open https://buisness.example.com, email excelent@example.com, and \"C:\\enviroment folder\\buisness file.txt\", then buisness enviroment excelent."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => issue.Word).Should().Equal("buisness", "enviroment", "excelent");
        plan.IssueCount.Should().Be(3);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].CorrectedText.Should().Be(
            "Open https://buisness.example.com, email excelent@example.com, and \"C:\\enviroment folder\\buisness file.txt\", then business environment excellent.");
        plan.Edits[0].ReplacementCount.Should().Be(3);
    }

    [Fact]
    public void ApplyCorrection_ReplacesWholeWordAndPreservesCapitalization()
    {
        var issue = new SpellingIssue(
            new CellAddress(SheetId.New(), 1, 1),
            "Teh",
            "the",
            "Teh item is not the same as other.");

        var corrected = SpellCheckService.ApplyCorrection(issue, "the");

        corrected.Should().Be("The item is not the same as other.");
    }

    [Fact]
    public void ApplyCorrection_CanRemoveRepeatedWordIssue()
    {
        var issue = new SpellingIssue(
            new CellAddress(SheetId.New(), 1, 1),
            "the the",
            "the",
            "Please review the the file.");

        var corrected = SpellCheckService.ApplyCorrection(issue, "the");

        corrected.Should().Be("Please review the file.");
    }

    [Fact]
    public void ApplyCorrection_ReplacesOnlyTheCurrentGeneratedIssueOccurrence()
    {
        var address = new CellAddress(SheetId.New(), 1, 1);
        var issues = SpellCheckService.FindIssuesInCell(address, "teh first and teh second");

        var correctedFirst = SpellCheckService.ApplyCorrection(issues[0], "the");
        var correctedSecond = SpellCheckService.ApplyCorrection(issues[1], "the");

        correctedFirst.Should().Be("the first and teh second");
        correctedSecond.Should().Be("teh first and the second");
    }

    [Fact]
    public void ApplyCorrectionToAllOccurrences_PreservesEachOccurrenceCasingStyle()
    {
        var address = new CellAddress(SheetId.New(), 1, 1);
        var issue = SpellCheckService.FindIssuesInCell(address, "teh TEH Teh").First();

        var corrected = SpellCheckService.ApplyCorrectionToAllOccurrences(issue, "the");

        corrected.Should().Be("the THE The");
    }

    [Fact]
    public void ApplyCorrectionToAllOccurrences_CollapsesRepeatedWordRuns()
    {
        var address = new CellAddress(SheetId.New(), 1, 1);
        var issue = SpellCheckService.FindIssuesInCell(address, "the the the and The The file").First();

        var corrected = SpellCheckService.ApplyCorrectionToAllOccurrences(issue, "the");

        corrected.Should().Be("the and The file");
    }

    [Fact]
    public void ApplyCorrectionToAllOccurrences_SkipsIgnoredAddressSpans()
    {
        var address = new CellAddress(SheetId.New(), 1, 1);
        var issue = SpellCheckService
            .FindIssuesInCell(address, "Fix teh but leave https://teh.example.com and C:\\teh\\file.txt")
            .First();

        var corrected = SpellCheckService.ApplyCorrectionToAllOccurrences(issue, "the");

        corrected.Should().Be("Fix the but leave https://teh.example.com and C:\\teh\\file.txt");
    }

    [Theory]
    [InlineData("TEH", "THE")]
    [InlineData("Teh", "The")]
    [InlineData("teh", "the")]
    public void ApplyCorrection_PreservesKnownCorrectionCasingStyle(string original, string expected)
    {
        var issue = new SpellingIssue(
            new CellAddress(SheetId.New(), 1, 1),
            original,
            "the",
            original);

        var corrected = SpellCheckService.ApplyCorrection(issue, "the");

        corrected.Should().Be(expected);
    }

    [BenchmarkFact]
    public void Benchmark_FindIssuesPlainTextSheet_ReportsTimingAndAllocatedBytes()
    {
        const int rows = 20_000;
        const int iterations = 3;
        var wb = new Workbook("spell");
        var sheet = wb.AddSheet("Sheet1");
        for (uint row = 1; row <= rows; row++)
        {
            sheet.SetCell(
                new CellAddress(sheet.Id, row, 1),
                new TextValue($"Quarterly revenue forecast row {row} is ready for review"));
        }

        SpellCheckService.FindIssues(wb, sheet.Id).Should().BeEmpty();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        IReadOnlyList<SpellingIssue> issues = [];
        for (var i = 0; i < iterations; i++)
        {
            var step = Stopwatch.StartNew();
            issues = SpellCheckService.FindIssues(wb, sheet.Id);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF SPELLCHECK_PLAIN_TEXT " +
            $"rows={rows} steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        issues.Should().BeEmpty();
    }
}
