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
