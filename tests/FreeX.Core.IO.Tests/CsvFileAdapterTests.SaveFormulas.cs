using System.Diagnostics;
using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit.Abstractions;

namespace FreeX.Core.IO.Tests;

public sealed partial class CsvFileAdapterTests
{
    // R23-csv-text-import-export-2: CSV has no formula syntax, so real Excel's CSV Save-As always
    // writes a formula cell's calculated Cell.Value, never its formula source text (e.g. "=A1*2").
    // This test previously asserted the wrong output "2,=A1*2\r\n" as if Excel exported the raw
    // formula text — corrected below to assert the computed value is exported instead.
    [Fact]
    public void Save_WritesFormulaCellsAsComputedValues()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A1*2");
        sheet.GetCell(new CellAddress(sheet.Id, 1, 2))!.Value = new NumberValue(4);

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("2,4\r\n");
    }

    // R23-csv-text-import-export-2: same fix — a cell carrying a formula whose text happens to
    // already start with "=" must still export its computed Value, not the formula text.
    [Fact]
    public void Save_WritesComputedValueEvenWhenFormulaTextHasLeadingEquals()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            FormulaText = "=A1*2",
            Value = new NumberValue(4)
        });

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("4\r\n");
    }
}
