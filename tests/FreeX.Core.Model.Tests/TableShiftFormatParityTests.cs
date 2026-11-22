using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit.Abstractions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Excel parity (recorded with COM ListRows.Add on a TableStyleMedium2 table with direct formats on data rows):
/// a row inserted inside a table copies cell formats from the row above, direct formats of existing rows travel with
/// their cells, and the style's row banding keeps alternating across the shift.
/// </summary>
public sealed class TableShiftFormatParityTests(ITestOutputHelper output)
{
    private static readonly CellColor Red = new(255, 0, 0);

    private static (Workbook Wb, Sheet Sheet, TestCommandContext Ctx) Build(int dataRows = 6)
    {
        var wb = new Workbook("t");
        var sheet = wb.AddSheet("S");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("H1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("H2"));
        for (uint r = 2; r <= (uint)dataRows + 1; r++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new TextValue($"a{r}"));
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r));
        }

        var table = new StructuredTableModel
        {
            Id = 1, Name = "Table1", DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, (uint)dataRows + 1, 2)),
            HeaderRowCount = 1, StyleName = "TableStyleMedium2", ShowRowStripes = true
        };
        sheet.StructuredTables.Add(table);
        StructuredTableStyleService.ApplyLoadedTableStyles(wb).Should().BeTrue();
        return (wb, sheet, ctx);
    }

    private static CellStyle Style(Workbook wb, Sheet sheet, uint row, uint col) =>
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, row, col))?.StyleId ?? sheet.GetStyleOnly(row, col) ?? StyleId.Default);

    private static void Apply(TestCommandContext ctx, Sheet sheet, uint row, uint col, StyleDiff diff) =>
        new ApplyStyleCommand(sheet.Id, new GridRange(new CellAddress(sheet.Id, row, col), new CellAddress(sheet.Id, row, col)), diff)
            .Apply(ctx).Success.Should().BeTrue();

    private string Dump(Workbook wb, Sheet sheet, uint lastRow)
    {
        var lines = new List<string>();
        for (uint r = 1; r <= lastRow; r++)
        {
            var parts = new List<string>();
            for (uint c = 1; c <= 2; c++)
            {
                var s = Style(wb, sheet, r, c);
                parts.Add($"[nf={s.NumberFormat} b={s.Bold} i={s.Italic} fill={s.FillColor}]");
            }

            lines.Add($"r{r} " + string.Join(" ", parts));
        }

        var text = string.Join("\n", lines);
        output.WriteLine(text);
        return text;
    }

    [Fact]
    public void InsertTableRow_Middle_CopiesCellFormatsFromTheRowAbove()
    {
        var (wb, sheet, ctx) = Build();
        Apply(ctx, sheet, 3, 2, new StyleDiff(NumberFormat: "0.00", Bold: true));
        Apply(ctx, sheet, 3, 1, new StyleDiff(Italic: true));

        new InsertTableRowsCommand(sheet.Id, 1, dataRowIndex: 2).Apply(ctx).Success.Should().BeTrue(); // before data row 3 => new sheet row 4
        Dump(wb, sheet, 9);

        // New row 4 sits below formatted row 3: Excel copies formats from the row above.
        Style(wb, sheet, 4, 2).NumberFormat.Should().Be("0.00");
        Style(wb, sheet, 4, 2).Bold.Should().BeTrue();
        Style(wb, sheet, 4, 1).Italic.Should().BeTrue();
        // The formatted original row moved down by one carrying its formats.
        Style(wb, sheet, 3, 2).NumberFormat.Should().Be("0.00");
    }

    [Fact]
    public void InsertTableRow_Middle_KeepsBandingAlternatingAndFormatsTravelWithCells()
    {
        var (wb, sheet, ctx) = Build();
        Apply(ctx, sheet, 5, 2, new StyleDiff(NumberFormat: "0.0%"));
        new InsertTableRowsCommand(sheet.Id, 1, dataRowIndex: 1).Apply(ctx).Success.Should().BeTrue();
        Dump(wb, sheet, 9);

        for (uint r = 2; r <= 6; r++)
            Style(wb, sheet, r, 1).FillColor.Should().Be(Style(wb, sheet, r + 2, 1).FillColor, $"row {r} and {r + 2} share a band");
        Style(wb, sheet, 2, 1).FillColor.Should().NotBe(Style(wb, sheet, 3, 1).FillColor, "adjacent rows alternate");
        Style(wb, sheet, 6, 2).NumberFormat.Should().Be("0.0%", "the formatted cell moved from row 5 to 6");
    }

    [Fact]
    public void DeleteTableRow_KeepsBandingAlternating()
    {
        var (wb, sheet, ctx) = Build();
        new DeleteTableRowsCommand(sheet.Id, 1, dataRowIndex: 1).Apply(ctx).Success.Should().BeTrue();
        Dump(wb, sheet, 7);

        for (uint r = 2; r <= 4; r++)
            Style(wb, sheet, r, 1).FillColor.Should().Be(Style(wb, sheet, r + 2, 1).FillColor);
        Style(wb, sheet, 2, 1).FillColor.Should().NotBe(Style(wb, sheet, 3, 1).FillColor);
    }

    [Fact]
    public void InsertTableColumn_CopiesCellFormatsFromTheColumnToTheLeft_AndKeepsBanding()
    {
        var (wb, sheet, ctx) = Build(4);
        for (uint r = 2; r <= 5; r++)
            Apply(ctx, sheet, r, 2, new StyleDiff(NumberFormat: "0.00", Bold: true));

        // Insert a column to the right of column B (Excel ListColumns.Add(3)).
        new InsertTableColumnsCommand(sheet.Id, 1, columnIndex: 2).Apply(ctx).Success.Should().BeTrue();
        Dump(wb, sheet, 5);

        for (uint r = 2; r <= 5; r++)
        {
            Style(wb, sheet, r, 3).NumberFormat.Should().Be("0.00", $"row {r} inherits the left column's number format");
            Style(wb, sheet, r, 3).Bold.Should().BeTrue();
            Style(wb, sheet, r, 3).FillColor.Should().Be(Style(wb, sheet, r, 1).FillColor, "banding is positional across the new column");
        }
    }
}
