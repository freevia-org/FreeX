using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

public sealed class StructuredReferenceEditorResolverTests
{
    [Fact]
    public void ResolveEditorReference_TrimsAndResolvesOrdinarySelector()
    {
        var (workbook, sheet) = CreateWorkbook();

        var range = StructuredReferenceResolver.ResolveEditorReference(
            workbook,
            sheet,
            new CellAddress(sheet.Id, 2, 1),
            " Sales ",
            " Amount ");

        range.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 2)));
    }

    [Fact]
    public void ResolveEditorReference_CurrentRowSelectorReturnsSingleCell()
    {
        var (workbook, sheet) = CreateWorkbook();

        var range = StructuredReferenceResolver.ResolveEditorReference(
            workbook,
            sheet,
            new CellAddress(sheet.Id, 2, 1),
            "",
            "  @ Amount  ");

        var expected = new CellAddress(sheet.Id, 2, 2);
        range.Should().Be(new GridRange(expected, expected));
    }

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook()
    {
        var workbook = new Workbook("EditorStructuredReferenceTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2))
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        sheet.StructuredTables.Add(table);
        return (workbook, sheet);
    }
}
