using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression tests for two round-46 findings in the load-time table-banding materializer
/// (<see cref="StructuredTableStyleService"/>):
///
/// R46-io-table-style-bands-2-1: when a table's <c>tableStyleInfo</c> names a CUSTOM table style
/// (one defined in the workbook's own styles.xml &lt;tableStyles&gt;, recorded in
/// <see cref="Workbook.StructuredTableStyles"/>), the load pipeline already painted that style's exact
/// header/totals formatting onto the cells via XlsxStructuredTableModelMapper.MaterializeStyle
/// BEFORE this generic banding materializer runs. StructuredTableStyleBandingResolver only recognizes
/// Excel's built-in TableStyleLight/Medium/Dark name families, so a custom name fell through to the
/// generic default banding and unconditionally stomped the already-correct custom header/totals fill
/// (and bold/font) back to Excel's generic gray/black look.
///
/// R46-io-table-style-bands-2-2: the column-stripe pass always passed forceFill:true, which bypassed
/// the "preserve an explicit user fill" guard even for a fill that pre-dated this table being styled
/// at all (e.g. a user-set body-cell fill loaded straight from the source file) rather than one this
/// same method's own row-banding pass had just written moments earlier.
/// </summary>
public sealed class StructuredTableStyleBandingCustomStyleTests
{
    [Fact]
    public void ApplyLoadedTableStyles_DoesNotOverwriteAlreadyMaterializedCustomStyleHeader()
    {
        var workbook = new Workbook("CustomTableStyle");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 2);

        // Register the custom table style the workbook's styles.xml defines (mirrors what
        // XlsxStyleSheetReader/XlsxStructuredTableStyleMetadataWriter would produce for a
        // <tableStyle name="MyCustomStyle" table="1"> element).
        workbook.StructuredTableStyles.Add(new StructuredTableStyleModel
        {
            Name = "MyCustomStyle",
            AppliesToTables = true
        });

        // Simulate XlsxStructuredTableModelMapper.MaterializeStyle already having run at load time
        // and painted this custom style's header dxf (blue fill + white bold font) onto the header
        // row, BEFORE StructuredTableStyleService.ApplyLoadedTableStyles is invoked (matching the real
        // call order: XlsxFileAdapter.cs's per-sheet load loop calls MaterializeStyle, then
        // WorkbookOpenService calls ApplyLoadedTableStyles once for the whole workbook afterward).
        var customHeaderBlue = new CellColor(0x44, 0x72, 0xC4);
        var customHeaderWhite = new CellColor(0xFF, 0xFF, 0xFF);
        var customHeaderStyle = workbook.RegisterStyle(new CellStyle
        {
            FillColor = customHeaderBlue,
            FontColor = customHeaderWhite,
            Bold = true
        });
        sheet.GetCell(1, 1)!.StyleId = customHeaderStyle;
        sheet.GetCell(1, 2)!.StyleId = customHeaderStyle;

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 1,
            StyleName = "MyCustomStyle",
            ShowRowStripes = true
        };
        sheet.StructuredTables.Add(table);

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook);

        // The custom style's already-materialized header formatting must survive untouched — not be
        // stomped back to StructuredTableStyleBandingResolver's generic gray/black fallback banding.
        var header = workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId);
        header.FillColor.Should().Be(customHeaderBlue, "the custom table style's own header fill must win over generic banding");
        header.FontColor.Should().Be(customHeaderWhite, "the custom table style's own header font color must win over generic banding");
        header.Bold.Should().BeTrue();
    }

    // Sibling no-regression check: a table whose style name is NOT a registered custom style (the
    // overwhelmingly common case — a built-in TableStyleMedium2/Light1/etc name) must still get the
    // generic banding painted normally; the fix must not accidentally skip banding for every table.
    [Fact]
    public void ApplyLoadedTableStyles_StillPaintsGenericBandingForBuiltInStyleNameWithNoCustomEntry()
    {
        var workbook = new Workbook("BuiltInTableStyle");
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

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);
        var header = workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId);
        header.FillColor.Should().Be(banding.HeaderFill, "no registered custom style exists, so generic banding must still apply");
        header.Bold.Should().BeTrue();
    }

    [Fact]
    public void ApplyLoadedTableStyles_ColumnStripes_PreservesAnExplicitUserFillPreDatingTableStyling()
    {
        var workbook = new Workbook("ColumnStripesExplicitFill");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 2);

        // A body cell already carries an explicit user fill (e.g. loaded straight from the source
        // file, set directly in Excel via Home > Fill Color, independent of the table style).
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
            ShowRowStripes = false,
            ShowColumnStripes = true
        };
        sheet.StructuredTables.Add(table);

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        // Real Excel lets direct/explicit cell formatting override the table's automatic banding for
        // column stripes exactly as it does for row stripes — the explicit red fill must survive.
        workbook.GetStyle(sheet.GetCell(2, 1)!.StyleId).FillColor.Should().Be(userFill);
    }

    // Sibling no-regression check: column stripes must still paint normally onto body cells that have
    // no pre-existing explicit fill (the fix must not accidentally disable column banding entirely).
    [Fact]
    public void ApplyLoadedTableStyles_ColumnStripes_StillPaintsBandingOntoCellsWithNoPriorFill()
    {
        var workbook = new Workbook("ColumnStripesNormal");
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
            ShowRowStripes = false,
            ShowColumnStripes = true
        };
        sheet.StructuredTables.Add(table);

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);
        workbook.GetStyle(sheet.GetCell(2, 1)!.StyleId).FillColor.Should().Be(banding.EvenRowFill, "col 1 (offset 0) should be even");
        workbook.GetStyle(sheet.GetCell(2, 2)!.StyleId).FillColor.Should().Be(banding.OddRowFill, "col 2 (offset 1) should be odd");
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
}
