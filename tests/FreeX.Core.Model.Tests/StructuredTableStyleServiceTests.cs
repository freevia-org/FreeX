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

    // F1 (style-precedence, round 146): Excel's precedence is direct formatting > table style, for
    // header/totals cells exactly the same as body cells. A header cell that carries an explicit
    // direct fill (the user manually recolored it via Home > Fill Color on top of a built-in table
    // style) must keep that fill after ApplyLoadedTableStyles, not have it overwritten by the
    // generic table-style banding color.
    [Fact]
    public void ApplyLoadedTableStyles_PreservesAnExplicitUserFillOnAHeaderCell()
    {
        var workbook = new Workbook("LoadedTableStyleExplicitHeaderFill");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 2);

        // The user manually recolored the header cell (Excel "Green", RGB 0,176,80) on top of the
        // TableStyleMedium2 table style, whose own header fill is a different color entirely.
        var userHeaderFill = new CellColor(0, 176, 80);
        var explicitStyle = workbook.RegisterStyle(new CellStyle { FillColor = userHeaderFill });
        sheet.GetCell(1, 1)!.StyleId = explicitStyle;

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

        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);
        banding.HeaderFill.Should().NotBe(userHeaderFill, "the test must exercise a genuine style-vs-direct-format conflict");

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook);

        // The user's explicit header fill must survive materialization, just like Excel: direct
        // formatting wins over the computed table-style banding, for header cells same as body cells.
        StyleAt(workbook, sheet, 1, 1).FillColor.Should().Be(userHeaderFill);

        // The untouched header cell in the same row must still get the generic banding fill — this is
        // not a case of the whole header row being skipped, only the explicitly-formatted cell.
        StyleAt(workbook, sheet, 1, 2).FillColor.Should().Be(banding.HeaderFill);
    }

    // Sibling no-regression case: a header cell that carries NO direct formatting (the common case —
    // a table freshly created in FreeX, or loaded from a file whose header was never manually
    // recolored) must still receive the full table-style banding: fill, bold, and header font color.
    [Fact]
    public void ApplyLoadedTableStyles_UnformattedHeaderCellStillGetsFullBandingStyle()
    {
        var workbook = new Workbook("LoadedTableStyleUnformattedHeader");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 2);

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

        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook);

        var header = StyleAt(workbook, sheet, 1, 1);
        header.FillColor.Should().Be(banding.HeaderFill);
        header.FontColor.Should().Be(banding.HeaderFontColor);
        header.Bold.Should().BeTrue();
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

    // ── Border tests ────────────────────────────────────────────────────────

    /// <summary>
    /// TableStyleMedium2 (and other medium-family styles) include interior thin borders on every cell
    /// in the data body, a bottom border on the header row, and a top border on the totals row.  This
    /// mirrors Excel's built-in table border rendering and the Wave B borders requirement.
    /// </summary>
    [Fact]
    public void ApplyLoadedTableStyles_Medium2_AppliesBodyBordersHeaderBottomAndTotalsTop()
    {
        var workbook = new Workbook("Borders");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 3);
        // Row 5 is the totals row.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(60));

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
        banding.Border.Should().NotBeNull("TableStyleMedium2 is a medium-family style and must supply a border color");

        // Header row: bottom border is set (separator between header and data body).
        var headerStyle = StyleAt(workbook, sheet, 1, 1);
        headerStyle.BorderBottom.Style.Should().Be(BorderStyle.Thin, "header must have a thin bottom border");
        headerStyle.BorderBottom.Color.Should().Be(banding.Border!.Value, "header bottom border color must match resolved border color");
        headerStyle.BorderTop.Style.Should().Be(BorderStyle.None, "header must NOT have a top border");

        // Data body cells: all four sides are bordered.
        var bodyStyle = StyleAt(workbook, sheet, 2, 1);
        bodyStyle.BorderTop.Style.Should().Be(BorderStyle.Thin, "body cell must have a thin top border");
        bodyStyle.BorderBottom.Style.Should().Be(BorderStyle.Thin, "body cell must have a thin bottom border");
        bodyStyle.BorderLeft.Style.Should().Be(BorderStyle.Thin, "body cell must have a thin left border");
        bodyStyle.BorderRight.Style.Should().Be(BorderStyle.Thin, "body cell must have a thin right border");
        bodyStyle.BorderTop.Color.Should().Be(banding.Border!.Value, "body border color must match resolved border color");

        // Totals row: top border only (separator above totals, distinct from header bottom).
        var totalsStyle = StyleAt(workbook, sheet, 5, 1);
        totalsStyle.BorderTop.Style.Should().Be(BorderStyle.Thin, "totals row must have a thin top border");
        totalsStyle.BorderTop.Color.Should().Be(banding.Border!.Value, "totals top border color must match resolved border color");
        totalsStyle.BorderBottom.Style.Should().Be(BorderStyle.None, "totals row must NOT have a bottom border");
    }

    /// <summary>
    /// Light-family table styles (e.g. TableStyleLight1) do not draw interior borders in Excel.
    /// The resolver returns Border=null for these styles, and no borders should be applied.
    /// </summary>
    [Fact]
    public void ApplyLoadedTableStyles_Light1_DoesNotApplyBorders()
    {
        var workbook = new Workbook("NoBorders");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 2);

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 1,
            StyleName = "TableStyleLight1",
            ShowRowStripes = true
        };
        sheet.StructuredTables.Add(table);

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleLight1", workbook.Theme);
        banding.Border.Should().BeNull("TableStyleLight1 is a light-family style with no interior borders");

        // No borders should be applied on any cell.
        var headerStyle = StyleAt(workbook, sheet, 1, 1);
        headerStyle.BorderBottom.Style.Should().Be(BorderStyle.None, "light style must have no header bottom border");
        var bodyStyle = StyleAt(workbook, sheet, 2, 1);
        bodyStyle.BorderTop.Style.Should().Be(BorderStyle.None, "light style must have no body border");
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
