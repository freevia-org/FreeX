using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class SpellCheckServiceTests
{
    [Fact]
    public void FindIssues_DetectsObviousWorksheetMisspelling()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("A mispelled worksheet heading."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);

        issues.Should().ContainSingle().Which.Should().Be(new SpellingIssue(
            address,
            "mispelled",
            "misspelled",
            "A mispelled worksheet heading.",
            2,
            9));
    }

    [Fact]
    public void FindIssues_DetectsCommonUserTestingProofingMisspellings()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("This speling has erors, a sentance, and bad grammer."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("speling", "spelling"),
            ("erors", "errors"),
            ("sentance", "sentence"),
            ("grammer", "grammar"));
        issues.Should().OnlyContain(issue => issue.Address == address);
    }

    [Fact]
    public void FindIssues_ReturnsEmptyForCleanWorksheetText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A clean worksheet heading."));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));

        SpellCheckService.FindIssues(wb, sheet.Id).Should().BeEmpty();
    }

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

    // shared-proofing-F1: Insert > Text Box content was never scanned by Review > Spelling, so a
    // misspelled word typed into a text box was silently never flagged even though real Excel's
    // Spelling command checks text boxes. FindIssues now walks sheet.TextBoxes the same way it
    // already walks Comments/ThreadedComments, tagging each hit with the owning TextBoxModel.Id so
    // SpellCheckWorkflowPlanner can route a correction back into that exact text box.
    [Fact]
    public void FindIssues_DetectsKnownMisspellingsInTextBoxes()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 3, 2);
        var textBox = new TextBoxModel
        {
            Anchor = anchor,
            Text = "Please recieve teh shipment"
        };
        sheet.TextBoxes.Add(textBox);

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);

        issues.Select(issue => (issue.Address, issue.Word, issue.Suggestion, issue.Source, issue.TextBoxId))
            .Should().Equal(
                (anchor, "recieve", "receive", SpellingIssueSource.TextBox, textBox.Id),
                (anchor, "teh", "the", SpellingIssueSource.TextBox, textBox.Id));
    }

    [Fact]
    public void FindIssues_ReturnsEmptyForCleanTextBoxText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "A clean text box heading."
        });

        SpellCheckService.FindIssues(wb, sheet.Id).Should().BeEmpty();
    }

    // Sibling no-regression: cell text, notes, and threaded comments at the SAME address keep their
    // existing detection order/behavior once a text box is also present on the sheet -- the new
    // TextBoxes walk must not disturb the three pre-existing sources.
    [Fact]
    public void FindIssues_OrdersTextBoxIssuesAlongsideExistingSourcesAtTheSameAddress()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("adn cell"));
        sheet.Comments[address] = "teh note";
        sheet.ThreadedComments[address] = new ThreadedComment("recieve root");
        sheet.TextBoxes.Add(new TextBoxModel { Anchor = address, Text = "occured in box" });

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Source)).Should().Equal(
            ("adn", SpellingIssueSource.CellText),
            ("teh", SpellingIssueSource.Note),
            ("recieve", SpellingIssueSource.ThreadedComment),
            ("occured", SpellingIssueSource.TextBox));
    }

    [Fact]
    public void FindIssues_ReturnsFormulaReportingTyposInTextNotesAndThreadedComments()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("FORMUAL fomula"));
        sheet.Comments[a1] = "foruma note";
        sheet.ThreadedComments[b1] = new ThreadedComment("calculaton root")
        {
            Replies =
            [
                new CommentReply("summarry reply"),
                new CommentReply("summarise subtotaled clean")
            ]
        };

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion, issue.Source, issue.ReplyIndex)).Should().Equal(
            ("FORMUAL", "FORMULA", SpellingIssueSource.CellText, -1),
            ("fomula", "formula", SpellingIssueSource.CellText, -1),
            ("foruma", "formula", SpellingIssueSource.Note, -1),
            ("calculaton", "calculation", SpellingIssueSource.ThreadedComment, -1),
            ("summarry", "summary", SpellingIssueSource.ThreadedCommentReply, 0));
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
    public void FindIssuesInCell_IgnoresFormulaReportingTyposInsideAddressSpans()
    {
        var address = new CellAddress(SheetId.New(), 1, 1);
        var text = "Fix formual, then open https://formual.example.com/fomula, email calculaton@example.com, \"C:\\summarry folder\\foruma.xlsx\", and /tmp/calcuation/report.csv.";

        var issues = SpellCheckService.FindIssuesInCell(address, text);

        issues.Select(issue => issue.Word).Should().Equal("formual");
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
    public void PlanKnownCorrections_CoversFormulaCalculationAndSummaryTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "FORMUAL fomula foruma formular calculaton calcuation caluclation summarry summarise subtotaled."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("FORMUAL", "FORMULA"),
            ("fomula", "formula"),
            ("foruma", "formula"),
            ("formular", "formula"),
            ("calculaton", "calculation"),
            ("calcuation", "calculation"),
            ("caluclation", "calculation"),
            ("summarry", "summary"));
        plan.IssueCount.Should().Be(8);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "FORMULA formula formula formula calculation calculation calculation summary summarise subtotaled.");
        plan.Edits[0].ReplacementCount.Should().Be(8);
    }

    [Fact]
    public void PlanKnownCorrections_CoversFormulaFunctionLookupVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "vlookp vloookup xlookp xloookup pivottabel formla argment argments functon functons. Keep vlookp_path, https://xlookp.example.com/vloookup, analyst@argment.example.com, and \"C:\\functon folder\\pivottabel file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("vlookp", "vlookup"),
            ("vloookup", "vlookup"),
            ("xlookp", "xlookup"),
            ("xloookup", "xlookup"),
            ("pivottabel", "pivot table"),
            ("formla", "formula"),
            ("argment", "argument"),
            ("argments", "arguments"),
            ("functon", "function"),
            ("functons", "functions"));
        plan.IssueCount.Should().Be(10);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "vlookup vlookup xlookup xlookup pivot table formula argument arguments function functions. Keep vlookp_path, https://xlookp.example.com/vloookup, analyst@argment.example.com, and \"C:\\functon folder\\pivottabel file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(10);
    }

    [Fact]
    public void PlanKnownCorrections_CoversChartPivotWorkbookVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "CHRT chrat piviot pviot pivto workbok workboook workshet worsheet slicre slcier. Keep charting, prepiviot, workbok_path, https://chrat.example.com/piviot, analyst@workbok.example.com, and \"C:\\workshet folder\\slicre file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("CHRT", "CHART"),
            ("chrat", "chart"),
            ("piviot", "pivot"),
            ("pviot", "pivot"),
            ("pivto", "pivot"),
            ("workbok", "workbook"),
            ("workboook", "workbook"),
            ("workshet", "worksheet"),
            ("worsheet", "worksheet"),
            ("slicre", "slicer"),
            ("slcier", "slicer"));
        plan.IssueCount.Should().Be(11);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "CHART chart pivot pivot pivot workbook workbook worksheet worksheet slicer slicer. Keep charting, prepiviot, workbok_path, https://chrat.example.com/piviot, analyst@workbok.example.com, and \"C:\\workshet folder\\slicre file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(11);
    }

    [Fact]
    public void PlanKnownCorrections_CoversChartLabelingAndSeriesVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "LEGND Legned lable LABLES SEREIS serise axies AXS Sparklne sparklins. Keep legend, label, labels, series, axis, sparkline, sparklines, prelegnd, lable_text, https://legnd.example.com/sereis, editor@lables.example.com, and \"C:\\sparklne folder\\axs file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("LEGND", "LEGEND"),
            ("Legned", "Legend"),
            ("lable", "label"),
            ("LABLES", "LABELS"),
            ("SEREIS", "SERIES"),
            ("serise", "series"),
            ("axies", "axis"),
            ("AXS", "AXIS"),
            ("Sparklne", "Sparkline"),
            ("sparklins", "sparklines"));
        plan.IssueCount.Should().Be(10);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "LEGEND Legend label LABELS SERIES series axis AXIS Sparkline sparklines. Keep legend, label, labels, series, axis, sparkline, sparklines, prelegnd, lable_text, https://legnd.example.com/sereis, editor@lables.example.com, and \"C:\\sparklne folder\\axs file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(10);
    }

    [Fact]
    public void PlanKnownCorrections_CoversSpreadsheetWorkflowVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "FILTRE fliter fitler SORTNG srot sroting subtoal subtotl subttoal Tabl tbale FORMATING formatng. Keep prefiltre, tabl_name, https://fliter.example.com/subtoal, analyst@formatng.example.com, and \"C:\\formating folder\\filtre file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("FILTRE", "FILTER"),
            ("fliter", "filter"),
            ("fitler", "filter"),
            ("SORTNG", "SORTING"),
            ("srot", "sorting"),
            ("sroting", "sorting"),
            ("subtoal", "subtotal"),
            ("subtotl", "subtotal"),
            ("subttoal", "subtotal"),
            ("Tabl", "Table"),
            ("tbale", "table"),
            ("FORMATING", "FORMATTING"),
            ("formatng", "formatting"));
        plan.IssueCount.Should().Be(13);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "FILTER filter filter SORTING sorting sorting subtotal subtotal subtotal Table table FORMATTING formatting. Keep prefiltre, tabl_name, https://fliter.example.com/subtoal, analyst@formatng.example.com, and \"C:\\formating folder\\filtre file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(13);
    }

    [Fact]
    public void PlanKnownCorrections_CoversTablePivotReportVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Feild fild feilds flitered fitlered flitering totl grnad caluclated refesh sorce timline tiemline. Keep feild_name, preflitered, https://flitered.example.com/timline, analyst@sorce.example.com, and \"C:\\caluclated folder\\refesh file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Feild", "Field"),
            ("fild", "field"),
            ("feilds", "fields"),
            ("flitered", "filtered"),
            ("fitlered", "filtered"),
            ("flitering", "filtering"),
            ("totl", "total"),
            ("grnad", "grand"),
            ("caluclated", "calculated"),
            ("refesh", "refresh"),
            ("sorce", "source"),
            ("timline", "timeline"),
            ("tiemline", "timeline"));
        plan.IssueCount.Should().Be(13);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Field field fields filtered filtered filtering total grand calculated refresh source timeline timeline. Keep feild_name, preflitered, https://flitered.example.com/timline, analyst@sorce.example.com, and \"C:\\caluclated folder\\refesh file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(13);
    }

    [Fact]
    public void PlanKnownCorrections_CoversReferenceRangeValueVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "REFRENCE refrence referance refernece rang raange colomn cloumn transpos tranpose transopse vlaue valus. Keep prerefrence, colomn_name, ranging, https://refrence.example.com/rang, analyst@colomn.example.com, and \"C:\\referance folder\\vlaue file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("REFRENCE", "REFERENCE"),
            ("refrence", "reference"),
            ("referance", "reference"),
            ("refernece", "reference"),
            ("rang", "range"),
            ("raange", "range"),
            ("colomn", "column"),
            ("cloumn", "column"),
            ("transpos", "transpose"),
            ("tranpose", "transpose"),
            ("transopse", "transpose"),
            ("vlaue", "value"),
            ("valus", "values"));
        plan.IssueCount.Should().Be(13);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "REFERENCE reference reference reference range range column column transpose transpose transpose value values. Keep prerefrence, colomn_name, ranging, https://refrence.example.com/rang, analyst@colomn.example.com, and \"C:\\referance folder\\vlaue file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(13);
    }

    [Fact]
    public void PlanKnownCorrections_CoversFinanceSpreadsheetMisspellings()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Liabilty recievable payrole quaterly deductable ammortize AMMORTIZATION depreciaton consolodated reconcilliation benifit forcasted."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Liabilty", "Liability"),
            ("recievable", "receivable"),
            ("payrole", "payroll"),
            ("quaterly", "quarterly"),
            ("deductable", "deductible"),
            ("ammortize", "amortize"),
            ("AMMORTIZATION", "AMORTIZATION"),
            ("depreciaton", "depreciation"),
            ("consolodated", "consolidated"),
            ("reconcilliation", "reconciliation"),
            ("benifit", "benefit"),
            ("forcasted", "forecasted"));
        plan.IssueCount.Should().Be(12);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Liability receivable payroll quarterly deductible amortize AMORTIZATION depreciation consolidated reconciliation benefit forecasted.");
        plan.Edits[0].ReplacementCount.Should().Be(12);
    }

    [Fact]
    public void PlanKnownCorrections_CoversAccountingLedgerVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "ACOUNT acounts acural accrul acrued payble paybles Variace FORECASTNG capitalizaton amortizaton depriciation reconcilation remitance witholding LEDGR jounral Ballance statemnt amout liabilties. Keep https://acount.example.com/payble, accountant@remitance.example.com, and \"C:\\witholding folder\\ledgr file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("ACOUNT", "ACCOUNT"),
            ("acounts", "accounts"),
            ("acural", "accrual"),
            ("accrul", "accrual"),
            ("acrued", "accrued"),
            ("payble", "payable"),
            ("paybles", "payables"),
            ("Variace", "Variance"),
            ("FORECASTNG", "FORECASTING"),
            ("capitalizaton", "capitalization"),
            ("amortizaton", "amortization"),
            ("depriciation", "depreciation"),
            ("reconcilation", "reconciliation"),
            ("remitance", "remittance"),
            ("witholding", "withholding"),
            ("LEDGR", "LEDGER"),
            ("jounral", "journal"),
            ("Ballance", "Balance"),
            ("statemnt", "statement"),
            ("amout", "amount"),
            ("liabilties", "liabilities"));
        plan.IssueCount.Should().Be(21);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "ACCOUNT accounts accrual accrual accrued payable payables Variance FORECASTING capitalization amortization depreciation reconciliation remittance withholding LEDGER journal Balance statement amount liabilities. Keep https://acount.example.com/payble, accountant@remitance.example.com, and \"C:\\witholding folder\\ledgr file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(21);
    }

    [Fact]
    public void PlanKnownCorrections_CoversTaxAuditBillingVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Taxble TAXABL deductable deducton expenss expensses reimbursment reinbursement billng billablee AUDITT auditng witholdings remitances. Valid expensable comptroller vatable. Keep taxble_id, https://taxble.example.com/billng, billing@auditt.example.com, and \"C:\\reimbursment folder\\auditng file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Taxble", "Taxable"),
            ("TAXABL", "TAXABLE"),
            ("deductable", "deductible"),
            ("deducton", "deduction"),
            ("expenss", "expense"),
            ("expensses", "expenses"),
            ("reimbursment", "reimbursement"),
            ("reinbursement", "reimbursement"),
            ("billng", "billing"),
            ("billablee", "billable"),
            ("AUDITT", "AUDIT"),
            ("auditng", "auditing"),
            ("witholdings", "withholdings"),
            ("remitances", "remittances"));
        plan.IssueCount.Should().Be(14);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Taxable TAXABLE deductible deduction expense expenses reimbursement reimbursement billing billable AUDIT auditing withholdings remittances. Valid expensable comptroller vatable. Keep taxble_id, https://taxble.example.com/billng, billing@auditt.example.com, and \"C:\\reimbursment folder\\auditng file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(14);
    }

    [Fact]
    public void PlanKnownCorrections_CoversBankingTreasuryVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "TREASRY cashflw Liqudity collaterl princpal intrest maturty escroww disbursemnt settlemnt transacton bankng. Keep treasry_id, https://treasry.example.com/cashflw, treasury@collaterl.example.com, and \"C:\\settlemnt folder\\transacton file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("TREASRY", "TREASURY"),
            ("cashflw", "cashflow"),
            ("Liqudity", "Liquidity"),
            ("collaterl", "collateral"),
            ("princpal", "principal"),
            ("intrest", "interest"),
            ("maturty", "maturity"),
            ("escroww", "escrow"),
            ("disbursemnt", "disbursement"),
            ("settlemnt", "settlement"),
            ("transacton", "transaction"),
            ("bankng", "banking"));
        plan.IssueCount.Should().Be(12);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "TREASURY cashflow Liquidity collateral principal interest maturity escrow disbursement settlement transaction banking. Keep treasry_id, https://treasry.example.com/cashflw, treasury@collaterl.example.com, and \"C:\\settlemnt folder\\transacton file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(12);
    }

    [Fact]
    public void PlanKnownCorrections_CoversInsuranceActuarialVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "PREMUM deductble Cliam cliams acturial annuitiy underwritng benificiary reinsurence endorsemnt. Keep premum_id, https://premum.example.com/cliam, actuarial@benificiary.example.com, \"C:\\reinsurence folder\\endorsemnt file.xlsx\", and [C:\\deductble folder\\underwritng file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("PREMUM", "PREMIUM"),
            ("deductble", "deductible"),
            ("Cliam", "Claim"),
            ("cliams", "claims"),
            ("acturial", "actuarial"),
            ("annuitiy", "annuity"),
            ("underwritng", "underwriting"),
            ("benificiary", "beneficiary"),
            ("reinsurence", "reinsurance"),
            ("endorsemnt", "endorsement"));
        plan.IssueCount.Should().Be(10);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "PREMIUM deductible Claim claims actuarial annuity underwriting beneficiary reinsurance endorsement. Keep premum_id, https://premum.example.com/cliam, actuarial@benificiary.example.com, \"C:\\reinsurence folder\\endorsemnt file.xlsx\", and [C:\\deductble folder\\underwritng file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(10);
    }

    [Fact]
    public void PlanKnownCorrections_CoversHealthcareClinicalVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "PATINT patints Symptms clinicly treatmnt diagnosys medicaton prescriptn vaccinaton laborotory. Keep patint_id, https://patint.example.com/symptms, clinical@medicaton.example.com, \"C:\\diagnosys folder\\prescriptn file.xlsx\", and [C:\\vaccinaton folder\\laborotory file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("PATINT", "PATIENT"),
            ("patints", "patients"),
            ("Symptms", "Symptoms"),
            ("clinicly", "clinically"),
            ("treatmnt", "treatment"),
            ("diagnosys", "diagnosis"),
            ("medicaton", "medication"),
            ("prescriptn", "prescription"),
            ("vaccinaton", "vaccination"),
            ("laborotory", "laboratory"));
        plan.IssueCount.Should().Be(10);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "PATIENT patients Symptoms clinically treatment diagnosis medication prescription vaccination laboratory. Keep patint_id, https://patint.example.com/symptms, clinical@medicaton.example.com, \"C:\\diagnosys folder\\prescriptn file.xlsx\", and [C:\\vaccinaton folder\\laborotory file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(10);
    }

    [Fact]
    public void PlanKnownCorrections_CoversEducationAcademicVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "STUDNT studnts Clasroom curriculm assignmnt syllbus registrr enrollmnt attendence gradution. Keep studnt_id, https://studnt.example.com/syllbus, academic@assignmnt.example.com, \"C:\\curriculm folder\\registrr file.xlsx\", and [C:\\enrollmnt folder\\gradution file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("STUDNT", "STUDENT"),
            ("studnts", "students"),
            ("Clasroom", "Classroom"),
            ("curriculm", "curriculum"),
            ("assignmnt", "assignment"),
            ("syllbus", "syllabus"),
            ("registrr", "registrar"),
            ("enrollmnt", "enrollment"),
            ("attendence", "attendance"),
            ("gradution", "graduation"));
        plan.IssueCount.Should().Be(10);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "STUDENT students Classroom curriculum assignment syllabus registrar enrollment attendance graduation. Keep studnt_id, https://studnt.example.com/syllbus, academic@assignmnt.example.com, \"C:\\curriculm folder\\registrr file.xlsx\", and [C:\\enrollmnt folder\\gradution file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(10);
    }

    [Fact]
    public void PlanKnownCorrections_CoversFacilitiesRealEstateVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "FACILTY facilties Tenent occupncy leasng maintenence renovatn utilties janitoral inspeciton. Keep facilty_id, https://facilty.example.com/leasng, realestate@maintenence.example.com, \"C:\\occupncy folder\\inspeciton file.xlsx\", and [C:\\renovatn folder\\janitoral file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("FACILTY", "FACILITY"),
            ("facilties", "facilities"),
            ("Tenent", "Tenant"),
            ("occupncy", "occupancy"),
            ("leasng", "leasing"),
            ("maintenence", "maintenance"),
            ("renovatn", "renovation"),
            ("utilties", "utilities"),
            ("janitoral", "janitorial"),
            ("inspeciton", "inspection"));
        plan.IssueCount.Should().Be(10);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "FACILITY facilities Tenant occupancy leasing maintenance renovation utilities janitorial inspection. Keep facilty_id, https://facilty.example.com/leasng, realestate@maintenence.example.com, \"C:\\occupncy folder\\inspeciton file.xlsx\", and [C:\\renovatn folder\\janitoral file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(10);
    }

    [Fact]
    public void PlanKnownCorrections_CoversConstructionFieldServiceVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "CONSTRCTION contracor Subcontracor bluepritn permitt insulatoin excavatoin scafolding SAFETEY punchlistt walkthru Workordr. Keep construction, contractor, subcontractor, blueprint, permit, insulation, excavation, scaffolding, safety, punchlist, walkthrough, workorder, preconstrction, contracor_id, https://contracor.example.com/bluepritn, field@scafolding.example.com, \"C:\\insulatoin folder\\walkthru file.xlsx\", and [C:\\excavatoin folder\\workordr file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("CONSTRCTION", "CONSTRUCTION"),
            ("contracor", "contractor"),
            ("Subcontracor", "Subcontractor"),
            ("bluepritn", "blueprint"),
            ("permitt", "permit"),
            ("insulatoin", "insulation"),
            ("excavatoin", "excavation"),
            ("scafolding", "scaffolding"),
            ("SAFETEY", "SAFETY"),
            ("punchlistt", "punchlist"),
            ("walkthru", "walkthrough"),
            ("Workordr", "Workorder"));
        plan.IssueCount.Should().Be(12);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "CONSTRUCTION contractor Subcontractor blueprint permit insulation excavation scaffolding SAFETY punchlist walkthrough Workorder. Keep construction, contractor, subcontractor, blueprint, permit, insulation, excavation, scaffolding, safety, punchlist, walkthrough, workorder, preconstrction, contracor_id, https://contracor.example.com/bluepritn, field@scafolding.example.com, \"C:\\insulatoin folder\\walkthru file.xlsx\", and [C:\\excavatoin folder\\workordr file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(12);
    }

    [Fact]
    public void PlanKnownCorrections_CoversManufacturingProductionVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "MANUFACTRUING prodction Assembely machinary shiftt yieldd scrapp throughputt downtimee linebalnce. Valid workcenter workcentre. Keep manufactruing_id, https://manufactruing.example.com/prodction, plant@machinary.example.com, \"C:\\throughputt folder\\downtimee file.xlsx\", and [C:\\assembely folder\\linebalnce file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("MANUFACTRUING", "MANUFACTURING"),
            ("prodction", "production"),
            ("Assembely", "Assembly"),
            ("machinary", "machinery"),
            ("shiftt", "shift"),
            ("yieldd", "yield"),
            ("scrapp", "scrap"),
            ("throughputt", "throughput"),
            ("downtimee", "downtime"),
            ("linebalnce", "line balance"));
        plan.IssueCount.Should().Be(10);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "MANUFACTURING production Assembly machinery shift yield scrap throughput downtime line balance. Valid workcenter workcentre. Keep manufactruing_id, https://manufactruing.example.com/prodction, plant@machinary.example.com, \"C:\\throughputt folder\\downtimee file.xlsx\", and [C:\\assembely folder\\linebalnce file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(10);
    }

    [Fact]
    public void PlanKnownCorrections_CoversRetailEcommerceVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "MERCHANDISNG checkuot Catlog catlogue shpping refundd promotn couponn fulfilmnt curbsidee wishlistt. Valid skuunit. Keep merchandisng_id, https://merchandisng.example.com/checkuot, store@fulfilmnt.example.com, \"C:\\catlogue folder\\wishlistt file.xlsx\", and [C:\\shpping folder\\curbsidee file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("MERCHANDISNG", "MERCHANDISING"),
            ("checkuot", "checkout"),
            ("Catlog", "Catalog"),
            ("catlogue", "catalog"),
            ("shpping", "shipping"),
            ("refundd", "refund"),
            ("promotn", "promotion"),
            ("couponn", "coupon"),
            ("fulfilmnt", "fulfillment"),
            ("curbsidee", "curbside"),
            ("wishlistt", "wishlist"));
        plan.IssueCount.Should().Be(11);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "MERCHANDISING checkout Catalog catalog shipping refund promotion coupon fulfillment curbside wishlist. Valid skuunit. Keep merchandisng_id, https://merchandisng.example.com/checkuot, store@fulfilmnt.example.com, \"C:\\catlogue folder\\wishlistt file.xlsx\", and [C:\\shpping folder\\curbsidee file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(11);
    }

    [Fact]
    public void PlanKnownCorrections_CoversEnergyUtilitiesVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "ELECTRICTY generaton Transmision distrubution substaton meterng griddd voltagee waterr sewerr pipelinee emisson sustainablity. Keep electricty_id, https://electricty.example.com/generaton, utility@pipelinee.example.com, \"C:\\substaton folder\\voltagee file.xlsx\", and [C:\\meterng folder\\sustainablity file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("ELECTRICTY", "ELECTRICITY"),
            ("generaton", "generation"),
            ("Transmision", "Transmission"),
            ("distrubution", "distribution"),
            ("substaton", "substation"),
            ("meterng", "metering"),
            ("griddd", "grid"),
            ("voltagee", "voltage"),
            ("waterr", "water"),
            ("sewerr", "sewer"),
            ("pipelinee", "pipeline"),
            ("emisson", "emission"),
            ("sustainablity", "sustainability"));
        plan.IssueCount.Should().Be(13);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "ELECTRICITY generation Transmission distribution substation metering grid voltage water sewer pipeline emission sustainability. Keep electricty_id, https://electricty.example.com/generaton, utility@pipelinee.example.com, \"C:\\substaton folder\\voltagee file.xlsx\", and [C:\\meterng folder\\sustainablity file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(13);
    }

    [Fact]
    public void PlanKnownCorrections_CoversEnvironmentSustainabilityVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "EMISIONS Decarbonizaton renewble biodiveristy conservaton recyling compostng greenhose disclousre EFFICENCY climte Stewardhsip. Keep emissions, decarbonization, renewable, biodiversity, conservation, recycling, composting, greenhouse, disclosure, efficiency, climate, stewardship, preemisions, emisions_id, https://emisions.example.com/recyling, esg@greenhose.example.com, \"C:\\climte folder\\stewardhsip file.xlsx\", and [C:\\disclousre folder\\compostng file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("EMISIONS", "EMISSIONS"),
            ("Decarbonizaton", "Decarbonization"),
            ("renewble", "renewable"),
            ("biodiveristy", "biodiversity"),
            ("conservaton", "conservation"),
            ("recyling", "recycling"),
            ("compostng", "composting"),
            ("greenhose", "greenhouse"),
            ("disclousre", "disclosure"),
            ("EFFICENCY", "EFFICIENCY"),
            ("climte", "climate"),
            ("Stewardhsip", "Stewardship"));
        plan.IssueCount.Should().Be(12);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "EMISSIONS Decarbonization renewable biodiversity conservation recycling composting greenhouse disclosure EFFICIENCY climate Stewardship. Keep emissions, decarbonization, renewable, biodiversity, conservation, recycling, composting, greenhouse, disclosure, efficiency, climate, stewardship, preemisions, emisions_id, https://emisions.example.com/recyling, esg@greenhose.example.com, \"C:\\climte folder\\stewardhsip file.xlsx\", and [C:\\disclousre folder\\compostng file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(12);
    }

    [Fact]
    public void PlanKnownCorrections_CoversTransportLogisticsVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "TRANSPORATION logstics Routng freigt carier fleettt dispatchh mileagee fuelng custms manifst schedulng trailor. Keep transporation_id, https://transporation.example.com/logstics, freight@dispatchh.example.com, \"C:\\schedulng folder\\trailor file.xlsx\", and [C:\\manifst folder\\mileagee file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("TRANSPORATION", "TRANSPORTATION"),
            ("logstics", "logistics"),
            ("Routng", "Routing"),
            ("freigt", "freight"),
            ("carier", "carrier"),
            ("fleettt", "fleet"),
            ("dispatchh", "dispatch"),
            ("mileagee", "mileage"),
            ("fuelng", "fueling"),
            ("custms", "customs"),
            ("manifst", "manifest"),
            ("schedulng", "scheduling"),
            ("trailor", "trailer"));
        plan.IssueCount.Should().Be(13);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "TRANSPORTATION logistics Routing freight carrier fleet dispatch mileage fueling customs manifest scheduling trailer. Keep transporation_id, https://transporation.example.com/logstics, freight@dispatchh.example.com, \"C:\\schedulng folder\\trailor file.xlsx\", and [C:\\manifst folder\\mileagee file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(13);
    }

    [Fact]
    public void PlanKnownCorrections_CoversHospitalityFoodServiceVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "RESTURANT restaraunt Caterng reservaton hospitallity menuu ingredent ingredents alergens nutriton beveragee banquettt roomservce housekeepng conciergee. Valid allergen. Keep resturant_id, https://resturant.example.com/caterng, chef@ingredent.example.com, \"C:\\reservaton folder\\conciergee file.xlsx\", and [C:\\housekeepng folder\\banquettt file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("RESTURANT", "RESTAURANT"),
            ("restaraunt", "restaurant"),
            ("Caterng", "Catering"),
            ("reservaton", "reservation"),
            ("hospitallity", "hospitality"),
            ("menuu", "menu"),
            ("ingredent", "ingredient"),
            ("ingredents", "ingredients"),
            ("alergens", "allergens"),
            ("nutriton", "nutrition"),
            ("beveragee", "beverage"),
            ("banquettt", "banquet"),
            ("roomservce", "room service"),
            ("housekeepng", "housekeeping"),
            ("conciergee", "concierge"));
        plan.IssueCount.Should().Be(15);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "RESTAURANT restaurant Catering reservation hospitality menu ingredient ingredients allergens nutrition beverage banquet room service housekeeping concierge. Valid allergen. Keep resturant_id, https://resturant.example.com/caterng, chef@ingredent.example.com, \"C:\\reservaton folder\\conciergee file.xlsx\", and [C:\\housekeepng folder\\banquettt file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(15);
    }

    [Fact]
    public void PlanKnownCorrections_CoversGovernmentNonprofitVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "MUNICIPL constituant Grantt appropreation compliace Donaton donorrr fundraisin sponsorshipp volunter. Valid government regulation. Keep municipl_id, https://municipl.example.com/donaton, donorrr@fundraisin.example.com, \"C:\\appropreation folder\\sponsorshipp file.xlsx\", and [C:\\volunter folder\\compliace file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("MUNICIPL", "MUNICIPAL"),
            ("constituant", "constituent"),
            ("Grantt", "Grant"),
            ("appropreation", "appropriation"),
            ("compliace", "compliance"),
            ("Donaton", "Donation"),
            ("donorrr", "donor"),
            ("fundraisin", "fundraising"),
            ("sponsorshipp", "sponsorship"),
            ("volunter", "volunteer"));
        plan.IssueCount.Should().Be(10);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "MUNICIPAL constituent Grant appropriation compliance Donation donor fundraising sponsorship volunteer. Valid government regulation. Keep municipl_id, https://municipl.example.com/donaton, donorrr@fundraisin.example.com, \"C:\\appropreation folder\\sponsorshipp file.xlsx\", and [C:\\volunter folder\\compliace file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(10);
    }

    [Fact]
    public void PlanKnownCorrections_CoversCommonReportSpreadsheetTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Availible forcasting statment includes balence, ammount, comparision, and expences; relevent notes were listed seperately, refered, transfered, and occuring."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Availible", "Available"),
            ("forcasting", "forecasting"),
            ("statment", "statement"),
            ("balence", "balance"),
            ("ammount", "amount"),
            ("comparision", "comparison"),
            ("expences", "expenses"),
            ("relevent", "relevant"),
            ("seperately", "separately"),
            ("refered", "referred"),
            ("transfered", "transferred"),
            ("occuring", "occurring"));
        plan.IssueCount.Should().Be(12);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Available forecasting statement includes balance, amount, comparison, and expenses; relevant notes were listed separately, referred, transferred, and occurring.");
        plan.Edits[0].ReplacementCount.Should().Be(12);
    }

    [Fact]
    public void PlanKnownCorrections_CoversDataAnalyticsReportingVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "DASHBORD Dashbaord metrc METIRC metircs Segmnt cohrot ANALYTCS Insigt insigts DATSET querry atribute Dimenson visualizaton aggregaton corelation. Keep dashboard, predashbord, dashbord_id, https://dashbord.example.com/metrc, analyst@dashbord.example.com, and \"C:\\datset folder\\dashbord file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "dashbord DASHBORD Dashbord https://dashbord.example.com/dashbord analyst@dashbord.example.com dashbord_id predashbord \"C:\\dashbord folder\\dashbord file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "dashboard");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("DASHBORD", "DASHBOARD"),
            ("Dashbaord", "Dashboard"),
            ("metrc", "metric"),
            ("METIRC", "METRIC"),
            ("metircs", "metrics"),
            ("Segmnt", "Segment"),
            ("cohrot", "cohort"),
            ("ANALYTCS", "ANALYTICS"),
            ("Insigt", "Insight"),
            ("insigts", "insights"),
            ("DATSET", "DATASET"),
            ("querry", "query"),
            ("atribute", "attribute"),
            ("Dimenson", "Dimension"),
            ("visualizaton", "visualization"),
            ("aggregaton", "aggregation"),
            ("corelation", "correlation"));
        plan.IssueCount.Should().Be(17);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "DASHBOARD Dashboard metric METRIC metrics Segment cohort ANALYTICS Insight insights DATASET query attribute Dimension visualization aggregation correlation. Keep dashboard, predashbord, dashbord_id, https://dashbord.example.com/metrc, analyst@dashbord.example.com, and \"C:\\datset folder\\dashbord file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(17);
        replaceAllCorrected.Should().Be(
            "dashboard DASHBOARD Dashboard https://dashbord.example.com/dashbord analyst@dashbord.example.com dashbord_id predashbord \"C:\\dashbord folder\\dashbord file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversOperationsPlanningVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Milstone Deliverble delivarable scedule SHEDULE dependancy dependancies resouce RESOUCES capcity Utilzation thruput prioritzation. Keep milestone, deliverable, schedule, dependency, dependencies, resource, resources, capacity, utilization, throughput, prioritization, premilstone, deliverble_id, https://scedule.example.com/resouce, planner@capcity.example.com, and \"C:\\utilzation folder\\dependancy file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Milstone", "Milestone"),
            ("Deliverble", "Deliverable"),
            ("delivarable", "deliverable"),
            ("scedule", "schedule"),
            ("SHEDULE", "SCHEDULE"),
            ("dependancy", "dependency"),
            ("dependancies", "dependencies"),
            ("resouce", "resource"),
            ("RESOUCES", "RESOURCES"),
            ("capcity", "capacity"),
            ("Utilzation", "Utilization"),
            ("thruput", "throughput"),
            ("prioritzation", "prioritization"));
        plan.IssueCount.Should().Be(13);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Milestone Deliverable deliverable schedule SCHEDULE dependency dependencies resource RESOURCES capacity Utilization throughput prioritization. Keep milestone, deliverable, schedule, dependency, dependencies, resource, resources, capacity, utilization, throughput, prioritization, premilstone, deliverble_id, https://scedule.example.com/resouce, planner@capcity.example.com, and \"C:\\utilzation folder\\dependancy file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(13);
    }

    [Fact]
    public void PlanKnownCorrections_CoversProductEngineeringPlanningVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Requirment REQUIRMENTS roadmp backlogg sprintt relase releasee Featre featuers bugfixx. Keep requirement, requirements, roadmap, backlog, sprint, release, feature, features, bugfix, prerequirment, sprintt_id, https://roadmp.example.com/relase, planner@featuers.example.com, and \"C:\\backlogg folder\\bugfixx file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Requirment", "Requirement"),
            ("REQUIRMENTS", "REQUIREMENTS"),
            ("roadmp", "roadmap"),
            ("backlogg", "backlog"),
            ("sprintt", "sprint"),
            ("relase", "release"),
            ("releasee", "release"),
            ("Featre", "Feature"),
            ("featuers", "features"),
            ("bugfixx", "bugfix"));
        plan.IssueCount.Should().Be(10);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Requirement REQUIREMENTS roadmap backlog sprint release release Feature features bugfix. Keep requirement, requirements, roadmap, backlog, sprint, release, feature, features, bugfix, prerequirment, sprintt_id, https://roadmp.example.com/relase, planner@featuers.example.com, and \"C:\\backlogg folder\\bugfixx file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(10);
    }

    [Fact]
    public void PlanKnownCorrections_CoversBudgetStakeholderProjectControlVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Budjet BUDGGET Stakehlder stakeholer estimte estimete SPONSER sponsr apporver approvr fundng Allocaton alocation scopee changereq. Keep budget, stakeholder, estimate, sponsor, approver, funding, allocation, scope, change request, prebudjet, apporver_id, https://budjet.example.com/stakehlder, finance@fundng.example.com, and \"C:\\allocaton folder\\scopee file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "budjet BUDJET Budjet https://budjet.example.com/budjet finance@budjet.example.com budjet_id prebudjet \"C:\\budjet folder\\budjet file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "budget");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Budjet", "Budget"),
            ("BUDGGET", "BUDGET"),
            ("Stakehlder", "Stakeholder"),
            ("stakeholer", "stakeholder"),
            ("estimte", "estimate"),
            ("estimete", "estimate"),
            ("SPONSER", "SPONSOR"),
            ("sponsr", "sponsor"),
            ("apporver", "approver"),
            ("approvr", "approver"),
            ("fundng", "funding"),
            ("Allocaton", "Allocation"),
            ("alocation", "allocation"),
            ("scopee", "scope"),
            ("changereq", "change request"));
        plan.IssueCount.Should().Be(15);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Budget BUDGET Stakeholder stakeholder estimate estimate SPONSOR sponsor approver approver funding Allocation allocation scope change request. Keep budget, stakeholder, estimate, sponsor, approver, funding, allocation, scope, change request, prebudjet, apporver_id, https://budjet.example.com/stakehlder, finance@fundng.example.com, and \"C:\\allocaton folder\\scopee file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(15);
        replaceAllCorrected.Should().Be(
            "budget BUDGET Budget https://budjet.example.com/budjet finance@budjet.example.com budjet_id prebudjet \"C:\\budjet folder\\budjet file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversQualityTestingVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Testng QAULITY validaton Verfication defectt REGRESION scenrio Automtion coverge ASSERTON basline Expcted. Keep testing, quality, validation, verification, defect, regression, scenario, automation, coverage, assertion, baseline, expected, pretestng, qaulity_id, https://testng.example.com/validaton, qa@qaulity.example.com, and \"C:\\verfication folder\\defectt file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Testng", "Testing"),
            ("QAULITY", "QUALITY"),
            ("validaton", "validation"),
            ("Verfication", "Verification"),
            ("defectt", "defect"),
            ("REGRESION", "REGRESSION"),
            ("scenrio", "scenario"),
            ("Automtion", "Automation"),
            ("coverge", "coverage"),
            ("ASSERTON", "ASSERTION"),
            ("basline", "baseline"),
            ("Expcted", "Expected"));
        plan.IssueCount.Should().Be(12);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Testing QUALITY validation Verification defect REGRESSION scenario Automation coverage ASSERTION baseline Expected. Keep testing, quality, validation, verification, defect, regression, scenario, automation, coverage, assertion, baseline, expected, pretestng, qaulity_id, https://testng.example.com/validaton, qa@qaulity.example.com, and \"C:\\verfication folder\\defectt file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(12);
    }

    [Fact]
    public void PlanKnownCorrections_CoversCalendarStatusVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Calandar tommorrow dedline deadlne stauts aproval APPROVL complte compelte pendng reivew reveiw. Keep calendar, tomorrow, deadline, status, approval, complete, pending, review, precalandar, aproval_code, https://calandar.example.com/dedline, owner@stauts.example.com, and \"C:\\pendng folder\\reivew file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Calandar", "Calendar"),
            ("tommorrow", "tomorrow"),
            ("dedline", "deadline"),
            ("deadlne", "deadline"),
            ("stauts", "status"),
            ("aproval", "approval"),
            ("APPROVL", "APPROVAL"),
            ("complte", "complete"),
            ("compelte", "complete"),
            ("pendng", "pending"),
            ("reivew", "review"),
            ("reveiw", "review"));
        plan.IssueCount.Should().Be(12);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Calendar tomorrow deadline deadline status approval APPROVAL complete complete pending review review. Keep calendar, tomorrow, deadline, status, approval, complete, pending, review, precalandar, aproval_code, https://calandar.example.com/dedline, owner@stauts.example.com, and \"C:\\pendng folder\\reivew file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(12);
    }

    [Fact]
    public void PlanKnownCorrections_CoversMeetingCommunicationVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "MEATING meetng agnda MINUTS atendee ATTENDES mesage communcation notfication commnet notse. Keep meeting, agenda, minutes, attendee, attendees, message, communication, notification, comment, notes, premeating, mesage_id, https://meating.example.com/mesage, organizer@communcation.example.com, and \"C:\\notfication folder\\commnet file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "mesage MESAGE Mesage https://mesage.example.com/mesage editor@mesage.example.com mesage_id premesage \"C:\\mesage folder\\mesage file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "message");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("MEATING", "MEETING"),
            ("meetng", "meeting"),
            ("agnda", "agenda"),
            ("MINUTS", "MINUTES"),
            ("atendee", "attendee"),
            ("ATTENDES", "ATTENDEES"),
            ("mesage", "message"),
            ("communcation", "communication"),
            ("notfication", "notification"),
            ("commnet", "comment"),
            ("notse", "notes"));
        plan.IssueCount.Should().Be(11);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "MEETING meeting agenda MINUTES attendee ATTENDEES message communication notification comment notes. Keep meeting, agenda, minutes, attendee, attendees, message, communication, notification, comment, notes, premeating, mesage_id, https://meating.example.com/mesage, organizer@communcation.example.com, and \"C:\\notfication folder\\commnet file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(11);
        replaceAllCorrected.Should().Be(
            "message MESSAGE Message https://mesage.example.com/mesage editor@mesage.example.com mesage_id premesage \"C:\\mesage folder\\mesage file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversPeopleHrAndTeamVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Employe emploee EMPLYEE maneger MANGER departmant departmnt benifits vacaton onbord onbording perfomance performnce trainging recruting. Keep employee, manager, department, benefits, vacation, onboard, onboarding, performance, training, recruiting, preemploye, maneger_id, https://employe.example.com/maneger, hr@departmant.example.com, and \"C:\\benifits folder\\vacaton file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "manger MANGER Manger https://manger.example.com/manger lead@manger.example.com manger_id premanger \"C:\\manger folder\\manger file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "manager");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Employe", "Employee"),
            ("emploee", "employee"),
            ("EMPLYEE", "EMPLOYEE"),
            ("maneger", "manager"),
            ("MANGER", "MANAGER"),
            ("departmant", "department"),
            ("departmnt", "department"),
            ("benifits", "benefits"),
            ("vacaton", "vacation"),
            ("onbord", "onboard"),
            ("onbording", "onboarding"),
            ("perfomance", "performance"),
            ("performnce", "performance"),
            ("trainging", "training"),
            ("recruting", "recruiting"));
        plan.IssueCount.Should().Be(15);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Employee employee EMPLOYEE manager MANAGER department department benefits vacation onboard onboarding performance performance training recruiting. Keep employee, manager, department, benefits, vacation, onboard, onboarding, performance, training, recruiting, preemploye, maneger_id, https://employe.example.com/maneger, hr@departmant.example.com, and \"C:\\benifits folder\\vacaton file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(15);
        replaceAllCorrected.Should().Be(
            "manager MANAGER Manager https://manger.example.com/manger lead@manger.example.com manger_id premanger \"C:\\manger folder\\manger file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversRiskActionTrackingVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "RISCK riks isssue isue ACTOIN acion ownre owenr mitgation mitgiate escallate esclation followup. Keep risk, issue, action, owner, mitigation, mitigate, escalate, escalation, follow-up, prerisck, risck_id, https://risck.example.com/actoin, reviewer@isue.example.com, and \"C:\\mitgation folder\\escallate file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("RISCK", "RISK"),
            ("riks", "risk"),
            ("isssue", "issue"),
            ("isue", "issue"),
            ("ACTOIN", "ACTION"),
            ("acion", "action"),
            ("ownre", "owner"),
            ("owenr", "owner"),
            ("mitgation", "mitigation"),
            ("mitgiate", "mitigate"),
            ("escallate", "escalate"),
            ("esclation", "escalation"),
            ("followup", "follow-up"));
        plan.IssueCount.Should().Be(13);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "RISK risk issue issue ACTION action owner owner mitigation mitigate escalate escalation follow-up. Keep risk, issue, action, owner, mitigation, mitigate, escalate, escalation, follow-up, prerisck, risck_id, https://risck.example.com/actoin, reviewer@isue.example.com, and \"C:\\mitgation folder\\escallate file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(13);
    }

    [Fact]
    public void PlanKnownCorrections_CoversLegalCompliancePolicyVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Complaince polcy contrct AGREEMNT privicy CONFIDENTAL confidntial regulaton signiture authorizaton certificaton liablity AUDT. Keep compliance, policy, contract, agreement, privacy, confidential, regulation, signature, authorization, certification, liability, audit, prepolcy, contrct_id, https://complaince.example.com/polcy, legal@privicy.example.com, and \"C:\\confidental folder\\signiture file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "polcy POLCY Polcy https://polcy.example.com/polcy legal@polcy.example.com polcy_id prepolcy \"C:\\polcy folder\\polcy file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "policy");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Complaince", "Compliance"),
            ("polcy", "policy"),
            ("contrct", "contract"),
            ("AGREEMNT", "AGREEMENT"),
            ("privicy", "privacy"),
            ("CONFIDENTAL", "CONFIDENTIAL"),
            ("confidntial", "confidential"),
            ("regulaton", "regulation"),
            ("signiture", "signature"),
            ("authorizaton", "authorization"),
            ("certificaton", "certification"),
            ("liablity", "liability"),
            ("AUDT", "AUDIT"));
        plan.IssueCount.Should().Be(13);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Compliance policy contract AGREEMENT privacy CONFIDENTIAL confidential regulation signature authorization certification liability AUDIT. Keep compliance, policy, contract, agreement, privacy, confidential, regulation, signature, authorization, certification, liability, audit, prepolcy, contrct_id, https://complaince.example.com/polcy, legal@privicy.example.com, and \"C:\\confidental folder\\signiture file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(13);
        replaceAllCorrected.Should().Be(
            "policy POLICY Policy https://polcy.example.com/polcy legal@polcy.example.com polcy_id prepolcy \"C:\\polcy folder\\polcy file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversSecurityAccessControlVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "SECURTY Permisson permisison autentication authentcation autorization encrypton ENCRPYTION Passwrod credental credentail credentails Privlege privleges firewal. Keep security, permission, authentication, authorization, encryption, password, credential, credentials, privilege, privileges, firewall, presecurty, securty_id, https://securty.example.com/permisson, admin@passwrod.example.com, and \"C:\\credental folder\\firewal file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "securty SECURTY Securty https://securty.example.com/securty admin@securty.example.com securty_id presecurty \"C:\\securty folder\\securty file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "security");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("SECURTY", "SECURITY"),
            ("Permisson", "Permission"),
            ("permisison", "permission"),
            ("autentication", "authentication"),
            ("authentcation", "authentication"),
            ("autorization", "authorization"),
            ("encrypton", "encryption"),
            ("ENCRPYTION", "ENCRYPTION"),
            ("Passwrod", "Password"),
            ("credental", "credential"),
            ("credentail", "credential"),
            ("credentails", "credentials"),
            ("Privlege", "Privilege"),
            ("privleges", "privileges"),
            ("firewal", "firewall"));
        plan.IssueCount.Should().Be(15);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "SECURITY Permission permission authentication authentication authorization encryption ENCRYPTION Password credential credential credentials Privilege privileges firewall. Keep security, permission, authentication, authorization, encryption, password, credential, credentials, privilege, privileges, firewall, presecurty, securty_id, https://securty.example.com/permisson, admin@passwrod.example.com, and \"C:\\credental folder\\firewal file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(15);
        replaceAllCorrected.Should().Be(
            "security SECURITY Security https://securty.example.com/securty admin@securty.example.com securty_id presecurty \"C:\\securty folder\\securty file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversItCloudSystemVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Deploymnt DEPLOYEMNT databse DATABSAE Configration CONFIGURATON backkup restoree Migraton Integraton connecton SYNCRONIZE monitering alertng serverr servce cloudd. Keep deployment, database, configuration, backup, restore, migration, integration, connection, synchronize, monitoring, alerting, server, service, cloud, predeploymnt, deploymnt_id, https://deploymnt.example.com/databse, ops@databse.example.com, and \"C:\\deploymnt folder\\serverr file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "deploymnt DEPLOYMNT Deploymnt https://deploymnt.example.com/deploymnt ops@deploymnt.example.com deploymnt_id predeploymnt \"C:\\deploymnt folder\\deploymnt file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "deployment");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Deploymnt", "Deployment"),
            ("DEPLOYEMNT", "DEPLOYMENT"),
            ("databse", "database"),
            ("DATABSAE", "DATABASE"),
            ("Configration", "Configuration"),
            ("CONFIGURATON", "CONFIGURATION"),
            ("backkup", "backup"),
            ("restoree", "restore"),
            ("Migraton", "Migration"),
            ("Integraton", "Integration"),
            ("connecton", "connection"),
            ("SYNCRONIZE", "SYNCHRONIZE"),
            ("monitering", "monitoring"),
            ("alertng", "alerting"),
            ("serverr", "server"),
            ("servce", "service"),
            ("cloudd", "cloud"));
        plan.IssueCount.Should().Be(17);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Deployment DEPLOYMENT database DATABASE Configuration CONFIGURATION backup restore Migration Integration connection SYNCHRONIZE monitoring alerting server service cloud. Keep deployment, database, configuration, backup, restore, migration, integration, connection, synchronize, monitoring, alerting, server, service, cloud, predeploymnt, deploymnt_id, https://deploymnt.example.com/databse, ops@databse.example.com, and \"C:\\deploymnt folder\\serverr file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(17);
        replaceAllCorrected.Should().Be(
            "deployment DEPLOYMENT Deployment https://deploymnt.example.com/deploymnt ops@deploymnt.example.com deploymnt_id predeploymnt \"C:\\deploymnt folder\\deploymnt file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversTelecomNetworkVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "FIBERR opticl Modemm handof LATANCY signall switchh Routerr backhal BROADBAN bandwith roamingg Gatewayy cellularr Subscrber activaton Provisoning. Keep fiber, optical, modem, handoff, latency, signal, switch, router, backhaul, broadband, bandwidth, roaming, gateway, cellular, subscriber, activation, provisioning, prefiberr, fiberr_id, https://fiberr.example.com/routerr, noc@signall.example.com, \"C:\\backhal folder\\gatewayy file.xlsx\", and [C:\\roamingg folder\\provisoning file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "fiberr FIBERR Fiberr https://fiberr.example.com/fiberr noc@fiberr.example.com fiberr_id prefiberr \"C:\\fiberr folder\\fiberr file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "fiber");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("FIBERR", "FIBER"),
            ("opticl", "optical"),
            ("Modemm", "Modem"),
            ("handof", "handoff"),
            ("LATANCY", "LATENCY"),
            ("signall", "signal"),
            ("switchh", "switch"),
            ("Routerr", "Router"),
            ("backhal", "backhaul"),
            ("BROADBAN", "BROADBAND"),
            ("bandwith", "bandwidth"),
            ("roamingg", "roaming"),
            ("Gatewayy", "Gateway"),
            ("cellularr", "cellular"),
            ("Subscrber", "Subscriber"),
            ("activaton", "activation"),
            ("Provisoning", "Provisioning"));
        plan.IssueCount.Should().Be(17);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "FIBER optical Modem handoff LATENCY signal switch Router backhaul BROADBAND bandwidth roaming Gateway cellular Subscriber activation Provisioning. Keep fiber, optical, modem, handoff, latency, signal, switch, router, backhaul, broadband, bandwidth, roaming, gateway, cellular, subscriber, activation, provisioning, prefiberr, fiberr_id, https://fiberr.example.com/routerr, noc@signall.example.com, \"C:\\backhal folder\\gatewayy file.xlsx\", and [C:\\roamingg folder\\provisoning file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(17);
        replaceAllCorrected.Should().Be(
            "fiber FIBER Fiber https://fiberr.example.com/fiberr noc@fiberr.example.com fiberr_id prefiberr \"C:\\fiberr folder\\fiberr file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversReliabilityMaintenanceVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "RELABILITY reliablity Incidentt outtage DOWNTME uptme Availablity MAINTNANCE Recoverey failoverr Redundncy RESILIENC. Keep reliability, incident, outage, downtime, uptime, availability, maintenance, recovery, failover, redundancy, resilience, prerelability, relability_id, https://relability.example.com/outtage, sre@maintnance.example.com, and \"C:\\maintnance folder\\failoverr file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("RELABILITY", "RELIABILITY"),
            ("reliablity", "reliability"),
            ("Incidentt", "Incident"),
            ("outtage", "outage"),
            ("DOWNTME", "DOWNTIME"),
            ("uptme", "uptime"),
            ("Availablity", "Availability"),
            ("MAINTNANCE", "MAINTENANCE"),
            ("Recoverey", "Recovery"),
            ("failoverr", "failover"),
            ("Redundncy", "Redundancy"),
            ("RESILIENC", "RESILIENCE"));
        plan.IssueCount.Should().Be(12);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "RELIABILITY reliability Incident outage DOWNTIME uptime Availability MAINTENANCE Recovery failover Redundancy RESILIENCE. Keep reliability, incident, outage, downtime, uptime, availability, maintenance, recovery, failover, redundancy, resilience, prerelability, relability_id, https://relability.example.com/outtage, sre@maintnance.example.com, and \"C:\\maintnance folder\\failoverr file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(12);
    }

    [Fact]
    public void PlanKnownCorrections_CoversInvoiceSupplyChainVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Custmer CUSTMR customr vendro ORDR odrer invoce invioce Recipt PAYMNT payemnt paymetn shipmnt shippment delivry QUANITY quantiy purchse purcahse. Keep customer, vendor, order, invoice, receipt, payment, shipment, delivery, quantity, purchase, precustmer, custmer_id, https://custmer.example.com/invoce, buyer@vendro.example.com, and \"C:\\paymnt folder\\shippment file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "invoce INVOCE Invoce https://invoce.example.com/invoce buyer@invoce.example.com invoce_id preinvoce \"C:\\invoce folder\\invoce file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "invoice");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Custmer", "Customer"),
            ("CUSTMR", "CUSTOMER"),
            ("customr", "customer"),
            ("vendro", "vendor"),
            ("ORDR", "ORDER"),
            ("odrer", "order"),
            ("invoce", "invoice"),
            ("invioce", "invoice"),
            ("Recipt", "Receipt"),
            ("PAYMNT", "PAYMENT"),
            ("payemnt", "payment"),
            ("paymetn", "payment"),
            ("shipmnt", "shipment"),
            ("shippment", "shipment"),
            ("delivry", "delivery"),
            ("QUANITY", "QUANTITY"),
            ("quantiy", "quantity"),
            ("purchse", "purchase"),
            ("purcahse", "purchase"));
        plan.IssueCount.Should().Be(19);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Customer CUSTOMER customer vendor ORDER order invoice invoice Receipt PAYMENT payment payment shipment shipment delivery QUANTITY quantity purchase purchase. Keep customer, vendor, order, invoice, receipt, payment, shipment, delivery, quantity, purchase, precustmer, custmer_id, https://custmer.example.com/invoce, buyer@vendro.example.com, and \"C:\\paymnt folder\\shippment file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(19);
        replaceAllCorrected.Should().Be(
            "invoice INVOICE Invoice https://invoce.example.com/invoce buyer@invoce.example.com invoce_id preinvoce \"C:\\invoce folder\\invoce file.xlsx\".");
    }

    [Theory]
    [InlineData("Inventry", "Inventory")]
    [InlineData("inventroy", "inventory")]
    [InlineData("SUPPLER", "SUPPLIER")]
    [InlineData("suplier", "supplier")]
    [InlineData("warehous", "warehouse")]
    [InlineData("procuremnt", "procurement")]
    [InlineData("fulfillmnt", "fulfillment")]
    [InlineData("backordr", "backorder")]
    [InlineData("reorderd", "reordered")]
    [InlineData("stockk", "stock")]
    [InlineData("recieving", "receiving")]
    public void FindIssuesInCell_CoversProcurementInventorySupplierVocabularyTypos(
        string word,
        string expectedSuggestion)
    {
        var address = new CellAddress(SheetId.New(), 1, 1);

        var issues = SpellCheckService.FindIssuesInCell(address, $"Review {word}.");

        issues.Should().HaveCount(1);
        issues[0].Word.Should().Be(word);
        issues[0].Suggestion.Should().Be(expectedSuggestion);
    }

    [Fact]
    public void PlanKnownCorrections_CoversProcurementInventorySupplierSentenceAndIgnoredSpans()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Procuremnt inventry inventroy from suppler suplier at warehous, then fulfillmnt backordr reorderd stockk recieving. Keep https://inventry.example.com/procuremnt, ops@suppler.example.com, and \"C:\\warehous folder\\backordr file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => issue.Word).Should().Equal(
            "Procuremnt",
            "inventry",
            "inventroy",
            "suppler",
            "suplier",
            "warehous",
            "fulfillmnt",
            "backordr",
            "reorderd",
            "stockk",
            "recieving");
        plan.IssueCount.Should().Be(11);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Procurement inventory inventory from supplier supplier at warehouse, then fulfillment backorder reordered stock receiving. Keep https://inventry.example.com/procuremnt, ops@suppler.example.com, and \"C:\\warehous folder\\backordr file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(11);
    }

    [Fact]
    public void PlanKnownCorrections_CoversSalesMarketingCustomerVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Campain campains marketting advertisment ADVERTISMENTS pipline Opportunty oppertunity prospectve prospec convertion conversoin retenton churnn QOUTE quotaton pricng discountng. Keep campaign, campaigns, marketing, advertisement, advertisements, pipeline, opportunity, prospective, prospect, conversion, retention, churn, quote, quotation, pricing, discounting, customer, precampain, campain_id, https://campain.example.com/pipline, sales@marketting.example.com, and \"C:\\campain folder\\qoute file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "campain CAMPAIN Campain https://campain.example.com/campain sales@campain.example.com campain_id precampain \"C:\\campain folder\\campain file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "campaign");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Campain", "Campaign"),
            ("campains", "campaigns"),
            ("marketting", "marketing"),
            ("advertisment", "advertisement"),
            ("ADVERTISMENTS", "ADVERTISEMENTS"),
            ("pipline", "pipeline"),
            ("Opportunty", "Opportunity"),
            ("oppertunity", "opportunity"),
            ("prospectve", "prospective"),
            ("prospec", "prospect"),
            ("convertion", "conversion"),
            ("conversoin", "conversion"),
            ("retenton", "retention"),
            ("churnn", "churn"),
            ("QOUTE", "QUOTE"),
            ("quotaton", "quotation"),
            ("pricng", "pricing"),
            ("discountng", "discounting"));
        plan.IssueCount.Should().Be(18);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Campaign campaigns marketing advertisement ADVERTISEMENTS pipeline Opportunity opportunity prospective prospect conversion conversion retention churn QUOTE quotation pricing discounting. Keep campaign, campaigns, marketing, advertisement, advertisements, pipeline, opportunity, prospective, prospect, conversion, retention, churn, quote, quotation, pricing, discounting, customer, precampain, campain_id, https://campain.example.com/pipline, sales@marketting.example.com, and \"C:\\campain folder\\qoute file.xlsx\".");
        plan.Edits[0].ReplacementCount.Should().Be(18);
        replaceAllCorrected.Should().Be(
            "campaign CAMPAIGN Campaign https://campain.example.com/campain sales@campain.example.com campain_id precampain \"C:\\campain folder\\campain file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversMediaCreativeDesignVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "MOCKP desgin Brandng pallete vectorr rasterr exportt wirefram Prototyp kerningg renderng ANIMATON typograpy storybord compositon photogrphy copywritng iconogrphy Illustation. Keep mockup, design, branding, palette, vector, raster, export, wireframe, prototype, kerning, rendering, animation, typography, storyboard, composition, photography, copywriting, iconography, illustration, premockp, mockp_id, https://mockp.example.com/vectorr, creative@pallete.example.com, \"C:\\renderng folder\\iconogrphy file.xlsx\", and [C:\\storybord folder\\illustation file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "mockp MOCKP Mockp https://mockp.example.com/mockp creative@mockp.example.com mockp_id premockp \"C:\\mockp folder\\mockp file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "mockup");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("MOCKP", "MOCKUP"),
            ("desgin", "design"),
            ("Brandng", "Branding"),
            ("pallete", "palette"),
            ("vectorr", "vector"),
            ("rasterr", "raster"),
            ("exportt", "export"),
            ("wirefram", "wireframe"),
            ("Prototyp", "Prototype"),
            ("kerningg", "kerning"),
            ("renderng", "rendering"),
            ("ANIMATON", "ANIMATION"),
            ("typograpy", "typography"),
            ("storybord", "storyboard"),
            ("compositon", "composition"),
            ("photogrphy", "photography"),
            ("copywritng", "copywriting"),
            ("iconogrphy", "iconography"),
            ("Illustation", "Illustration"));
        plan.IssueCount.Should().Be(19);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "MOCKUP design Branding palette vector raster export wireframe Prototype kerning rendering ANIMATION typography storyboard composition photography copywriting iconography Illustration. Keep mockup, design, branding, palette, vector, raster, export, wireframe, prototype, kerning, rendering, animation, typography, storyboard, composition, photography, copywriting, iconography, illustration, premockp, mockp_id, https://mockp.example.com/vectorr, creative@pallete.example.com, \"C:\\renderng folder\\iconogrphy file.xlsx\", and [C:\\storybord folder\\illustation file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(19);
        replaceAllCorrected.Should().Be(
            "mockup MOCKUP Mockup https://mockp.example.com/mockp creative@mockp.example.com mockp_id premockp \"C:\\mockp folder\\mockp file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversResearchLabScienceVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "RESERCH Experment sampel SAMPLNG labratory Protocolll hypothsis ANALYSISIS reagentt Calibraton microscop sequencng Genotypingg chromatograpy spectromtry Centrifugee incubaton replicatee Specimennt. Keep research, experiment, sample, sampling, laboratory, protocol, hypothesis, analysis, reagent, calibration, microscope, sequencing, genotyping, chromatography, spectrometry, centrifuge, incubation, replicate, specimen, prereserch, reserch_id, https://reserch.example.com/microscop, lab@reagentt.example.com, \"C:\\labratory folder\\sequencng file.xlsx\", and [C:\\chromatograpy folder\\spectromtry file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "reserch RESERCH Reserch https://reserch.example.com/reserch lab@reserch.example.com reserch_id prereserch \"C:\\reserch folder\\reserch file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "research");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("RESERCH", "RESEARCH"),
            ("Experment", "Experiment"),
            ("sampel", "sample"),
            ("SAMPLNG", "SAMPLING"),
            ("labratory", "laboratory"),
            ("Protocolll", "Protocol"),
            ("hypothsis", "hypothesis"),
            ("ANALYSISIS", "ANALYSIS"),
            ("reagentt", "reagent"),
            ("Calibraton", "Calibration"),
            ("microscop", "microscope"),
            ("sequencng", "sequencing"),
            ("Genotypingg", "Genotyping"),
            ("chromatograpy", "chromatography"),
            ("spectromtry", "spectrometry"),
            ("Centrifugee", "Centrifuge"),
            ("incubaton", "incubation"),
            ("replicatee", "replicate"),
            ("Specimennt", "Specimen"));
        plan.IssueCount.Should().Be(19);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "RESEARCH Experiment sample SAMPLING laboratory Protocol hypothesis ANALYSIS reagent Calibration microscope sequencing Genotyping chromatography spectrometry Centrifuge incubation replicate Specimen. Keep research, experiment, sample, sampling, laboratory, protocol, hypothesis, analysis, reagent, calibration, microscope, sequencing, genotyping, chromatography, spectrometry, centrifuge, incubation, replicate, specimen, prereserch, reserch_id, https://reserch.example.com/microscop, lab@reagentt.example.com, \"C:\\labratory folder\\sequencng file.xlsx\", and [C:\\chromatograpy folder\\spectromtry file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(19);
        replaceAllCorrected.Should().Be(
            "research RESEARCH Research https://reserch.example.com/reserch lab@reserch.example.com reserch_id prereserch \"C:\\reserch folder\\reserch file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversAgricultureFieldOperationsVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "HARVST Irrigaton fertilzer PESTICDE croping Seedlng greenhous Livestok pasturee ORCHRD vinyard grazng Fencng manuree sprayng Nurseryy pollinaton RIPENES grainn. Keep harvest, irrigation, fertilizer, pesticide, cropping, seedling, greenhouse, livestock, pasture, orchard, vineyard, grazing, fencing, manure, spraying, nursery, pollination, ripeness, grain, preharvst, harvst_id, https://harvst.example.com/irrigaton, farm@fertilzer.example.com, \"C:\\greenhous folder\\sprayng file.xlsx\", and [C:\\vinyard folder\\pollinaton file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "harvst HARVST Harvst https://harvst.example.com/harvst farm@harvst.example.com harvst_id preharvst \"C:\\harvst folder\\harvst file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "harvest");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("HARVST", "HARVEST"),
            ("Irrigaton", "Irrigation"),
            ("fertilzer", "fertilizer"),
            ("PESTICDE", "PESTICIDE"),
            ("croping", "cropping"),
            ("Seedlng", "Seedling"),
            ("greenhous", "greenhouse"),
            ("Livestok", "Livestock"),
            ("pasturee", "pasture"),
            ("ORCHRD", "ORCHARD"),
            ("vinyard", "vineyard"),
            ("grazng", "grazing"),
            ("Fencng", "Fencing"),
            ("manuree", "manure"),
            ("sprayng", "spraying"),
            ("Nurseryy", "Nursery"),
            ("pollinaton", "pollination"),
            ("RIPENES", "RIPENESS"),
            ("grainn", "grain"));
        plan.IssueCount.Should().Be(19);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "HARVEST Irrigation fertilizer PESTICIDE cropping Seedling greenhouse Livestock pasture ORCHARD vineyard grazing Fencing manure spraying Nursery pollination RIPENESS grain. Keep harvest, irrigation, fertilizer, pesticide, cropping, seedling, greenhouse, livestock, pasture, orchard, vineyard, grazing, fencing, manure, spraying, nursery, pollination, ripeness, grain, preharvst, harvst_id, https://harvst.example.com/irrigaton, farm@fertilzer.example.com, \"C:\\greenhous folder\\sprayng file.xlsx\", and [C:\\vinyard folder\\pollinaton file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(19);
        replaceAllCorrected.Should().Be(
            "harvest HARVEST Harvest https://harvst.example.com/harvst farm@harvst.example.com harvst_id preharvst \"C:\\harvst folder\\harvst file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversTravelEventVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "TRAVELL Itinery bookng FLIGTH airlnie Departue arival PASSPRT bagage Lugage boardng Airfaire shuttel VENEU confernce Registraton sessoin Speker exhbit Bootth. Keep travel, itinerary, booking, flight, airline, departure, arrival, passport, baggage, luggage, boarding, airfare, shuttle, venue, conference, registration, session, speaker, exhibit, booth, pretravell, travell_id, https://travell.example.com/confernce, events@airlnie.example.com, \"C:\\departue folder\\boardng file.xlsx\", and [C:\\veneu folder\\registraton file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "travell TRAVELL Travell https://travell.example.com/travell events@travell.example.com travell_id pretravell \"C:\\travell folder\\travell file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "travel");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("TRAVELL", "TRAVEL"),
            ("Itinery", "Itinerary"),
            ("bookng", "booking"),
            ("FLIGTH", "FLIGHT"),
            ("airlnie", "airline"),
            ("Departue", "Departure"),
            ("arival", "arrival"),
            ("PASSPRT", "PASSPORT"),
            ("bagage", "baggage"),
            ("Lugage", "Luggage"),
            ("boardng", "boarding"),
            ("Airfaire", "Airfare"),
            ("shuttel", "shuttle"),
            ("VENEU", "VENUE"),
            ("confernce", "conference"),
            ("Registraton", "Registration"),
            ("sessoin", "session"),
            ("Speker", "Speaker"),
            ("exhbit", "exhibit"),
            ("Bootth", "Booth"));
        plan.IssueCount.Should().Be(20);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "TRAVEL Itinerary booking FLIGHT airline Departure arrival PASSPORT baggage Luggage boarding Airfare shuttle VENUE conference Registration session Speaker exhibit Booth. Keep travel, itinerary, booking, flight, airline, departure, arrival, passport, baggage, luggage, boarding, airfare, shuttle, venue, conference, registration, session, speaker, exhibit, booth, pretravell, travell_id, https://travell.example.com/confernce, events@airlnie.example.com, \"C:\\departue folder\\boardng file.xlsx\", and [C:\\veneu folder\\registraton file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(20);
        replaceAllCorrected.Should().Be(
            "travel TRAVEL Travel https://travell.example.com/travell events@travell.example.com travell_id pretravell \"C:\\travell folder\\travell file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversSportsFitnessWellnessVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "ATHLEET Athelete competion TOURNMENT pracitce Equpment fitnes Wellnes exercize workot Leaguee SEASN scorebord scorng Officiatng conditoning Hydraton membrship Schedual regimn Rehabilitaton. Keep athlete, competition, tournament, practice, equipment, fitness, wellness, exercise, workout, league, season, scoreboard, scoring, officiating, conditioning, hydration, membership, schedule, regimen, rehabilitation, preathleet, athleet_id, https://athleet.example.com/scorebord, coach@wellnes.example.com, \"C:\\fitnes folder\\workot file.xlsx\", and [C:\\leaguee folder\\rehabilitaton file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "athleet ATHLEET Athleet https://athleet.example.com/athleet coach@athleet.example.com athleet_id preathleet \"C:\\athleet folder\\athleet file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "athlete");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("ATHLEET", "ATHLETE"),
            ("Athelete", "Athlete"),
            ("competion", "competition"),
            ("TOURNMENT", "TOURNAMENT"),
            ("pracitce", "practice"),
            ("Equpment", "Equipment"),
            ("fitnes", "fitness"),
            ("Wellnes", "Wellness"),
            ("exercize", "exercise"),
            ("workot", "workout"),
            ("Leaguee", "League"),
            ("SEASN", "SEASON"),
            ("scorebord", "scoreboard"),
            ("scorng", "scoring"),
            ("Officiatng", "Officiating"),
            ("conditoning", "conditioning"),
            ("Hydraton", "Hydration"),
            ("membrship", "membership"),
            ("Schedual", "Schedule"),
            ("regimn", "regimen"),
            ("Rehabilitaton", "Rehabilitation"));
        plan.IssueCount.Should().Be(21);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "ATHLETE Athlete competition TOURNAMENT practice Equipment fitness Wellness exercise workout League SEASON scoreboard scoring Officiating conditioning Hydration membership Schedule regimen Rehabilitation. Keep athlete, competition, tournament, practice, equipment, fitness, wellness, exercise, workout, league, season, scoreboard, scoring, officiating, conditioning, hydration, membership, schedule, regimen, rehabilitation, preathleet, athleet_id, https://athleet.example.com/scorebord, coach@wellnes.example.com, \"C:\\fitnes folder\\workot file.xlsx\", and [C:\\leaguee folder\\rehabilitaton file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(21);
        replaceAllCorrected.Should().Be(
            "athlete ATHLETE Athlete https://athleet.example.com/athleet coach@athleet.example.com athleet_id preathleet \"C:\\athleet folder\\athleet file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversPublicSafetyWeatherEmergencyVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "EMERGNCY Evacuaton sheltr RESPNSE hazrd Wildifre floodng Stormm smokee Alertt drilll Sirenn rescuee OUTBREK quarantne Sanitaton dispatchr Weathr forcast Advisry warningg. Keep emergency, evacuation, shelter, response, hazard, wildfire, flooding, storm, smoke, alert, drill, siren, rescue, outbreak, quarantine, sanitation, dispatcher, weather, forecast, advisory, warning, preemergncy, emergncy_id, https://emergncy.example.com/floodng, safety@weathr.example.com, \"C:\\stormm folder\\warningg file.xlsx\", and [C:\\dispatchr folder\\quarantne file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "emergncy EMERGNCY Emergncy https://emergncy.example.com/emergncy safety@emergncy.example.com emergncy_id preemergncy \"C:\\emergncy folder\\emergncy file.xlsx\".")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "emergency");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("EMERGNCY", "EMERGENCY"),
            ("Evacuaton", "Evacuation"),
            ("sheltr", "shelter"),
            ("RESPNSE", "RESPONSE"),
            ("hazrd", "hazard"),
            ("Wildifre", "Wildfire"),
            ("floodng", "flooding"),
            ("Stormm", "Storm"),
            ("smokee", "smoke"),
            ("Alertt", "Alert"),
            ("drilll", "drill"),
            ("Sirenn", "Siren"),
            ("rescuee", "rescue"),
            ("OUTBREK", "OUTBREAK"),
            ("quarantne", "quarantine"),
            ("Sanitaton", "Sanitation"),
            ("dispatchr", "dispatcher"),
            ("Weathr", "Weather"),
            ("forcast", "forecast"),
            ("Advisry", "Advisory"),
            ("warningg", "warning"));
        plan.IssueCount.Should().Be(21);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "EMERGENCY Evacuation shelter RESPONSE hazard Wildfire flooding Storm smoke Alert drill Siren rescue OUTBREAK quarantine Sanitation dispatcher Weather forecast Advisory warning. Keep emergency, evacuation, shelter, response, hazard, wildfire, flooding, storm, smoke, alert, drill, siren, rescue, outbreak, quarantine, sanitation, dispatcher, weather, forecast, advisory, warning, preemergncy, emergncy_id, https://emergncy.example.com/floodng, safety@weathr.example.com, \"C:\\stormm folder\\warningg file.xlsx\", and [C:\\dispatchr folder\\quarantne file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(21);
        replaceAllCorrected.Should().Be(
            "emergency EMERGENCY Emergency https://emergncy.example.com/emergncy safety@emergncy.example.com emergncy_id preemergncy \"C:\\emergncy folder\\emergncy file.xlsx\".");
    }

    [Fact]
    public void PlanKnownCorrections_CoversHelpdeskSlaVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "HELPDESKK Escalaton incidnt Workarond prioroty Severty outagee ticketng QUEU Breachd servcelevel Supportdesk triagee Callbackk chatbottt. Keep helpdesk, escalation, incident, workaround, priority, severity, outage, ticketing, queue, breached, service level, support desk, triage, callback, chatbot, prehelpdeskk, helpdeskk_id, https://helpdeskk.example.com/ticketng, desk@severty.example.com, \"C:\\incidnt folder\\workarond file.xlsx\", and [C:\\callbackk folder\\servcelevel file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "helpdeskk HELPDESKK Helpdeskk https://helpdeskk.example.com/helpdeskk desk@helpdeskk.example.com helpdeskk_id prehelpdeskk \"C:\\helpdeskk folder\\helpdeskk file.xlsx\" [C:\\helpdeskk folder\\helpdeskk file.xlsx].")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "helpdesk");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("HELPDESKK", "HELPDESK"),
            ("Escalaton", "Escalation"),
            ("incidnt", "incident"),
            ("Workarond", "Workaround"),
            ("prioroty", "priority"),
            ("Severty", "Severity"),
            ("outagee", "outage"),
            ("ticketng", "ticketing"),
            ("QUEU", "QUEUE"),
            ("Breachd", "Breached"),
            ("servcelevel", "service level"),
            ("Supportdesk", "Support desk"),
            ("triagee", "triage"),
            ("Callbackk", "Callback"),
            ("chatbottt", "chatbot"));
        plan.IssueCount.Should().Be(15);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "HELPDESK Escalation incident Workaround priority Severity outage ticketing QUEUE Breached service level Support desk triage Callback chatbot. Keep helpdesk, escalation, incident, workaround, priority, severity, outage, ticketing, queue, breached, service level, support desk, triage, callback, chatbot, prehelpdeskk, helpdeskk_id, https://helpdeskk.example.com/ticketng, desk@severty.example.com, \"C:\\incidnt folder\\workarond file.xlsx\", and [C:\\callbackk folder\\servcelevel file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(15);
        replaceAllCorrected.Should().Be(
            "helpdesk HELPDESK Helpdesk https://helpdeskk.example.com/helpdeskk desk@helpdeskk.example.com helpdeskk_id prehelpdeskk \"C:\\helpdeskk folder\\helpdeskk file.xlsx\" [C:\\helpdeskk folder\\helpdeskk file.xlsx].");
    }

    [Fact]
    public void PlanKnownCorrections_CoversSubscriptionLicensingRenewalVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "SUBSCRPTION Subscribtion licnse Licensng renewl Renewel expiraton Expirng cancelation CANCELLATON entitlment Overagee prorateed SEATSS triall Billngcycle. Keep subscription, license, licensing, renewal, expiration, expiring, cancellation, entitlement, overage, prorated, seats, trial, billing cycle, presubscrption, subscrption_id, https://subscrption.example.com/licensng, renewals@expiraton.example.com, \"C:\\licnse folder\\renewl file.xlsx\", and [C:\\triall folder\\billngcycle file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "subscrption SUBSCRPTION Subscrption https://subscrption.example.com/subscrption billing@subscrption.example.com subscrption_id presubscrption \"C:\\subscrption folder\\subscrption file.xlsx\" [C:\\subscrption folder\\subscrption file.xlsx].")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "subscription");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("SUBSCRPTION", "SUBSCRIPTION"),
            ("Subscribtion", "Subscription"),
            ("licnse", "license"),
            ("Licensng", "Licensing"),
            ("renewl", "renewal"),
            ("Renewel", "Renewal"),
            ("expiraton", "expiration"),
            ("Expirng", "Expiring"),
            ("cancelation", "cancellation"),
            ("CANCELLATON", "CANCELLATION"),
            ("entitlment", "entitlement"),
            ("Overagee", "Overage"),
            ("prorateed", "prorated"),
            ("SEATSS", "SEATS"),
            ("triall", "trial"),
            ("Billngcycle", "Billing cycle"));
        plan.IssueCount.Should().Be(16);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "SUBSCRIPTION Subscription license Licensing renewal Renewal expiration Expiring cancellation CANCELLATION entitlement Overage prorated SEATS trial Billing cycle. Keep subscription, license, licensing, renewal, expiration, expiring, cancellation, entitlement, overage, prorated, seats, trial, billing cycle, presubscrption, subscrption_id, https://subscrption.example.com/licensng, renewals@expiraton.example.com, \"C:\\licnse folder\\renewl file.xlsx\", and [C:\\triall folder\\billngcycle file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(16);
        replaceAllCorrected.Should().Be(
            "subscription SUBSCRIPTION Subscription https://subscrption.example.com/subscrption billing@subscrption.example.com subscrption_id presubscrption \"C:\\subscrption folder\\subscrption file.xlsx\" [C:\\subscrption folder\\subscrption file.xlsx].");
    }

    [Fact]
    public void PlanKnownCorrections_CoversUiAccessibilityRibbonVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "ACCESIBILITY Acessibility keybord Shortct SHORTCUTT ribbn Toolbaar dialogg BUTON checkbx Comboboxx tooltp Navigaton focuss Screenreder Alttextt Keytipp. Keep accessibility, keyboard, shortcut, ribbon, toolbar, dialog, button, checkbox, combo box, tooltip, navigation, focus, screen reader, alt text, keytip, preaccesibility, accesibility_id, https://accesibility.example.com/toolbaar, ui@keybord.example.com, \"C:\\ribbn folder\\tooltp file.xlsx\", and [C:\\alttextt folder\\screenreder file.xlsx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "accesibility ACCESIBILITY Accesibility https://accesibility.example.com/accesibility ui@accesibility.example.com accesibility_id preaccesibility \"C:\\accesibility folder\\accesibility file.xlsx\" [C:\\accesibility folder\\accesibility file.xlsx].")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "accessibility");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("ACCESIBILITY", "ACCESSIBILITY"),
            ("Acessibility", "Accessibility"),
            ("keybord", "keyboard"),
            ("Shortct", "Shortcut"),
            ("SHORTCUTT", "SHORTCUT"),
            ("ribbn", "ribbon"),
            ("Toolbaar", "Toolbar"),
            ("dialogg", "dialog"),
            ("BUTON", "BUTTON"),
            ("checkbx", "checkbox"),
            ("Comboboxx", "Combo box"),
            ("tooltp", "tooltip"),
            ("Navigaton", "Navigation"),
            ("focuss", "focus"),
            ("Screenreder", "Screen reader"),
            ("Alttextt", "Alt text"),
            ("Keytipp", "Keytip"));
        plan.IssueCount.Should().Be(17);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "ACCESSIBILITY Accessibility keyboard Shortcut SHORTCUT ribbon Toolbar dialog BUTTON checkbox Combo box tooltip Navigation focus Screen reader Alt text Keytip. Keep accessibility, keyboard, shortcut, ribbon, toolbar, dialog, button, checkbox, combo box, tooltip, navigation, focus, screen reader, alt text, keytip, preaccesibility, accesibility_id, https://accesibility.example.com/toolbaar, ui@keybord.example.com, \"C:\\ribbn folder\\tooltp file.xlsx\", and [C:\\alttextt folder\\screenreder file.xlsx].");
        plan.Edits[0].ReplacementCount.Should().Be(17);
        replaceAllCorrected.Should().Be(
            "accessibility ACCESSIBILITY Accessibility https://accesibility.example.com/accesibility ui@accesibility.example.com accesibility_id preaccesibility \"C:\\accesibility folder\\accesibility file.xlsx\" [C:\\accesibility folder\\accesibility file.xlsx].");
    }

    [Fact]
    public void PlanKnownCorrections_CoversReleasePackagingInstallerVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "INSTALATION Instaler packge Packging PUBLSH Publishng Artifactt verison MANIFESTT Manfiest certificat Signng Previeww Distributon Releasecandidate. Keep installation, installer, package, packaging, publish, publishing, artifact, version, manifest, certificate, signing, preview, distribution, release candidate, preinstalation, instalation_id, https://instalation.example.com/packging, release@verison.example.com, \"C:\\packge folder\\signng file.zip\", and [C:\\artifactt folder\\releasecandidate file.zip]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "instalation INSTALATION Instalation https://instalation.example.com/instalation release@instalation.example.com instalation_id preinstalation \"C:\\instalation folder\\instalation file.zip\" [C:\\instalation folder\\instalation file.zip].")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "installation");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("INSTALATION", "INSTALLATION"),
            ("Instaler", "Installer"),
            ("packge", "package"),
            ("Packging", "Packaging"),
            ("PUBLSH", "PUBLISH"),
            ("Publishng", "Publishing"),
            ("Artifactt", "Artifact"),
            ("verison", "version"),
            ("MANIFESTT", "MANIFEST"),
            ("Manfiest", "Manifest"),
            ("certificat", "certificate"),
            ("Signng", "Signing"),
            ("Previeww", "Preview"),
            ("Distributon", "Distribution"),
            ("Releasecandidate", "Release candidate"));
        plan.IssueCount.Should().Be(15);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "INSTALLATION Installer package Packaging PUBLISH Publishing Artifact version MANIFEST Manifest certificate Signing Preview Distribution Release candidate. Keep installation, installer, package, packaging, publish, publishing, artifact, version, manifest, certificate, signing, preview, distribution, release candidate, preinstalation, instalation_id, https://instalation.example.com/packging, release@verison.example.com, \"C:\\packge folder\\signng file.zip\", and [C:\\artifactt folder\\releasecandidate file.zip].");
        plan.Edits[0].ReplacementCount.Should().Be(15);
        replaceAllCorrected.Should().Be(
            "installation INSTALLATION Installation https://instalation.example.com/instalation release@instalation.example.com instalation_id preinstalation \"C:\\instalation folder\\instalation file.zip\" [C:\\instalation folder\\instalation file.zip].");
    }

    [Fact]
    public void PlanKnownCorrections_CoversLocalizationGlobalizationResourceVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "LOCALIZATON Globalizaton Internatonalization translaton Langauge cultre resorce Resxfile Fallbackk localee Regionalseting Pseudolocalizaton pluralizaton TIMEZONEE Righttoleft. Keep localization, globalization, internationalization, translation, language, culture, resource, resource file, fallback, locale, regional setting, pseudolocalization, pluralization, time zone, right to left, prelocalizaton, localizaton_id, https://localizaton.example.com/resxfile, i18n@langauge.example.com, \"C:\\cultre folder\\timezonee file.resx\", and [C:\\resorce folder\\righttoleft file.resx]."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);
        var replaceAllIssue = SpellCheckService
            .FindIssuesInCell(
                textAddress,
                "localizaton LOCALIZATON Localizaton https://localizaton.example.com/localizaton i18n@localizaton.example.com localizaton_id prelocalizaton \"C:\\localizaton folder\\localizaton file.resx\" [C:\\localizaton folder\\localizaton file.resx].")
            .First();

        var replaceAllCorrected = SpellCheckService.ApplyCorrectionToAllOccurrences(replaceAllIssue, "localization");

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("LOCALIZATON", "LOCALIZATION"),
            ("Globalizaton", "Globalization"),
            ("Internatonalization", "Internationalization"),
            ("translaton", "translation"),
            ("Langauge", "Language"),
            ("cultre", "culture"),
            ("resorce", "resource"),
            ("Resxfile", "Resource file"),
            ("Fallbackk", "Fallback"),
            ("localee", "locale"),
            ("Regionalseting", "Regional setting"),
            ("Pseudolocalizaton", "Pseudolocalization"),
            ("pluralizaton", "pluralization"),
            ("TIMEZONEE", "TIME ZONE"),
            ("Righttoleft", "Right to left"));
        plan.IssueCount.Should().Be(15);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "LOCALIZATION Globalization Internationalization translation Language culture resource Resource file Fallback locale Regional setting Pseudolocalization pluralization TIME ZONE Right to left. Keep localization, globalization, internationalization, translation, language, culture, resource, resource file, fallback, locale, regional setting, pseudolocalization, pluralization, time zone, right to left, prelocalizaton, localizaton_id, https://localizaton.example.com/resxfile, i18n@langauge.example.com, \"C:\\cultre folder\\timezonee file.resx\", and [C:\\resorce folder\\righttoleft file.resx].");
        plan.Edits[0].ReplacementCount.Should().Be(15);
        replaceAllCorrected.Should().Be(
            "localization LOCALIZATION Localization https://localizaton.example.com/localizaton i18n@localizaton.example.com localizaton_id prelocalizaton \"C:\\localizaton folder\\localizaton file.resx\" [C:\\localizaton folder\\localizaton file.resx].");
    }

    [Fact]
    public void PlanKnownCorrections_CoversDocumentationSupportVocabularyTypos()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(
            textAddress,
            new TextValue(
                "Manul documnt DOCUMENTATON instrction Guidline suport SUPPORTT troubleshot Resoluton KNOWLEGE artcle tickett. Keep manual, document, documentation, instruction, guideline, support, troubleshoot, resolution, knowledge, article, ticket, premanul, documnt_id, https://manul.example.com/documnt, docs@suport.example.com, and \"C:\\documentaton folder\\tickett file.xlsx\"."));

        var issues = SpellCheckService.FindIssues(wb, sheet.Id);
        var plan = SpellCheckService.PlanKnownCorrections(wb, sheet.Id);

        issues.Select(issue => (issue.Word, issue.Suggestion)).Should().Equal(
            ("Manul", "Manual"),
            ("documnt", "document"),
            ("DOCUMENTATON", "DOCUMENTATION"),
            ("instrction", "instruction"),
            ("Guidline", "Guideline"),
            ("suport", "support"),
            ("SUPPORTT", "SUPPORT"),
            ("troubleshot", "troubleshoot"),
            ("Resoluton", "Resolution"),
            ("KNOWLEGE", "KNOWLEDGE"),
            ("artcle", "article"),
            ("tickett", "ticket"));
        plan.IssueCount.Should().Be(12);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(textAddress);
        plan.Edits[0].CorrectedText.Should().Be(
            "Manual document DOCUMENTATION instruction Guideline support SUPPORT troubleshoot Resolution KNOWLEDGE article ticket. Keep manual, document, documentation, instruction, guideline, support, troubleshoot, resolution, knowledge, article, ticket, premanul, documnt_id, https://manul.example.com/documnt, docs@suport.example.com, and \"C:\\documentaton folder\\tickett file.xlsx\".");
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
