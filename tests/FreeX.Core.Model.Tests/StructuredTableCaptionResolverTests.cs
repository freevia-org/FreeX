using System.Globalization;

using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class StructuredTableCaptionResolverTests
{
    [Fact]
    public void TryResolveColumnCaptions_DefaultHeaderAndShownTotals_ProjectsOnlyDataBody()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var (workbook, sheet, table) = CreateTable(
                startRow: 1,
                endRow: 8,
                headerRowCount: null,
                totalsRowShown: true,
                totalsRowCount: 2);

            Set(sheet, 1, new TextValue("Header"));
            Set(sheet, 2, new TextValue("\u0130stanbul"));
            Set(sheet, 3, new TextValue("istanbul"));
            Set(sheet, 4, new NumberValue(1234.5));
            Set(sheet, 5, new BoolValue(true));
            var date = new DateTime(2026, 8, 23, 14, 30, 0);
            Set(sheet, 6, DateTimeValue.FromDateTime(date));
            Set(sheet, 7, new TextValue("First total"));
            Set(sheet, 8, new TextValue("Second total"));

            var found = StructuredTableCaptionResolver.TryResolveColumnCaptions(
                workbook,
                table.Id,
                table.Columns[0].Id,
                out var captions);

            found.Should().BeTrue();
            captions.Should().Equal(
                "\u0130stanbul",
                1234.5.ToString(CultureInfo.CurrentCulture),
                "TRUE",
                date.ToString(CultureInfo.CurrentCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void TryResolveColumnCaptions_HeaderlessTable_IncludesFirstRangeRow()
    {
        var (workbook, sheet, table) = CreateTable(
            startRow: 3,
            endRow: 5,
            headerRowCount: 0,
            totalsRowShown: false,
            totalsRowCount: null);
        Set(sheet, 3, new TextValue("First data row"));
        Set(sheet, 4, BlankValue.Instance);
        Set(sheet, 5, new ErrorValue("#VALUE!"));

        var found = StructuredTableCaptionResolver.TryResolveColumnCaptions(
            workbook,
            table.Id,
            table.Columns[0].Id,
            out var captions);

        found.Should().BeTrue();
        captions.Should().Equal("First data row");
    }

    [Fact]
    public void TryResolveColumnCaptions_StructuralRowsConsumeRange_ReturnsEmptyWithoutLeakingTotals()
    {
        var (workbook, sheet, table) = CreateTable(
            startRow: 1,
            endRow: 3,
            headerRowCount: 1,
            totalsRowShown: true,
            totalsRowCount: 2);
        Set(sheet, 1, new TextValue("Header"));
        Set(sheet, 2, new TextValue("Total one"));
        Set(sheet, 3, new TextValue("Total two"));

        var found = StructuredTableCaptionResolver.TryResolveColumnCaptions(
            workbook,
            table.Id,
            table.Columns[0].Id,
            out var captions);

        found.Should().BeTrue();
        captions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(99, 11)]
    [InlineData(9, 99)]
    public void TryResolveColumnCaptions_MissingTableOrColumn_ReturnsFalse(int tableId, int columnId)
    {
        var (workbook, _, _) = CreateTable(
            startRow: 1,
            endRow: 2,
            headerRowCount: 1,
            totalsRowShown: false,
            totalsRowCount: null);

        var found = StructuredTableCaptionResolver.TryResolveColumnCaptions(
            workbook,
            tableId,
            columnId,
            out var captions);

        found.Should().BeFalse();
        captions.Should().BeEmpty();
    }

    private static (Workbook Workbook, Sheet Sheet, StructuredTableModel Table) CreateTable(
        uint startRow,
        uint endRow,
        int? headerRowCount,
        bool totalsRowShown,
        int? totalsRowCount)
    {
        var workbook = new Workbook("Caption resolver tests");
        var sheet = workbook.AddSheet("Data");
        var table = new StructuredTableModel
        {
            Id = 9,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(
                new CellAddress(sheet.Id, startRow, 2),
                new CellAddress(sheet.Id, endRow, 2)),
            HeaderRowCount = headerRowCount,
            TotalsRowShown = totalsRowShown,
            TotalsRowCount = totalsRowCount,
            Columns = { new StructuredTableColumnModel(11, "Category") }
        };
        sheet.StructuredTables.Add(table);
        return (workbook, sheet, table);
    }

    private static void Set(Sheet sheet, uint row, ScalarValue value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), value);
}
