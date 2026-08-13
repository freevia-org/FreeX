using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class SpellCheckWorkflowPlannerTests
{
    [Fact]
    public void ScanWorksheet_DetectsObviousMisspellingInWorksheetText()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(address, new TextValue("This report has a mispelled heading."));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("This row is clean."));

        var result = SpellCheckWorkflowPlanner.ScanWorksheet(
            workbook,
            sheet.Id,
            customDictionary: null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<SpellingIssueKey>());

        result.IsComplete.Should().BeFalse();
        result.Issues.Should().ContainSingle().Which.Should().Match<SpellingIssue>(issue =>
            issue.Address == address &&
            issue.Word == "mispelled" &&
            issue.Suggestion == "misspelled" &&
            issue.Source == SpellingIssueSource.CellText);
    }

    [Fact]
    public void ScanWorksheet_DoesNotReportCompleteForCommonUserTestingMisspellings()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(address, new TextValue("Fix this speling erors sentance."));

        var result = SpellCheckWorkflowPlanner.ScanWorksheet(
            workbook,
            sheet.Id,
            customDictionary: null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<SpellingIssueKey>());

        result.IsComplete.Should().BeFalse();
        result.Issues.Select(issue => (issue.Word, issue.Suggestion, issue.Source)).Should().Equal(
            ("speling", "spelling", SpellingIssueSource.CellText),
            ("erors", "errors", SpellingIssueSource.CellText),
            ("sentance", "sentence", SpellingIssueSource.CellText));
        result.Issues.Should().OnlyContain(issue => issue.Address == address);
    }

    [Fact]
    public void ScanWorksheet_ReportsCompleteOnlyWhenNoMisspellingsRemain()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("This worksheet content is clean."));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromFormula("\"mispelled cached text\""));

        var result = SpellCheckWorkflowPlanner.ScanWorksheet(
            workbook,
            sheet.Id,
            customDictionary: null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<SpellingIssueKey>());

        result.IsComplete.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void SpellCheckCommand_CommitsPendingEditsBeforeScanningWorksheet()
    {
        var reviewSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");
        var editingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");

        var commitIndex = reviewSource.IndexOf("TryCommitPendingSpellCheckEdit()", StringComparison.Ordinal);
        var startIndex = reviewSource.IndexOf("controller.Start()", StringComparison.Ordinal);

        commitIndex.Should().BeGreaterThanOrEqualTo(0);
        startIndex.Should().BeGreaterThan(commitIndex);
        reviewSource.Should().Contain("if (!TryCommitPendingSpellCheckEdit())");
        DialogSourceTestSupport.ReadAppServicesSource("SpellCheckSessionController.cs")
            .Should().Contain("SpellCheckWorkflowPlanner.ScanWorksheet(");
        editingSource.Should().Contain("private bool TryCommitPendingSpellCheckEdit()");
        editingSource.Should().Contain("FormulaBar.Text = _inlineEditor.Text;");
        editingSource.Should().Contain("return CommitEdit();");
    }
}
