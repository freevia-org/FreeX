using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

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
}
