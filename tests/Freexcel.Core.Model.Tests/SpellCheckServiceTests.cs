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
