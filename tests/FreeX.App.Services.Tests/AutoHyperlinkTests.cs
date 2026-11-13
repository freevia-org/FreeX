using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Excel's AutoCorrect "Internet and network paths with hyperlinks": typing a bare web address,
/// e-mail address or network path makes the cell a hyperlink, as its own undo step.
/// </summary>
public sealed class AutoHyperlinkTests
{
    [Theory]
    [InlineData("https://example.com/a?b=1", "https://example.com/a?b=1")]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("www.example.com", "http://www.example.com")]
    [InlineData("someone@example.com", "mailto:someone@example.com")]
    [InlineData(@"\\server\share", @"\\server\share")]
    public void Planner_RecognizesBareLinks(string typed, string target)
    {
        AutoHyperlinkPlanner.TryGetTarget(typed).Should().Be(target);
    }

    [Theory]
    [InlineData("visit www.example.com")]
    [InlineData("example.com")]
    [InlineData("=HYPERLINK(\"https://x.com\")")]
    [InlineData("'https://example.com")]
    [InlineData("hello")]
    [InlineData("a@b")]
    [InlineData("")]
    public void Planner_LeavesOtherEntriesAlone(string typed)
    {
        AutoHyperlinkPlanner.TryGetTarget(typed).Should().BeNull();
    }

    [Fact]
    public void CommitCellText_TypedUrlBecomesAHyperlink_AndFirstUndoRemovesOnlyTheLink()
    {
        var (session, sheet, address) = CreateSessionAtA1();

        session.CommitCellText("www.example.com").Success.Should().BeTrue();

        sheet.Hyperlinks.Should().ContainKey(address).WhoseValue.Should().Be("http://www.example.com");
        sheet.GetValue(address).Should().Be(new TextValue("www.example.com"));

        session.UndoLastEdit();
        sheet.Hyperlinks.Should().NotContainKey(address, "the first undo reverses the automatic hyperlink, as in Excel");
        sheet.GetValue(address).Should().Be(new TextValue("www.example.com"), "the typed text stays");

        session.UndoLastEdit();
        sheet.GetCell(address).Should().BeNull("the second undo removes the typing");
    }

    [Fact]
    public void CommitCellText_PlainTextDoesNotCreateAHyperlink()
    {
        var (session, sheet, address) = CreateSessionAtA1();

        session.CommitCellText("hello world").Success.Should().BeTrue();

        sheet.Hyperlinks.Should().NotContainKey(address);
    }

    private static (WorkbookSession Session, Sheet Sheet, CellAddress Address) CreateSessionAtA1()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var sheet = workbook.Sheets.Single();
        var address = new CellAddress(sheet.Id, 1, 1);

        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        session.SelectCell(address);

        return (session, sheet, address);
    }
}
