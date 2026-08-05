using System.Linq;

using FluentAssertions;

using FreeX.App.Avalonia;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.App.Presentation.TableUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the non-UI glue backing the Avalonia "Insert Table" menu command: reading a portable
/// <see cref="QuickAnalysisSelectionDescription"/> from live sheet cells (column-kind and header
/// detection) and mapping a selection onto the Core <see cref="CreateStructuredTableCommand"/>. No running
/// UI is required.
/// </summary>
public sealed class InsertTableShellGlueTests
{
    private static Sheet CreateSheet() => new Workbook("Book").AddSheet("Sheet1");

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));

    // Selection reader: column-kind detection

    [Fact]
    public void Describe_ClassifiesColumnsByContent()
    {
        var sheet = CreateSheet();
        // Col 1 numeric, col 2 text, col 3 dates, col 4 blank.
        for (uint row = 1; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue($"name{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), DateTimeValue.FromDateTime(new DateTime(2026, 1, (int)row)));
        }

        var description = QuickAnalysisSelectionReader.Describe(sheet, Range(sheet, 1, 1, 3, 4));

        description.ColumnKinds.Should().Equal(
            QuickAnalysisColumnKind.Numeric,
            QuickAnalysisColumnKind.Text,
            QuickAnalysisColumnKind.Date,
            QuickAnalysisColumnKind.Empty);
    }

    // Selection reader: header detection

    [Fact]
    public void Describe_DetectsHeaderRow_WhenFirstRowAllTextOverNumericData()
    {
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        var description = QuickAnalysisSelectionReader.Describe(sheet, Range(sheet, 1, 1, 4, 2));

        description.HasHeaderRow.Should().BeTrue();
        // Header row excluded: col 1 stays text, col 2 numeric.
        description.ColumnKinds.Should().Equal(QuickAnalysisColumnKind.Text, QuickAnalysisColumnKind.Numeric);
        description.DataRowCount.Should().Be(3u);
    }

    [Fact]
    public void Describe_NoHeaderRow_WhenAllColumnsAreText()
    {
        var sheet = CreateSheet();
        for (uint row = 1; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"a{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue($"b{row}"));
        }

        var description = QuickAnalysisSelectionReader.Describe(sheet, Range(sheet, 1, 1, 3, 2));

        description.HasHeaderRow.Should().BeFalse();
    }

    // Shared table planner: selection + header -> CreateStructuredTableCommand

    [Fact]
    public void InsertTablePlanner_BuildsCommand_OverSelection_WithDetectedHeaders_AndCreatesTableOnApply()
    {
        var workbook = new Workbook("Tables");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        var range = Range(sheet, 1, 1, 4, 2);
        var hasHeaderRow = QuickAnalysisSelectionReader.Describe(sheet, range).HasHeaderRow;
        hasHeaderRow.Should().BeTrue();

        var command = TableCreationPlanner.BuildInsertCommand(sheet.Id, range, hasHeaderRow);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.StructuredTables.Should().ContainSingle();
        var table = sheet.StructuredTables[0];
        table.Range.Should().Be(range);
        // First-row headers feed the column names from the selection's first row.
        table.Columns.Select(c => c.Name).Should().Equal("Region", "Sales");
    }

    [Fact]
    public void InsertTablePlanner_WithoutHeaders_GeneratesColumnNames()
    {
        var workbook = new Workbook("Tables");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 2));
        }

        var range = Range(sheet, 1, 1, 3, 2);
        var command = TableCreationPlanner.BuildInsertCommand(sheet.Id, range, firstRowHasHeaders: false);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.StructuredTables[0].Columns.Select(c => c.Name).Should().Equal("Column1", "Column2");
    }
}
