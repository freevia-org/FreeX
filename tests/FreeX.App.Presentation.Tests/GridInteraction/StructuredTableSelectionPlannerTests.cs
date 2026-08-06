using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class StructuredTableSelectionPlannerTests
{
    [Fact]
    public void WholeColumnSelection_EscalatesFromBodyToTableToWorksheet()
    {
        var (workbook, sheet, table) = CreateTable();
        var selectedCell = Range(sheet.Id, 5, 2, 5, 2);

        var body = StructuredTableSelectionPlanner.PlanWholeColumns(sheet, selectedCell);
        var all = StructuredTableSelectionPlanner.PlanWholeColumns(sheet, body.Range);
        var worksheet = StructuredTableSelectionPlanner.PlanWholeColumns(sheet, all.Range);

        body.Kind.Should().Be(StructuredTableSelectionExpansionKind.TableColumnData);
        body.Range.Should().Be(Range(sheet.Id, 2, 2, 9, 2));
        all.Kind.Should().Be(StructuredTableSelectionExpansionKind.TableColumnAll);
        all.Range.Should().Be(Range(sheet.Id, 1, 2, 10, 2));
        worksheet.Kind.Should().Be(StructuredTableSelectionExpansionKind.WorksheetColumns);
        worksheet.Range.Start.Row.Should().Be(1);
        worksheet.Range.End.Row.Should().Be(CellAddress.MaxRow);
        workbook.Sheets.Should().ContainSingle();
        table.Id.Should().Be(body.TableId);
    }

    [Fact]
    public void WholeRowSelection_EscalatesFromTableRowToWorksheetRow()
    {
        var (_, sheet, _) = CreateTable();
        var selectedCell = Range(sheet.Id, 5, 2, 5, 2);

        var tableRow = StructuredTableSelectionPlanner.PlanWholeRows(sheet, selectedCell);
        var worksheetRow = StructuredTableSelectionPlanner.PlanWholeRows(sheet, tableRow.Range);

        tableRow.Kind.Should().Be(StructuredTableSelectionExpansionKind.TableRows);
        tableRow.Range.Should().Be(Range(sheet.Id, 5, 1, 5, 3));
        worksheetRow.Kind.Should().Be(StructuredTableSelectionExpansionKind.WorksheetRows);
        worksheetRow.Range.Start.Col.Should().Be(1);
        worksheetRow.Range.End.Col.Should().Be(CellAddress.MaxCol);
    }

    [Fact]
    public void Describe_DecomposesHeaderBodyAndTotalsSemantics()
    {
        var (_, sheet, table) = CreateTable();

        var context = StructuredTableSelectionPlanner.Describe(sheet, table.Range);

        context.Should().NotBeNull();
        context!.RegionKind.Should().Be(StructuredTableSelectionRegionKind.FullTable);
        context.HeaderRange.Should().Be(Range(sheet.Id, 1, 1, 1, 3));
        context.DataBodyRange.Should().Be(Range(sheet.Id, 2, 1, 9, 3));
        context.TotalsRange.Should().Be(Range(sheet.Id, 10, 1, 10, 3));
        context.IncludesHeader.Should().BeTrue();
        context.IncludesDataBody.Should().BeTrue();
        context.IncludesTotals.Should().BeTrue();
    }

    [Fact]
    public void TableNameResolution_UsesNameOrDisplayNameAndReturnsDataBody()
    {
        var (workbook, sheet, table) = CreateTable();

        StructuredTableSelectionPlanner.TryResolveDataBodyRange(workbook, "sales", out var range)
            .Should().BeTrue();
        range.Should().Be(Range(sheet.Id, 2, 1, 9, 3));
        StructuredTableSelectionPlanner.ContainsTableName(workbook, "INTERNALSALES")
            .Should().BeTrue();
    }

    [Fact]
    public void SelectionOutsideTable_ExpandsDirectlyToWorksheetAxis()
    {
        var (_, sheet, _) = CreateTable();
        var outside = Range(sheet.Id, 20, 5, 21, 6);

        StructuredTableSelectionPlanner.PlanWholeColumns(sheet, outside).Kind
            .Should().Be(StructuredTableSelectionExpansionKind.WorksheetColumns);
        StructuredTableSelectionPlanner.PlanWholeRows(sheet, outside).Kind
            .Should().Be(StructuredTableSelectionExpansionKind.WorksheetRows);
        StructuredTableSelectionPlanner.Describe(sheet, outside).Should().BeNull();
        StructuredTableSelectionPlanner.OverlapsAnyTable(sheet, outside).Should().BeFalse();
    }

    [Fact]
    public void OverlapsAnyTable_IncludesPartialOverlapWithoutTreatingItAsContainedSelection()
    {
        var (_, sheet, table) = CreateTable();
        var selection = Range(sheet.Id, 5, 2, 12, 4);

        StructuredTableSelectionPlanner.OverlapsAnyTable(sheet, selection).Should().BeTrue();
        StructuredTableSelectionPlanner.FindOverlappingTableRange(sheet, selection).Should().Be(table.Range);
        StructuredTableSelectionPlanner.Describe(sheet, selection).Should().BeNull();
        table.Range.Contains(selection).Should().BeFalse();
    }

    private static (Workbook Workbook, Sheet Sheet, StructuredTableModel Table) CreateTable()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "InternalSales",
            DisplayName = "Sales",
            Range = Range(sheet.Id, 1, 1, 10, 3),
            HeaderRowCount = 1,
            TotalsRowShown = true,
            TotalsRowCount = 1
        };
        sheet.StructuredTables.Add(table);
        return (workbook, sheet, table);
    }

    private static GridRange Range(
        SheetId sheetId,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
}
