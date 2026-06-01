using System.Globalization;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxFilterMaterializerDateTests
{
    [Fact]
    public void WorksheetAutoFilter_MaterializesOutOfRangeDateSerialAsText()
    {
        var workbook = new Workbook("OutOfRangeAutoFilter");
        var sheet = workbook.AddSheet("Sheet1");
        var serialText = double.MaxValue.ToString("R", CultureInfo.InvariantCulture);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new DateTimeValue(double.MaxValue));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:A3", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, [serialText]));

        var act = () => XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        act.Should().NotThrow();
        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
    }

    [Fact]
    public void StructuredTableFilter_MaterializesOutOfRangeDateSerialAsText()
    {
        var workbook = new Workbook("OutOfRangeTableFilter");
        var sheet = workbook.AddSheet("Sheet1");
        var serialText = double.MaxValue.ToString("R", CultureInfo.InvariantCulture);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new DateTimeValue(double.MaxValue));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            HasAutoFilter = true,
            PackagePart = "/xl/tables/table1.xml"
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Date"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, [serialText]));

        var act = () => XlsxStructuredTableModelMapper.MaterializeFilters(sheet, table);

        act.Should().NotThrow();
        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
    }
}
