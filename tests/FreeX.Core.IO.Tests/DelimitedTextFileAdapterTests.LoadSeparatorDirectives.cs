using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class DelimitedTextFileAdapterTests
{
    [Fact]
    public void Load_HonorsExcelSeparatorDirectiveForTextFiles()
    {
        var adapter = new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=;\r\nName;Amount\r\nAlice;3.5\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
    }

    [Fact]
    public void Load_HonorsTabSeparatorDirectiveForTextFiles()
    {
        var adapter = new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=\t\r\nName\tAmount\r\nAlice\t3.5\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
    }

    [Fact]
    public void Load_KeepsQuotedSeparatorDirectiveAsLiteralText()
    {
        var adapter = new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"sep=;\"\r\nName\tAmount\r\nAlice\t3.5\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("sep=;"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new NumberValue(3.5));
    }

    [Fact]
    public void Load_KeepsQuoteSeparatorDirectiveAsLiteralText()
    {
        var adapter = new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=\"\r\nName\tAmount\r\nAlice\t3.5\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("sep=\""));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new NumberValue(3.5));
    }

}
