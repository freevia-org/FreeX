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
    [Fact]
    public void Save_WritesFormulaCellsAsExcelFormulaFields()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A1*2");

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("2,=A1*2\r\n");
    }

    [Fact]
    public void Save_DoesNotDuplicateLeadingEqualsInFormulaText()
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

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("=A1*2\r\n");
    }
}
