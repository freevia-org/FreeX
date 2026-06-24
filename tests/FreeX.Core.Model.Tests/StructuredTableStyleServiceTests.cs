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

    // ── Column-stripe tests ─────────────────────────────────────────────────

    /// <summary>
    /// When ShowColumnStripes=true (and ShowRowStripes=false) the load-time materializer must paint
    /// vertical column bands, mirroring StructuredTableCommand.BuildStyleCommands.  Column 1 (offset 0)
    /// gets the even fill, column 2 (offset 1) gets the odd fill.
    /// </summary>
    [Fact]
    public void ApplyLoadedTableStyles_PaintsColumnStripesWhenShowColumnStripesTrue()
    {
        var workbook = new Workbook("ColumnStripes");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 3);

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = false,
            ShowColumnStripes = true
        };
        sheet.StructuredTables.Add(table);

        // Seed a third column of data
        for (var r = 1u; r <= 4u; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 3), new NumberValue(r));

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);

        // Data rows: col 1 (offset 0) → even fill; col 2 (offset 1) → odd fill; col 3 (offset 2) → even fill
        for (var row = 2u; row <= 4u; row++)
        {
            StyleAt(workbook, sheet, row, 1).FillColor.Should().Be(banding.EvenRowFill, $"col 1 row {row} should be even (offset 0)");
            StyleAt(workbook, sheet, row, 2).FillColor.Should().Be(banding.OddRowFill,  $"col 2 row {row} should be odd (offset 1)");
            StyleAt(workbook, sheet, row, 3).FillColor.Should().Be(banding.EvenRowFill, $"col 3 row {row} should be even (offset 2)");
        }
    }

    // ── First/last column bold tests ────────────────────────────────────────

    [Fact]
    public void ApplyLoadedTableStyles_BoldsFirstAndLastColumnWhenFlagsSet()
    {
        var workbook = new Workbook("FirstLastCol");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 3);

        // Seed a third column
        for (var r = 1u; r <= 4u; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 3), new NumberValue(r));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true,
            ShowFirstColumn = true,
            ShowLastColumn = true
        };
        sheet.StructuredTables.Add(table);

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        // Every row of the first column must be bold.
        for (var row = 1u; row <= 4u; row++)
            StyleAt(workbook, sheet, row, 1).Bold.Should().BeTrue($"first column row {row} must be bold");

        // Every row of the last column must be bold.
        for (var row = 1u; row <= 4u; row++)
            StyleAt(workbook, sheet, row, 3).Bold.Should().BeTrue($"last column row {row} must be bold");

        // The middle column must NOT have bold added by first/last emphasis.
        StyleAt(workbook, sheet, 2, 2).Bold.Should().NotBe(true, "middle column must not be bolded by first/last emphasis");
    }

    // ── Totals-row regression: must not use header fill ─────────────────────

    [Fact]
    public void ApplyLoadedTableStyles_TotalsRowGetsBodyFillNotHeaderFill()
    {
        var workbook = new Workbook("TotalsRow");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 3);

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            HeaderRowCount = 1,
            TotalsRowShown = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        sheet.StructuredTables.Add(table);

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);

        // The totals row (row 5) must NOT carry the header fill.
        StyleAt(workbook, sheet, 5, 1).FillColor.Should().NotBe(banding.HeaderFill,
            "totals row must not be painted with the header fill");

        // The header row (row 1) must still have the header fill.
        StyleAt(workbook, sheet, 1, 1).FillColor.Should().Be(banding.HeaderFill,
            "header row must still carry the header fill");
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
