using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ReviewWorkflowPlannerTests
{
    [Fact]
    public void CreatePlan_CollectsReviewReadinessDataForActiveSheet()
    {
        var workbook = WorkbookFactory.Create();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.Name = "Review Sheet";
        sheet.SetCell(a1, new TextValue("teh report"));
        sheet.Comments[b2] = "recieve note";
        sheet.ThreadedComments[c3] = new ThreadedComment("adn root", "Codex")
        {
            Replies = [new CommentReply("summary")]
        };

        var plan = ReviewWorkflowPlanner.CreatePlan(workbook, sheet.Id);

        plan.Statistics.WorksheetCount.Should().Be(1);
        plan.Statistics.CommentCount.Should().Be(2);
        plan.SpellingIssues.Select(issue => issue.Word)
            .Should().Contain(["teh", "recieve", "adn"]);
        plan.Notes.Should().ContainSingle(note =>
            note.Kind == ReviewCommentKind.Note &&
            note.Address == b2 &&
            note.PreviewText == "recieve note");
        plan.ThreadedComments.Should().ContainSingle(thread =>
            thread.Kind == ReviewCommentKind.ThreadedComment &&
            thread.Address == c3 &&
            thread.PreviewText.Contains("Codex: adn root"));
    }

    [Fact]
    public void CreatePlan_AppliesIgnoredSpellingWordsAndIssueKeys()
    {
        var workbook = WorkbookFactory.Create();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("teh"));
        sheet.SetCell(b1, new TextValue("recieve"));
        var ignoredIssue = SpellCheckService.FindIssues(workbook, sheet.Id)
            .Single(issue => issue.Word == "recieve");

        var plan = ReviewWorkflowPlanner.CreatePlan(
            workbook,
            sheet.Id,
            ignoredWords: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TEH" },
            ignoredIssues: new HashSet<SpellingIssueKey>
            {
                SpellCheckWorkflowPlanner.CreateIssueKey(ignoredIssue)
            });

        plan.SpellingIssues.Should().BeEmpty();
    }

    [Fact]
    public void FindNextNote_WrapsThroughOrderedNoteAddresses()
    {
        var workbook = WorkbookFactory.Create();
        var sheet = workbook.Sheets.Single();
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var d4 = new CellAddress(sheet.Id, 4, 4);
        sheet.Comments[d4] = "later";
        sheet.Comments[b2] = "earlier";

        ReviewWorkflowPlanner.FindNextNote(sheet, b2, previous: false).Target.Should().Be(d4);
        ReviewWorkflowPlanner.FindNextNote(sheet, d4, previous: false).Target.Should().Be(b2);
        ReviewWorkflowPlanner.FindNextNote(sheet, b2, previous: true).Target.Should().Be(d4);
    }

    [Fact]
    public void GetAccessibilityNavigationTarget_UsesFirstCellInRangeOrFallsBackToSheetStart()
    {
        var sheetId = SheetId.New();
        var issue = new AccessibilityIssue(
            AccessibilityIssueKind.MergedCells,
            sheetId,
            "Sheet",
            "B2:C3",
            "Merged cells can make worksheet navigation harder.");
        var sheetOnlyIssue = issue with { Location = "Sheet" };

        ReviewWorkflowPlanner.GetAccessibilityNavigationTarget(issue)
            .Should().Be(new CellAddress(sheetId, 2, 2));
        ReviewWorkflowPlanner.GetAccessibilityNavigationTarget(sheetOnlyIssue)
            .Should().Be(new CellAddress(sheetId, 1, 1));
    }

    [Fact]
    public void WorkbookSession_ExecutesPlannedSpellingReplacementAndNavigatesReviewTargets()
    {
        var workbook = WorkbookFactory.Create();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(a1, new TextValue("teh"));
        sheet.Comments[c3] = "note";
        var session = CreateSession(workbook);
        var issue = session.GetReviewWorkflowPlan().SpellingIssues.Single(issue => issue.Word == "teh");
        var command = SpellCheckWorkflowPlanner.BuildReplacementCommand(issue, "the");

        var editResult = session.ExecuteReviewCommand(command, issue.Address);
        var navigationResult = session.GoToNextNote();

        editResult.Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("the"));
        session.IsDirty.Should().BeTrue();
        navigationResult.Success.Should().BeTrue();
        navigationResult.SelectedRange.Should().Be(new GridRange(c3, c3));
        session.ActiveCell.Should().Be(c3);
    }

    [Fact]
    public void CreateDisplayModel_FormatsSummaryAndBoundedRendererNeutralPreviews()
    {
        var workbook = WorkbookFactory.Create();
        var sheet = workbook.Sheets.Single();
        for (uint row = 1; row <= 7; row++)
        {
            var address = new CellAddress(sheet.Id, row, 1);
            sheet.SetCell(address, new TextValue("teh"));
            sheet.Comments[address] = row == 1
                ? "line one\nline two"
                : $"note {row}";
        }

        var display = ReviewWorkflowPlanner.CreateDisplayModel(
            ReviewWorkflowPlanner.CreatePlan(workbook, sheet.Id));

        display.Summary.Should().Contain("Sheets: 1");
        display.Summary.Should().Contain("Spelling issues: 7");
        display.SpellingIssues.Should().HaveCount(7);
        display.SpellingIssues[0].Should().Be("A1: teh -> the (cell text)");
        display.SpellingIssues[^1].Should().Be("... and 1 more");
        display.Notes[0].Should().Be("A1: line one line two");
        display.Notes[^1].Should().Be("... and 1 more");
        display.ThreadedComments.Should().Equal("No threaded comments on the active sheet.");
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(
                workbook,
                workbook.Name,
                "Opened workbook.",
                IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
}
