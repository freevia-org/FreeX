using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class StructuredTableStyleServiceTests
{
    // A structured table loaded from xlsx arrives with data cells but no banding (Excel applies
    // TableStyleMediumN dynamically rather than baking it into per-cell styles).
    // ApplyLoadedTableStyles must paint the header fill + alternating row stripes onto the cells.
    [Fact]
    public void ApplyLoadedTableStyles_PaintsHeaderFillAndAlternatingRowStripes()
    {
        var workbook = new Workbook("LoadedTableStyle");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 4);

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        sheet.StructuredTables.Add(table);

        // Loaded tables start unstyled (default style) like a freshly-parsed xlsx.
        AnyHeaderFill(workbook, sheet).Should().BeFalse("the loaded table starts unstyled");

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);

        // Header row gets the header fill + bold header font.
        var header = StyleAt(workbook, sheet, 1, 1);
        header.FillColor.Should().Be(banding.HeaderFill);
        header.FontColor.Should().Be(banding.HeaderFontColor);
        header.Bold.Should().BeTrue();

        // Data rows alternate: first data row is the "even" (unfilled) stripe, second is the "odd"
        // (tinted) stripe, mirroring Excel and the table-creation command's parity.
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(banding.EvenRowFill);
        StyleAt(workbook, sheet, 3, 1).FillColor.Should().Be(banding.OddRowFill);
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(banding.EvenRowFill);
        StyleAt(workbook, sheet, 5, 2).FillColor.Should().Be(banding.OddRowFill);
    }

    [Fact]
    public void ApplyLoadedTableStyles_PreservesAnExplicitUserFillOnABodyCell()
    {
        var workbook = new Workbook("LoadedTableStyleExplicitFill");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 2);

        var userFill = new CellColor(255, 0, 0);
        var explicitStyle = workbook.RegisterStyle(new CellStyle { FillColor = userFill });
        sheet.GetCell(2, 1)!.StyleId = explicitStyle;

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        sheet.StructuredTables.Add(table);

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        // The user's explicit body fill must survive the dynamic banding, just like Excel.
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(userFill);
    }

    [Fact]
    public void ApplyLoadedTableStyles_NoTables_ReturnsFalse()
    {
        var workbook = new Workbook("NoTables");
        workbook.AddSheet("Sheet1");

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeFalse();
    }

    private static bool AnyHeaderFill(Workbook workbook, Sheet sheet)
    {
        for (var col = 1u; col <= 2u; col++)
        {
            if (sheet.GetCell(1, col) is { } cell && workbook.GetStyle(cell.StyleId).FillColor is not null)
                return true;
        }

        return false;
    }

    private static void SeedTable(Sheet sheet, int rowCount)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        for (var r = 2; r <= rowCount + 1; r++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, 1), new TextValue($"Row{r}"));
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, 2), new NumberValue(r * 10));
        }
    }

    private static CellStyle StyleAt(Workbook workbook, Sheet sheet, uint row, uint col) =>
        workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, row, col))!.StyleId);
}
