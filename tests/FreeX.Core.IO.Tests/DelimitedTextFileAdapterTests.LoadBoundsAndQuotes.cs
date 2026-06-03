using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class DelimitedTextFileAdapterTests
{
    [Fact]
    public void Load_IgnoresFieldsBeyondExcelColumnLimit()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        var fields = Enumerable.Repeat("", (int)CellAddress.MaxCol + 1).ToArray();
        fields[CellAddress.MaxCol - 1] = "last";
        fields[CellAddress.MaxCol] = "overflow";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\t', fields)));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, CellAddress.MaxCol)).Should().Be(new TextValue("last"));
        sheet.GetCell(new CellAddress(sheet.Id, 1, CellAddress.MaxCol + 1)).Should().BeNull();
    }

    [Fact]
    public void Load_IgnoresRecordsBeyondExcelRowLimit()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        var builder = new StringBuilder();
        for (var row = 1; row < CellAddress.MaxRow; row++)
            builder.AppendLine();
        builder.AppendLine("last");
        builder.AppendLine("overflow");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, CellAddress.MaxRow, 1)).Should().Be(new TextValue("last"));
        sheet.GetCell(new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 1)).Should().BeNull();
    }

    [Fact]
    public void Load_TreatsStandaloneCarriageReturnsAsRecordSeparators()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("A\tB\rC\tD\r"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("A"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("B"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("C"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new TextValue("D"));
    }

    [Fact]
    public void Load_KeepsQuotesInsideUnquotedFieldsAsLiteralText()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ab\"cd\t\"quoted\"\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("ab\"cd"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("quoted"));
    }

    [Fact]
    public void Load_ReadsFinalQuotedEmptyFieldWithoutTrailingNewline()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"\""));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue(""));
    }

    [Fact]
    public void Load_PreservesQuotedEmptyFieldBetweenPopulatedFields()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("left\t\"\"\tright\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("left"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue(""));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new TextValue("right"));
    }

}
