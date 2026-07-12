using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed partial class AccessibilityCheckerServiceTests
{
    [Fact]
    public void FindIssues_FlagsStructuredTableWithFullyBlankInteriorRow()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Units"));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(10));

        // Row 4 (the last data-body row) is entirely blank across all 4 table columns.

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(20));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4)),
            HeaderRowCount = 1,
            HasAutoFilter = true,
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.BlankRowOrColumnInTable).Subject;

        issue.SheetId.Should().Be(sheet.Id);
        issue.SheetName.Should().Be("Sales");
        issue.Location.Should().Be("A4:D4");
        issue.Message.Should().Be("Tables should not contain fully blank rows.");
    }

    [Fact]
    public void FindIssues_FlagsStructuredTableWithFullyBlankInteriorColumn()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Notes"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Sales"));

        // Column B ("Notes") has header text but every data-body cell beneath it is blank.

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(100));

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(200));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            HeaderRowCount = 1,
            HasAutoFilter = true,
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.BlankRowOrColumnInTable).Subject;

        issue.SheetId.Should().Be(sheet.Id);
        issue.SheetName.Should().Be("Sales");
        issue.Location.Should().Be("B2:B3");
        issue.Message.Should().Be("Tables should not contain fully blank columns.");
    }

    [Fact]
    public void FindIssues_DoesNotFlagFullyPopulatedStructuredTableForBlankRowsOrColumns()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Sales"));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(100));

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(200));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            HeaderRowCount = 1,
            HasAutoFilter = true,
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.BlankRowOrColumnInTable);
    }
}
